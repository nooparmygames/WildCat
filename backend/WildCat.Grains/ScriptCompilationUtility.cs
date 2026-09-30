using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using WildCat.Interfaces;
using RoslynScriptOptions = Microsoft.CodeAnalysis.Scripting.ScriptOptions;

namespace WildCat.Scripting
{
    /// <summary>
    /// How the event method hands back its result, which decides the generated call line.
    /// </summary>
    internal enum ScriptReturnKind
    {
        /// <summary><c>void s1(...)</c>: the call line is <c>s1(...);</c>.</summary>
        Void,
        /// <summary><c>Task s1(...)</c>: <c>await s1(...);</c>.</summary>
        Task,
        /// <summary><c>Task&lt;T&gt; s1(...)</c>: <c>return await s1(...);</c>.</summary>
        TaskOfValue,
        /// <summary><c>T s1(...)</c>: <c>return s1(...);</c>.</summary>
        Value
    }

    /// <summary>
    /// Everything needed to compile one version of a script event.
    /// </summary>
    internal sealed class ScriptSource
    {
        public ScriptSource(string gameKey, string eventName, IReadOnlyList<EventParameter> parameters, string code, string hash = null)
        {
            GameKey = gameKey ?? "";
            EventName = eventName ?? "";
            Parameters = parameters ?? Array.Empty<EventParameter>();
            Code = code ?? "";
            Hash = string.IsNullOrEmpty(hash) ? ScriptCompilationUtility.ComputeScriptHash(EventName, Parameters, Code) : hash;
        }

        public string GameKey { get; }
        public string EventName { get; }
        public IReadOnlyList<EventParameter> Parameters { get; }
        public string Code { get; }
        public string Hash { get; }

        public ScriptCacheKey CacheKey => new ScriptCacheKey(GameKey, EventName, Hash);

        public static ScriptSource FromState(ScriptEventState state)
        {
            return new ScriptSource(state.GameKey, state.Name, state.Parameters, state.Script, state.ScriptHash);
        }
    }

    /// <summary>
    /// A script event compiled with the sandbox guard calls, or the diagnostics that stopped it.
    /// </summary>
    public sealed class CompiledScript
    {
        internal CompiledScript(ScriptValidationResult validation, ScriptRunner<object> runner, int parameterCount)
        {
            Validation = validation;
            Runner = runner;
            ParameterCount = parameterCount;
        }

        public ScriptValidationResult Validation { get; }
        public bool IsValid => Runner != null;
        public int ParameterCount { get; }
        internal ScriptRunner<object> Runner { get; }
    }

    /// <summary>
    /// Validates and compiles script events. A developer writes a C# method named after the event
    /// (plus any helper methods and types); the server puts one generated line in front of it that
    /// casts the client's arguments and calls the method, validates the result against the
    /// sandbox rules (<see cref="ScriptValidator"/>), instruments it with
    /// <see cref="ScriptGuardRewriter"/> and compiles it with Roslyn. All of this is CPU-bound and
    /// runs on the thread pool.
    /// </summary>
    public static class ScriptCompilationUtility
    {
        /// <summary>
        /// Lines the server generates in front of the developer's script.
        /// </summary>
        internal const int GeneratedLineCount = 1;

        /// <summary>
        /// Longest script accepted, in characters.
        /// </summary>
        public const int MaxScriptLength = 100_000;

        public const string EventMethodMissing = "WC1007";
        public const string InvalidEventName = "WC1008";
        public const string ScriptTooLong = "WC1009";
        public const string InstrumentationFailed = "WC1010";
        public const string CompileTimedOut = "WC1011";
        public const string ReturnTypeNotAllowed = "WC1012";

        /// <summary>
        /// Longest a validation or compilation may take. Roslyn stops cooperatively, so a script
        /// written to make the compiler work exponentially cannot hold a thread for long.
        /// </summary>
        public static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(20);

        private static readonly Lazy<RoslynScriptOptions> options = new Lazy<RoslynScriptOptions>(BuildScriptOptions);

        private static readonly CSharpParseOptions parseOptions =
            new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.None, SourceCodeKind.Script);

        /// <summary>
        /// Roslyn options for script events. C# 12 is pinned because C# 13 and later prefer
        /// span-based overloads (params ReadOnlySpan, first-class spans) that the whitelist
        /// rejects, which would break ordinary calls such as Task.WhenAll(a, b). #r and #load
        /// resolve nothing, and unsafe code is off.
        /// </summary>
        public static RoslynScriptOptions CreateScriptOptions() => options.Value;

        private static RoslynScriptOptions BuildScriptOptions()
        {
            return RoslynScriptOptions.Default
                .WithReferences(
                    typeof(object).Assembly,
                    typeof(Enumerable).Assembly,
                    typeof(Queue<>).Assembly,
                    typeof(WildCatScriptApi).Assembly,
                    typeof(WildCat.Protocol.GetLeaderBoardResponse).Assembly)
                .WithImports("WildCat.Scripting", "System.Threading.Tasks", "System", "System.Collections.Generic", "System.Linq")
                .WithMetadataResolver(new NoDirectiveMetadataResolver(ScriptMetadataResolver.Default))
                .WithSourceResolver(NoSourceResolver.Instance)
                .WithAllowUnsafe(false)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithEmitDebugInformation(false)
                .WithLanguageVersion(LanguageVersion.CSharp12);
        }

        public static string ComputeScriptHash(string eventName, IEnumerable<EventParameter> parameters, string code)
        {
            var text = new StringBuilder();
            text.Append(eventName).Append('\n');
            foreach (var parameter in parameters ?? Enumerable.Empty<EventParameter>())
                text.Append((int)parameter.Type).Append(',');
            text.Append('\n').Append(code);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        public static EventParameter.ParamType GetTypeBasedOnParameter(object returnValue)
        {
            return GetReturnType(returnValue);
        }

        /// <summary>
        /// Whether a script may hand <paramref name="value"/> back to the client.
        /// </summary>
        public static bool IsAllowedReturnValue(object value)
        {
            return value == null || value is bool || value is byte || value is short || value is int || value is long
                || value is float || value is double || value is string || value is byte[];
        }

        public static EventParameter.ParamType GetReturnType(object returnValue)
        {
            Type t = returnValue.GetType();
            if (t == typeof(int))
                return EventParameter.ParamType.IntParam;
            if (t == typeof(short))
                return EventParameter.ParamType.ShortParam;
            if (t == typeof(long))
                return EventParameter.ParamType.LongParam;
            if (t == typeof(bool))
                return EventParameter.ParamType.BoolParam;
            if (t == typeof(byte[]))
                return EventParameter.ParamType.ByteArrayParam;
            if (t == typeof(byte))
                return EventParameter.ParamType.ByteParam;
            if (t == typeof(float))
                return EventParameter.ParamType.FloatParam;
            if (t == typeof(double))
                return EventParameter.ParamType.DoubleParam;
            if (t == typeof(string))
                return EventParameter.ParamType.StringParam;
            throw new ArgumentException("Invalid argument type");
        }

        /// <summary>
        /// Validates a script without compiling it to IL.
        /// </summary>
        internal static Task<ScriptValidationResult> ValidateAsync(ScriptSource source)
        {
            return Task.Run(() =>
            {
                using var timeout = new CancellationTokenSource(CompileTimeout);
                try
                {
                    return Analyze(source, timeout.Token).Validation;
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    return Fail(new ScriptValidationResult(), CompileTimedOut,
                        $"Validating the script took longer than {CompileTimeout.TotalSeconds} seconds.").Validation;
                }
            });
        }

        /// <summary>
        /// Validates, instruments and compiles a script. An invalid script comes back with
        /// <see cref="CompiledScript.IsValid"/> false and its diagnostics instead of throwing.
        /// </summary>
        /// <remarks>
        /// Throws TimeoutException (a faulted task) when compiling takes longer than
        /// <see cref="CompileTimeout"/>; that failure may be transient, so callers should not
        /// cache it.
        /// </remarks>
        internal static Task<CompiledScript> CompileAsync(ScriptSource source)
        {
            return Task.Run(() =>
            {
                using var timeout = new CancellationTokenSource(CompileTimeout);
                try
                {
                    return Compile(source, timeout.Token);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Compiling the script event '{source.EventName}' took longer than {CompileTimeout.TotalSeconds} seconds.");
                }
            });
        }

        internal static CompiledScript Compile(ScriptSource source, CancellationToken cancellationToken = default)
        {
            var analysis = Analyze(source, cancellationToken);
            if (!analysis.Validation.Success)
                return new CompiledScript(analysis.Validation, null, source.Parameters.Count);

            SyntaxNode instrumentedRoot;
            try
            {
                instrumentedRoot = new ScriptGuardRewriter(analysis.Model).Visit(analysis.Tree.GetRoot());
            }
            catch (InvalidOperationException ex)
            {
                return Failed(analysis.Validation, source, ex.Message);
            }

            var instrumented = CSharpScript.Create(instrumentedRoot.ToFullString(), CreateScriptOptions(), typeof(Globals));
            var errors = instrumented.Compile(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                return Failed(analysis.Validation, source, string.Join("; ", errors.Take(3).Select(e => $"{e.Id} {e.GetMessage()}")));

            return new CompiledScript(analysis.Validation, instrumented.CreateDelegate(cancellationToken), source.Parameters.Count);
        }

        private static CompiledScript Failed(ScriptValidationResult validated, ScriptSource source, string reason)
        {
            var result = new ScriptValidationResult
            {
                Success = false,
                HasReturnValue = validated.HasReturnValue,
                Diagnostics = new List<ScriptDiagnostic>(validated.Diagnostics)
                {
                    new ScriptDiagnostic
                    {
                        Severity = ScriptDiagnosticSeverity.Error,
                        Id = InstrumentationFailed,
                        Message = $"The sandbox could not instrument this script ({reason}). Please report this with the script."
                    }
                }
            };
            return new CompiledScript(result, null, source.Parameters.Count);
        }

        internal sealed class Analysis
        {
            public ScriptValidationResult Validation { get; init; }
            public SyntaxTree Tree { get; init; }
            public SemanticModel Model { get; init; }
        }

        internal static Analysis Analyze(ScriptSource source, CancellationToken cancellationToken = default)
        {
            var result = new ScriptValidationResult();
            string code = source.Code;

            if (code.Length > MaxScriptLength)
                return Fail(result, ScriptTooLong, $"The script is {code.Length} characters long; the limit is {MaxScriptLength}.");
            if (!SyntaxFacts.IsValidIdentifier(source.EventName) || SyntaxFacts.GetKeywordKind(source.EventName) != SyntaxKind.None)
                return Fail(result, InvalidEventName, $"'{source.EventName}' is not a valid event name; use a C# identifier.");

            // The developer's code on its own first: directives are rejected before Roslyn could
            // resolve them, and the event method's return type decides the generated call.
            var userRoot = CSharpSyntaxTree.ParseText(code, parseOptions, cancellationToken: cancellationToken).GetCompilationUnitRoot(cancellationToken);
            result.Diagnostics.AddRange(ScriptValidator.CheckDirectives(userRoot, location => MapUserLocation(location)));
            if (result.Diagnostics.Count > 0)
                return Finish(result);

            var method = userRoot.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == source.EventName);
            if (method == null)
            {
                return Fail(result, EventMethodMissing,
                    $"The script must declare a top-level method named {source.EventName} for the event.");
            }
            var returnKind = GetReturnKind(method.ReturnType);
            result.HasReturnValue = returnKind == ScriptReturnKind.TaskOfValue || returnKind == ScriptReturnKind.Value;

            string fullSource = BuildCallLine(source.EventName, source.Parameters, returnKind) + code;
            var script = CSharpScript.Create(fullSource, CreateScriptOptions(), typeof(Globals));
            var compilation = script.GetCompilation();
            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);

            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
            {
                if (diagnostic.Severity < DiagnosticSeverity.Warning)
                    continue;
                result.Diagnostics.Add(ToScriptDiagnostic(diagnostic, source.EventName));
            }
            result.Diagnostics.AddRange(new ScriptValidator(model, location => MapLocation(location)).Validate(tree.GetRoot(cancellationToken), cancellationToken));
            CheckEventReturnType(result, tree, model, source.EventName, returnKind, cancellationToken);
            return Finish(result, tree, model);
        }

        private static Analysis Fail(ScriptValidationResult result, string id, string message)
        {
            result.Diagnostics.Add(new ScriptDiagnostic { Severity = ScriptDiagnosticSeverity.Error, Id = id, Message = message });
            return Finish(result);
        }

        /// <summary>
        /// Sorts the diagnostics by position and sets <see cref="ScriptValidationResult.Success"/>.
        /// </summary>
        private static Analysis Finish(ScriptValidationResult result, SyntaxTree tree = null, SemanticModel model = null)
        {
            result.Diagnostics = result.Diagnostics
                .OrderBy(d => d.Line).ThenBy(d => d.Column)
                .ToList();
            result.Success = result.Diagnostics.All(d => d.Severity != ScriptDiagnosticSeverity.Error);
            return new Analysis { Validation = result, Tree = tree, Model = model };
        }

        /// <summary>
        /// The event's result goes back to the client, which understands the event parameter
        /// types only, so the method must return one of those (or object, checked at run time).
        /// </summary>
        private static void CheckEventReturnType(ScriptValidationResult result, SyntaxTree tree, SemanticModel model,
            string eventName, ScriptReturnKind returnKind, CancellationToken cancellationToken)
        {
            if (returnKind != ScriptReturnKind.Value && returnKind != ScriptReturnKind.TaskOfValue)
                return;
            var method = tree.GetCompilationUnitRoot(cancellationToken).Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == eventName);
            if (method == null || model.GetDeclaredSymbol(method, cancellationToken) is not IMethodSymbol symbol)
                return;
            ITypeSymbol valueType = symbol.ReturnType;
            if (returnKind == ScriptReturnKind.TaskOfValue)
                valueType = valueType is INamedTypeSymbol { IsGenericType: true } task ? task.TypeArguments[0] : null;
            if (valueType == null || valueType is IErrorTypeSymbol)
                return;
            bool allowed = valueType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte
                    or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64
                    or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_String
                    or SpecialType.System_Object
                || (valueType is IArrayTypeSymbol { Rank: 1 } array && array.ElementType.SpecialType == SpecialType.System_Byte);
            if (allowed)
                return;
            var (line, column) = MapLocation(method.ReturnType.GetLocation());
            result.Diagnostics.Add(new ScriptDiagnostic
            {
                Severity = ScriptDiagnosticSeverity.Error,
                Id = ReturnTypeNotAllowed,
                Message = $"The event method returns {valueType.ToDisplayString()}; events can return bool, byte, short, int, long, float, double, string or byte[] (or a Task of one).",
                Line = line,
                Column = column
            });
        }

        internal static ScriptReturnKind GetReturnKind(TypeSyntax returnType)
        {
            TypeSyntax type = returnType switch
            {
                QualifiedNameSyntax qualified => qualified.Right,
                AliasQualifiedNameSyntax alias => alias.Name,
                _ => returnType
            };
            switch (type)
            {
                case PredefinedTypeSyntax predefined when predefined.Keyword.IsKind(SyntaxKind.VoidKeyword):
                    return ScriptReturnKind.Void;
                case GenericNameSyntax generic when generic.Identifier.ValueText is "Task" or "ValueTask":
                    return ScriptReturnKind.TaskOfValue;
                case IdentifierNameSyntax identifier when identifier.Identifier.ValueText is "Task" or "ValueTask":
                    return ScriptReturnKind.Task;
                default:
                    return ScriptReturnKind.Value;
            }
        }

        /// <summary>
        /// The one line put in front of the developer's script. It is always exactly
        /// <see cref="GeneratedLineCount"/> line so diagnostics can be mapped back.
        /// </summary>
        internal static string BuildCallLine(string eventName, IReadOnlyList<EventParameter> parameters, ScriptReturnKind kind)
        {
            var arguments = string.Join(",", parameters.Select((p, i) => $"({p.GetCastTypeString()})args[{i}]"));
            string call = $"{eventName}({arguments})";
            return kind switch
            {
                ScriptReturnKind.Void => $"{call};\n",
                ScriptReturnKind.Task => $"await {call};\n",
                ScriptReturnKind.TaskOfValue => $"return await {call};\n",
                _ => $"return {call};\n"
            };
        }

        private static ScriptDiagnostic ToScriptDiagnostic(Diagnostic diagnostic, string eventName)
        {
            var (line, column) = MapLocation(diagnostic.Location);
            string message = diagnostic.GetMessage();
            if (line == 0 && diagnostic.Location.IsInSource)
            {
                message = $"In the generated call to {eventName}: {message} Check that the method's parameters match the event's parameter list.";
            }
            return new ScriptDiagnostic
            {
                Severity = (ScriptDiagnosticSeverity)(int)diagnostic.Severity,
                Id = diagnostic.Id,
                Message = message,
                Line = line,
                Column = column
            };
        }

        /// <summary>
        /// Maps a location in the full script (generated line plus developer code) to a 1-based
        /// line and column in the developer's code; the generated line maps to line 0.
        /// </summary>
        internal static (int Line, int Column) MapLocation(Location location)
        {
            if (location == null || !location.IsInSource)
                return (0, 0);
            var start = location.GetLineSpan().StartLinePosition;
            int line = start.Line - GeneratedLineCount;
            return line < 0 ? (0, 0) : (line + 1, start.Character + 1);
        }

        private static (int Line, int Column) MapUserLocation(Location location)
        {
            if (location == null || !location.IsInSource)
                return (0, 0);
            var start = location.GetLineSpan().StartLinePosition;
            return (start.Line + 1, start.Character + 1);
        }

        /// <summary>
        /// Resolves the assemblies Roslyn finds missing while binding (facades such as
        /// System.Runtime) but never a #r reference.
        /// </summary>
        private sealed class NoDirectiveMetadataResolver : MetadataReferenceResolver
        {
            private readonly MetadataReferenceResolver inner;

            public NoDirectiveMetadataResolver(MetadataReferenceResolver inner)
            {
                this.inner = inner;
            }

            public override bool ResolveMissingAssemblies => inner.ResolveMissingAssemblies;

            public override PortableExecutableReference ResolveMissingAssembly(MetadataReference definition, AssemblyIdentity referenceIdentity)
            {
                return inner.ResolveMissingAssembly(definition, referenceIdentity);
            }

            public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
            {
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            public override bool Equals(object other)
            {
                return other is NoDirectiveMetadataResolver resolver && Equals(inner, resolver.inner);
            }

            public override int GetHashCode()
            {
                return inner.GetHashCode();
            }
        }

        /// <summary>
        /// Resolves no #load file, so a script can never read from the server's disk.
        /// </summary>
        private sealed class NoSourceResolver : SourceReferenceResolver
        {
            public static readonly NoSourceResolver Instance = new NoSourceResolver();

            public override string NormalizePath(string path, string baseFilePath) => null;

            public override string ResolveReference(string path, string baseFilePath) => null;

            public override Stream OpenRead(string resolvedPath)
            {
                throw new NotSupportedException("Scripts cannot load files.");
            }

            public override bool Equals(object other) => other is NoSourceResolver;

            public override int GetHashCode() => 1;
        }
    }
}
