using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.CSharp;
using Interfaces;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Orleans.Concurrency;
using Orleans;

namespace ScriptingAPI
{
    [Immutable]
    public class CompiledScript
    {
        public Script<object> Script;
        public ulong Version;
    }

    public static class ScriptCompilationUtility
    {
        private static List<string> types;
        private static Dictionary<string, CompiledScript> scripts;

        static ScriptCompilationUtility()
        {
            scripts = new Dictionary<string, CompiledScript>();
            types = new List<string>();
            types.Add(typeof(byte).Name);
            types.Add(typeof(short).Name);
            types.Add(typeof(int).Name);
            types.Add(typeof(long).Name);
            types.Add(typeof(string).Name);
            types.Add(typeof(bool).Name);
            types.Add(typeof(sbyte).Name);
            types.Add(typeof(ushort).Name);
            types.Add(typeof(uint).Name);
            types.Add(typeof(ulong).Name);
            types.Add(typeof(float).Name);
            types.Add(typeof(double).Name);
            types.Add(typeof(DateTime).Name);
            types.Add(typeof(TimeSpan).Name);
            types.Add(typeof(Task).Name);
            types.Add(typeof(RealtimeServer.GetLeaderBoardResponse).Name);
        }

        public static async Task<Script<object>> GetScript(IGrainFactory grainFactory, string gameKey, string eventName)
        {
            var key = ResourcesUtility.GetEventScriptKey(gameKey, eventName);
            var eventCompiler = grainFactory.GetGrain<IScriptEventCompiler>(key);
            var info = await eventCompiler.GetInfo();
            if (scripts.ContainsKey(key) && info.Version == scripts[key].Version)
                return scripts[key].Script;
            else
            {

                await eventCompiler.Compile(info.HasReturnValue);
                return scripts[key].Script;
            }
        }

        public static List<string> GetAllowedTypes()
        {
            return types;
        }

        public static EventParameter.ParamType GetTypeBasedOnParameter(object returnValue)
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


        public static async Task<Script<object>> CompileScript(string code, string eventName, List<EventParameter> parameters, bool hasReturnType, string gameKey, ulong version)
        {
            Script<object> script = null;
            await Task.Run(() =>
            {
                StringBuilder sb = CreateFunctionCall(eventName, parameters, hasReturnType);
                ScriptOptions options = CreateScriptOptions();
                script = CSharpScript.Create(sb.ToString() + "\n" + code, globalsType: typeof(Globals), options: options);
                script.Compile();
            });
            var key = ResourcesUtility.GetEventScriptKey(gameKey, eventName);
            scripts[key] = new CompiledScript { Script = script, Version = version };
            return script;
        }

        public static ScriptOptions CreateScriptOptions()
        {
            ScriptOptions options = ScriptOptions.Default
                .WithReferences(typeof(ScriptingAPI.Apadana).Assembly, typeof(RealtimeServer.GetLeaderBoardResponse).Assembly)
                .WithImports("ScriptingAPI", "System.Threading.Tasks", "System", "System.Collections.Generic");
            return options;
        }

        private static StringBuilder CreateFunctionCall(string name, List<EventParameter> parameters, bool hasReturnType)
        {
            var sb = new StringBuilder();
            if (hasReturnType)
                sb.Append($"return await {name}(");
            else
                sb.Append($"await {name}(");
            for (int i = 0; i < parameters.Count; ++i)
            {
                string type = parameters[i].GetCastTypeString();
                sb.Append($"({type})args[{i}]");
                if (i != parameters.Count - 1)
                    sb.Append(",");
            }
            sb.AppendLine(");");
            return sb;
        }

        public static async Task<ValidationResult> ValidateEventScript(string scriptText, string eventName)
        {
            ValidationResult validationResult = null;

            await Task.Run(async () =>
            {
                var allowed = ScriptCompilationUtility.GetAllowedTypes();
                ScriptOptions options = ScriptCompilationUtility.CreateScriptOptions();
                var scriptToValidate = CSharpScript.Create(scriptText, options);
                var compilation = scriptToValidate.GetCompilation();
                CSharpSyntaxTree tree = (CSharpSyntaxTree)compilation.SyntaxTrees.First();
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = await tree.GetRootAsync();
                var nodes = root.DescendantNodes();
                var scriptAssembly = compilation.Assembly;

                foreach (var node in nodes)
                {
                    var typeInfo = semanticModel.GetTypeInfo(node);
                    if (typeInfo.Type != null)
                    {
                        var type = typeInfo.Type;
                        var typeName = typeInfo.Type.Name;
                        if (type.TypeKind == TypeKind.Array)
                        {
                            type = ((IArrayTypeSymbol)typeInfo.Type).ElementType;
                            typeName = type.Name;
                        }


                        bool ok = (allowed.Contains(typeName) && (type.ContainingAssembly.Name == "mscorlib" || type.ContainingAssembly.Name == "System.Private.CoreLib"))
                            || type.ContainingAssembly == scriptAssembly
                            || (type.ContainingAssembly.Name == "Collections" && typeName == "Apadana")
                            || (type.ContainingAssembly.Name == "Messages" && typeName == "GetLeaderBoardResponse");

                        if (!ok)
                        {
                            validationResult = new ValidationResult { IsValid = false, ErrorMsg = "Script Contains Invalid type " + typeName };
                            return;
                        }
                    }
                }
                MethodDeclarationSyntax method = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>().
                    FirstOrDefault(x => x.Identifier.ValueText == eventName);
                if (method == null)
                {
                    validationResult = new ValidationResult
                    {
                        IsValid = false,
                        ErrorMsg = $"No method with the event name {eventName} could be found" +
                        $""
                    };
                    return;
                }

                var ret = semanticModel.GetTypeInfo(method.ReturnType);

                bool isGeneric = method.ReturnType is GenericNameSyntax;
                validationResult = new ValidationResult { IsValid = true, HasReturnValue = isGeneric };
            });
            return validationResult;
        }

        public static async Task<object> RunScript(Script<object> script, Globals globals)
        {
            TaskCompletionSource<ScriptState<object>> completionSource = new TaskCompletionSource<ScriptState<object>>();
            Task<ScriptState<object>> state = new TaskCompletionSource<ScriptState<object>>().Task;
            Thread t = new Thread(() =>
            {
                try
                {
                    state = script.RunAsync(globals: globals);
                }
                catch (Exception x)
                {

                    completionSource.SetException(new System.Exception(x.Message));
                    state = completionSource.Task;
                }
            });
            t.Start();
            await Task.WhenAny(state, Task.Delay(3000));
            if (!state.IsCompleted)
            {
                // The legacy code called t.Abort() here, which throws PlatformNotSupportedException on
                // .NET 5 and later. The script is abandoned instead: the caller still gets the timeout
                // after 3 seconds, but a runaway script keeps its thread. Real limits come with the
                // sandbox hardening (roadmap step 9).
                throw new TimeoutException("Script timed out");
            }
            return (await state).ReturnValue;
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
    }
}