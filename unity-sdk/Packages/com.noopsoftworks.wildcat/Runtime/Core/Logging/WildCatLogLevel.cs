namespace WildCat
{
    /// <summary>Log levels of <see cref="IWildCatLogger"/>, lowest first.</summary>
    public enum WildCatLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,

        /// <summary>Used as a minimum level: log nothing.</summary>
        None,
    }
}
