namespace WildCat.Protocol
{
    /// <summary>
    /// The types a script event argument or return value can have. Keep in step with
    /// EventParameter.ParamType in WildCat.Interfaces (IScriptEventCompiler.cs).
    /// </summary>
    public enum ParamType { BoolParam, ByteParam, ShortParam, IntParam, LongParam, FloatParam, DoubleParam, StringParam, ByteArrayParam }
}
