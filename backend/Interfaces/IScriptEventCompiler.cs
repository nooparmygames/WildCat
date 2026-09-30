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

    public class EventParameter
    {
        public enum ParamType { BoolParam, ByteParam, ShortParam, IntParam, LongParam, FloatParam, DoubleParam, StringParam, ByteArrayParam }
        public ParamType Type { get; set; }
        public string Name { get; set; }

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

    public class ScriptEventState
    {
        public bool IsCreated { get; set; }
        public string Name { get; set; }
        public string Script { get; set; }
        public List<EventParameter> Parameters { get; set; }
        public ulong Version { get; set; }
        public bool HasReturnValue { get; set; }
        public string GameKey { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMsg { get; set; }
        public bool HasReturnValue { get; set; }
    }
}
