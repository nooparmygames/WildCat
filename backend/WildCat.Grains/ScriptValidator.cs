using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WildCat.Interfaces;

namespace WildCat.Scripting
{
    /// <summary>
    /// The sandbox rules for script events. Every syntax node of the compiled script (including
    /// the generated call line) is bound with the semantic model, and every type, member, local,
    /// parameter and conversion it touches must be on the whitelist: primitive types, types the
    /// script declares, the WildCat script API and its leaderboard results, and a small set of
    /// collection, LINQ, text and task types. Anything else (reflection through GetType or typeof,
    /// System.IO, System.Diagnostics, threading, Activator, dynamic, unsafe code, P/Invoke) is
    /// rejected. A few constructs are rejected by syntax because they would escape the runtime
    /// guard: lock, async void, destructors, static fields, records and anonymous types (their
    /// compiler-generated members recurse without guard calls).
    /// </summary>
    internal sealed class ScriptValidator
    {
        public const string TypeNotAllowed = "WC1001";
        public const string MemberNotAllowed = "WC1002";
        public const string ConstructNotAllowed = "WC1003";
        public const string AsyncVoidNotAllowed = "WC1004";
        public const string StaticStateNotAllowed = "WC1005";
        public const string DirectiveNotAllowed = "WC1006";

        private const int MaxDiagnostics = 50;

        private static readonly HashSet<string> TrustedAssemblies = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Linq",
            "System.Runtime.Extensions", "netstandard", "mscorlib", "WildCat.Grains", "WildCat.Protocol"
        };

        // Full metadata names of the library types scripts may use. Generic arguments are checked
        // separately; nested types are listed explicitly.
        private static readonly HashSet<string> AllowedTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Object", "System.Void", "System.Boolean", "System.Char", "System.SByte", "System.Byte",
            "System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
            "System.Single", "System.Double", "System.Decimal", "System.String", "System.DateTime",
            "System.TimeSpan", "System.DayOfWeek", "System.DateTimeKind", "System.Guid", "System.Math",
            "System.Random", "System.Convert", "System.Enum", "System.ValueType", "System.Array",
            "System.Nullable`1", "System.IDisposable", "System.IComparable`1", "System.IEquatable`1",
            "System.Index", "System.Range", "System.StringComparison", "System.StringSplitOptions",
            "System.MidpointRounding", "System.Comparison`1", "System.Predicate`1", "System.Converter`2", "System.Action",
            "System.Collections.IEnumerable", "System.Collections.IEnumerator",
            "System.Collections.Generic.List`1", "System.Collections.Generic.List`1+Enumerator",
            "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.Dictionary`2+Enumerator",
            "System.Collections.Generic.Dictionary`2+KeyCollection",
            "System.Collections.Generic.Dictionary`2+KeyCollection+Enumerator",
            "System.Collections.Generic.Dictionary`2+ValueCollection",
            "System.Collections.Generic.Dictionary`2+ValueCollection+Enumerator",
            "System.Collections.Generic.HashSet`1", "System.Collections.Generic.HashSet`1+Enumerator",
            "System.Collections.Generic.Queue`1", "System.Collections.Generic.Queue`1+Enumerator",
            "System.Collections.Generic.Stack`1", "System.Collections.Generic.Stack`1+Enumerator",
            "System.Collections.Generic.KeyValuePair`2", "System.Collections.Generic.IEnumerable`1",
            "System.Collections.Generic.IEnumerator`1", "System.Collections.Generic.ICollection`1",
            "System.Collections.Generic.IList`1", "System.Collections.Generic.IReadOnlyCollection`1",
            "System.Collections.Generic.IReadOnlyList`1", "System.Collections.Generic.IDictionary`2",
            "System.Collections.Generic.IReadOnlyDictionary`2", "System.Collections.Generic.ISet`1",
            "System.Collections.Generic.IReadOnlySet`1", "System.Collections.Generic.IComparer`1",
            "System.Collections.Generic.IEqualityComparer`1", "System.Collections.Generic.Comparer`1",
            "System.Collections.Generic.EqualityComparer`1", "System.Collections.Generic.CollectionExtensions",
            "System.Linq.Enumerable", "System.Linq.IOrderedEnumerable`1", "System.Linq.IGrouping`2",
            "System.Linq.ILookup`2",
            "System.Text.StringBuilder", "System.Text.StringBuilder+AppendInterpolatedStringHandler",
            "System.Threading.Tasks.Task", "System.Threading.Tasks.Task`1",
            "System.Exception", "System.InvalidOperationException", "System.ArgumentException",
            "WildCat.Scripting.WildCatScriptApi", "WildCat.Scripting.Globals",
            "WildCat.Protocol.GetLeaderBoardResponse", "WildCat.Protocol.LeaderBoardEntry"
        };

        // Families matched by prefix: Func`1..Func`17, Action`1..Action`16, ValueTuple`1..ValueTuple`8.
        private static readonly string[] AllowedTypePrefixes = { "System.Func`", "System.Action`", "System.ValueTuple" };

        // Types whose values could nest through object-typed slots; their library ToString, Equals
        // and GetHashCode recurse without guard calls, so their elements must not be object.
        private static readonly string[] NoObjectArguments = { "System.ValueTuple", "System.Collections.Generic.KeyValuePair`2" };

        private static readonly HashSet<string> TaskMembers = new HashSet<string>(StringComparer.Ordinal)
        {
            "CompletedTask", "FromResult", "WhenAll", "WhenAny", "IsCompleted", "IsCompletedSuccessfully",
            "IsFaulted", "IsCanceled"
        };

        private static readonly HashSet<string> DelegateMembers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Invoke", ".ctor"
        };

        private static readonly HashSet<string> ObjectDeniedMembers = new HashSet<string>(StringComparer.Ordinal)
        {
            "GetType", "MemberwiseClone", "Finalize"
        };

        private static readonly HashSet<string> GlobalsMembers = new HashSet<string>(StringComparer.Ordinal)
        {
            "args", "WildCat"
        };

        private readonly SemanticModel model;
        private readonly IAssemblySymbol scriptAssembly;
        private readonly Func<Location, (int Line, int Column)> map;
        private readonly List<ScriptDiagnostic> diagnostics = new List<ScriptDiagnostic>();
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);

        public ScriptValidator(SemanticModel model, Func<Location, (int Line, int Column)> map)
        {
            this.model = model;
            this.scriptAssembly = model.Compilation.Assembly;
            this.map = map;
        }

        /// <summary>
        /// Rejects #r, #load and #line before anything is compiled: #load and #r would read files
        /// from the server, and #line would make diagnostics point at the wrong lines.
        /// </summary>
        public static List<ScriptDiagnostic> CheckDirectives(SyntaxNode root, Func<Location, (int Line, int Column)> map)
        {
            var result = new List<ScriptDiagnostic>();
            foreach (var directive in root.DescendantNodes(descendIntoTrivia: true).OfType<DirectiveTriviaSyntax>())
            {
                if (directive is ReferenceDirectiveTriviaSyntax || directive is LoadDirectiveTriviaSyntax
                    || directive is LineDirectiveTriviaSyntax || directive is LineSpanDirectiveTriviaSyntax)
                {
                    var (line, column) = map(directive.GetLocation());
                    result.Add(new ScriptDiagnostic
                    {
                        Severity = ScriptDiagnosticSeverity.Error,
                        Id = DirectiveNotAllowed,
                        Message = $"The directive #{directive.DirectiveNameToken.ValueText} is not allowed in scripts.",
                        Line = line,
                        Column = column
                    });
                }
            }
            return result;
        }

        public List<ScriptDiagnostic> Validate(SyntaxNode root, CancellationToken cancellationToken = default)
        {
            foreach (var node in root.DescendantNodes())
            {
                if (diagnostics.Count >= MaxDiagnostics)
                    break;
                cancellationToken.ThrowIfCancellationRequested();
                CheckSyntax(node);
                CheckSymbol(node, model.GetSymbolInfo(node, cancellationToken).Symbol);
                var typeInfo = model.GetTypeInfo(node, cancellationToken);
                CheckType(node, typeInfo.Type);
                CheckType(node, typeInfo.ConvertedType);
                CheckDeclaration(node);
            }
            return diagnostics;
        }

        private void CheckSyntax(SyntaxNode node)
        {
            switch (node)
            {
                case UnsafeStatementSyntax:
                case FixedStatementSyntax:
                case StackAllocArrayCreationExpressionSyntax:
                case ImplicitStackAllocArrayCreationExpressionSyntax:
                case PointerTypeSyntax:
                case FunctionPointerTypeSyntax:
                case MakeRefExpressionSyntax:
                case RefTypeExpressionSyntax:
                case RefValueExpressionSyntax:
                case ExternAliasDirectiveSyntax:
                    Report(node, ConstructNotAllowed, "Unsafe code, pointers, stackalloc and other low-level constructs are not allowed in scripts.");
                    return;
                case LockStatementSyntax:
                    Report(node, ConstructNotAllowed, "lock is not allowed in scripts; a blocked lock cannot be stopped by the script time limit.");
                    return;
                case DestructorDeclarationSyntax:
                    Report(node, ConstructNotAllowed, "Finalizers are not allowed in scripts; they run outside the script's time limit.");
                    return;
                case RecordDeclarationSyntax:
                    Report(node, ConstructNotAllowed, "Records are not allowed in scripts; declare a class or struct instead.");
                    return;
                case AnonymousObjectCreationExpressionSyntax:
                    Report(node, ConstructNotAllowed, "Anonymous types are not allowed in scripts; declare a class or struct instead.");
                    return;
                case AnonymousFunctionExpressionSyntax function:
                    if (function.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)
                        && model.GetSymbolInfo(function).Symbol is IMethodSymbol { ReturnsVoid: true })
                    {
                        Report(node, AsyncVoidNotAllowed, "Async lambdas must return a Task; async void would crash the server on an exception.");
                    }
                    return;
                case ExpressionSyntax expression when expression.IsKind(SyntaxKind.ArgListExpression):
                    Report(node, ConstructNotAllowed, "__arglist is not allowed in scripts.");
                    return;
                case MethodDeclarationSyntax method:
                    CheckModifiers(node, method.Modifiers);
                    CheckAsyncVoid(node, method.Modifiers, method.ReturnType);
                    return;
                case LocalFunctionStatementSyntax localFunction:
                    CheckModifiers(node, localFunction.Modifiers);
                    CheckAsyncVoid(node, localFunction.Modifiers, localFunction.ReturnType);
                    return;
                case FieldDeclarationSyntax field:
                    CheckModifiers(node, field.Modifiers);
                    if (field.Modifiers.Any(SyntaxKind.StaticKeyword) && !field.Modifiers.Any(SyntaxKind.ConstKeyword))
                        Report(node, StaticStateNotAllowed, "Static fields are not allowed in scripts; every run and every player must start from a clean state. Use const, or store data through the WildCat API.");
                    return;
                case EventFieldDeclarationSyntax eventField:
                    CheckModifiers(node, eventField.Modifiers);
                    if (eventField.Modifiers.Any(SyntaxKind.StaticKeyword))
                        Report(node, StaticStateNotAllowed, "Static events are not allowed in scripts.");
                    return;
                case PropertyDeclarationSyntax property:
                    CheckModifiers(node, property.Modifiers);
                    if (property.Modifiers.Any(SyntaxKind.StaticKeyword) && IsAutoProperty(property))
                        Report(node, StaticStateNotAllowed, "Static auto-properties are not allowed in scripts; they keep state between runs.");
                    return;
                case BaseMethodDeclarationSyntax member:
                    CheckModifiers(node, member.Modifiers);
                    return;
                case BasePropertyDeclarationSyntax member:
                    CheckModifiers(node, member.Modifiers);
                    return;
                case BaseTypeDeclarationSyntax type:
                    CheckModifiers(node, type.Modifiers);
                    return;
            }
        }

        private static bool IsAutoProperty(PropertyDeclarationSyntax property)
        {
            return property.ExpressionBody == null
                && (property.Initializer != null
                    || (property.AccessorList != null
                        && property.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null)));
        }

        private void CheckModifiers(SyntaxNode node, SyntaxTokenList modifiers)
        {
            if (modifiers.Any(SyntaxKind.UnsafeKeyword) || modifiers.Any(SyntaxKind.FixedKeyword))
                Report(node, ConstructNotAllowed, "Unsafe code is not allowed in scripts.");
            if (modifiers.Any(SyntaxKind.ExternKeyword))
                Report(node, ConstructNotAllowed, "extern methods (P/Invoke) are not allowed in scripts.");
        }

        private void CheckAsyncVoid(SyntaxNode node, SyntaxTokenList modifiers, TypeSyntax returnType)
        {
            if (modifiers.Any(SyntaxKind.AsyncKeyword) && returnType is PredefinedTypeSyntax predefined
                && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
            {
                Report(node, AsyncVoidNotAllowed, "async void is not allowed in scripts; return a Task instead.");
            }
        }

        private void CheckDeclaration(SyntaxNode node)
        {
            ISymbol declared;
            try
            {
                declared = model.GetDeclaredSymbol(node);
            }
            catch (ArgumentException)
            {
                return;
            }
            switch (declared)
            {
                case null:
                    return;
                case INamedTypeSymbol type:
                    // Explicit base types are checked where they appear in the base list; this
                    // only stops script exception types, which the server would have to serialize.
                    for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
                    {
                        if (baseType.SpecialType == SpecialType.None && MetadataName(baseType) == "System.Exception")
                        {
                            Report(node, ConstructNotAllowed, "Script types cannot derive from Exception; throw InvalidOperationException or ArgumentException with a message.");
                            break;
                        }
                    }
                    return;
                case IMethodSymbol method:
                    if (method.IsVararg)
                        Report(node, ConstructNotAllowed, "__arglist is not allowed in scripts.");
                    if (!method.ReturnsVoid)
                        CheckType(node, method.ReturnType);
                    foreach (var parameter in method.Parameters)
                        CheckType(node, parameter.Type);
                    return;
                case IFieldSymbol field when IsScriptStruct(field.ContainingType) && CanHoldBoxedStruct(field.Type):
                case IPropertySymbol property when IsScriptStruct(property.ContainingType) && CanHoldBoxedStruct(property.Type):
                    Report(node, ConstructNotAllowed,
                        "Fields of a script struct cannot be object, an interface or a type parameter; the runtime's default Equals and GetHashCode for structs would follow them without the script's time and depth limits. Use a class or a concrete type.");
                    return;
                default:
                    CheckSymbol(node, declared);
                    return;
            }
        }

        private bool IsScriptStruct(INamedTypeSymbol type)
        {
            return type != null && type.TypeKind == TypeKind.Struct && IsScriptType(type);
        }

        private static bool CanHoldBoxedStruct(ITypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_Object
                || type.SpecialType == SpecialType.System_ValueType
                || type.TypeKind == TypeKind.Interface
                || type.TypeKind == TypeKind.TypeParameter;
        }

        private void CheckSymbol(SyntaxNode node, ISymbol symbol)
        {
            switch (symbol)
            {
                case null:
                case INamespaceSymbol:
                case ILabelSymbol:
                case IRangeVariableSymbol:
                case IPreprocessingSymbol:
                    return;
                case ITypeSymbol type:
                    CheckType(node, type);
                    return;
                case IAliasSymbol alias:
                    CheckSymbol(node, alias.Target);
                    return;
                case ILocalSymbol local:
                    CheckType(node, local.Type);
                    return;
                case IParameterSymbol parameter:
                    CheckType(node, parameter.Type);
                    return;
                case IDiscardSymbol discard:
                    CheckType(node, discard.Type);
                    return;
                case IMethodSymbol method:
                    CheckMethod(node, method);
                    return;
                case IPropertySymbol property:
                    if (CheckMember(node, property))
                    {
                        CheckType(node, property.Type);
                        foreach (var parameter in property.Parameters)
                            CheckType(node, parameter.Type);
                    }
                    return;
                case IFieldSymbol field:
                    if (CheckMember(node, field))
                        CheckType(node, field.Type);
                    return;
                case IEventSymbol eventSymbol:
                    if (CheckMember(node, eventSymbol))
                        CheckType(node, eventSymbol.Type);
                    return;
                default:
                    Report(node, MemberNotAllowed, $"'{symbol.ToDisplayString()}' is not allowed in scripts.");
                    return;
            }
        }

        private void CheckMethod(SyntaxNode node, IMethodSymbol method)
        {
            if (method.MethodKind == MethodKind.FunctionPointerSignature || method.IsExtern || method.IsVararg)
            {
                Report(node, MemberNotAllowed, $"'{method.ToDisplayString()}' is not allowed in scripts.");
                return;
            }
            var definition = method.ReducedFrom ?? method;
            if (definition.MethodKind != MethodKind.AnonymousFunction && definition.MethodKind != MethodKind.LocalFunction
                && !CheckMember(node, definition))
            {
                return;
            }
            if (!method.ReturnsVoid)
                CheckType(node, method.ReturnType);
            foreach (var parameter in method.Parameters)
                CheckType(node, parameter.Type);
            foreach (var argument in method.TypeArguments)
                CheckType(node, argument);
        }

        /// <summary>
        /// Checks that a member belongs to an allowed type and is not one of the denied members
        /// of an allowed type. Returns false after reporting.
        /// </summary>
        private bool CheckMember(SyntaxNode node, ISymbol member)
        {
            var containing = member.ContainingType;
            if (containing == null || IsScriptType(containing))
                return true;
            if (!IsAllowedType(containing, out string offending))
            {
                Report(node, TypeNotAllowed, $"The type '{offending}' is not allowed in scripts.");
                return false;
            }

            string typeName = MetadataName(containing.OriginalDefinition);
            string name = member.Name;
            bool denied =
                (typeName == "System.Object" && ObjectDeniedMembers.Contains(name))
                || ((typeName == "System.Threading.Tasks.Task" || typeName == "System.Threading.Tasks.Task`1") && !TaskMembers.Contains(name))
                || (containing.TypeKind == TypeKind.Delegate && !DelegateMembers.Contains(name))
                || (typeName == "WildCat.Scripting.WildCatScriptApi" && name == ".ctor")
                || (typeName == "WildCat.Scripting.Globals" && !GlobalsMembers.Contains(name))
                || (member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor && IsException(containing)
                    && constructor.Parameters.Any(p => IsException(p.Type)));
            if (denied)
            {
                Report(node, MemberNotAllowed, $"'{containing.Name}.{name}' is not allowed in scripts.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Exceptions a script creates carry only a message: an inner exception would let a
        /// script build an arbitrarily long chain that ToString and the grain response serializer
        /// walk recursively.
        /// </summary>
        private static bool IsException(ITypeSymbol type)
        {
            for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                if (MetadataName(current.OriginalDefinition) == "System.Exception")
                    return true;
            }
            return false;
        }

        private void CheckType(SyntaxNode node, ITypeSymbol type)
        {
            if (type != null && !IsAllowedType(type, out string offending))
                Report(node, TypeNotAllowed, $"The type '{offending}' is not allowed in scripts.");
        }

        private bool IsAllowedType(ITypeSymbol type, out string offending)
        {
            offending = null;
            switch (type)
            {
                case IErrorTypeSymbol:
                case ITypeParameterSymbol:
                    return true;
                case IArrayTypeSymbol array:
                    return IsAllowedType(array.ElementType, out offending);
                case IPointerTypeSymbol:
                case IFunctionPointerTypeSymbol:
                case IDynamicTypeSymbol:
                    offending = type.ToDisplayString();
                    return false;
                case INamedTypeSymbol named:
                    return IsAllowedNamedType(named, out offending);
                default:
                    offending = type.ToDisplayString();
                    return false;
            }
        }

        private bool IsAllowedNamedType(INamedTypeSymbol type, out string offending)
        {
            offending = null;
            if (!IsScriptType(type))
            {
                string name = MetadataName(type.OriginalDefinition);
                bool listed = AllowedTypes.Contains(name) || AllowedTypePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
                if (!listed || type.ContainingAssembly == null || !TrustedAssemblies.Contains(type.ContainingAssembly.Name))
                {
                    offending = type.ToDisplayString();
                    return false;
                }
                if (NoObjectArguments.Any(p => name.StartsWith(p, StringComparison.Ordinal))
                    && type.TypeArguments.Any(a => a.SpecialType == SpecialType.System_Object || a.SpecialType == SpecialType.System_ValueType))
                {
                    offending = type.ToDisplayString() + " (tuple and key-value pair elements cannot be object)";
                    return false;
                }
            }
            foreach (var argument in type.TypeArguments)
            {
                if (!IsAllowedType(argument, out offending))
                    return false;
            }
            if (type.ContainingType != null && !IsAllowedNamedType(type.ContainingType, out offending))
                return false;
            return true;
        }

        private bool IsScriptType(INamedTypeSymbol type)
        {
            return SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, scriptAssembly);
        }

        private static string MetadataName(INamedTypeSymbol type)
        {
            if (type.ContainingType != null)
                return MetadataName(type.ContainingType) + "+" + type.MetadataName;
            var ns = type.ContainingNamespace;
            return ns == null || ns.IsGlobalNamespace ? type.MetadataName : ns.ToDisplayString() + "." + type.MetadataName;
        }

        private void Report(SyntaxNode node, string id, string message)
        {
            var (line, column) = map(node.GetLocation());
            if (!reported.Add($"{line}|{id}|{message}"))
                return;
            diagnostics.Add(new ScriptDiagnostic
            {
                Severity = ScriptDiagnosticSeverity.Error,
                Id = id,
                Message = message,
                Line = line,
                Column = column
            });
        }
    }
}
