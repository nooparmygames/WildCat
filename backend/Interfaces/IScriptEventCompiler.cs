using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Interfaces
{
    public interface IScriptEventCompiler : IManagedResource
    {
        Task Initialize(string name, List<EventParameter> parameters, string script);
        Task Edit(List<EventParameter> parameters, string script);
        Task<object> Run(ISession session,string player,params object[] args);
        Task<ScriptEventState> GetInfo();
        Task<ValidationResult> Validate(string script);
        Task Compile(bool hasReturnValue);
    }

    [GenerateSerializer]
    [Alias("wildcat.EventParameter")]
    public class EventParameter
    {
        [GenerateSerializer, Alias("wildcat.EventParameter.ParamType")]
        public enum ParamType { BoolParam, ByteParam, ShortParam, IntParam, LongParam, FloatParam, DoubleParam, StringParam, ByteArrayParam }
        [Id(0)] public ParamType Type { get; set; }
        [Id(1)] public string Name { get; set; }

        public string GetCastTypeString()
        {
            switch(Type)
            {
                case ParamType.BoolParam:
                    return "bool";
                case ParamType.ByteParam:
                    return "byte";
                case ParamType.ByteArrayParam:
                    return "byte[Fact]";
                case ParamType.DoubleParam:
                    return "double";
                case ParamType.FloatParam:
                    return "float";
                case ParamType.IntParam:
                    return "int";
                case ParamType.LongParam:
                    return "long";
                case ParamType.ShortParam:
                    return "short";
                case ParamType.StringParam:
                    return "string";
                default:
                    throw new InvalidOperationException("Invalid parameter type");

            }
        }
    }

    [GenerateSerializer]
    [Alias("wildcat.ScriptEventState")]
    public class ScriptEventState
    {
        [Id(0)] public bool IsCreated { get; set; }
        [Id(1)] public string Name { get; set; }
        [Id(2)] public string Script { get; set; }
        [Id(3)] public List<EventParameter> Parameters { get; set; }
        [Id(4)] public ulong Version { get; set; }
        [Id(5)] public bool HasReturnValue { get; set; }
        [Id(6)] public string GameKey { get; set; }
    }

    [GenerateSerializer]
    [Alias("wildcat.ValidationResult")]
    public class ValidationResult
    {
        [Id(0)] public bool IsValid { get; set; }
        [Id(1)] public string ErrorMsg { get; set; }
        [Id(2)] public bool HasReturnValue { get; set; }
    }
}
