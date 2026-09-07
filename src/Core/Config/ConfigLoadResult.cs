namespace Core.Config
{
    /// <summary>
    /// Outcome of reading config.json. The failure travels as data rather than as an
    /// exception because every consumer needs the message, not the stack: the Add-In
    /// shows it in a TIA notification, the satellite next to the offending field.
    /// Neither wants to catch exceptions to find out what went wrong.
    /// </summary>
    public sealed class ConfigLoadResult
    {
        private ConfigLoadResult(Config config, string error)
        {
            Config = config;
            Error = error;
        }

        /// <summary>The parsed configuration, or null when loading failed.</summary>
        public Config Config { get; }

        /// <summary>Why loading failed, or null when it succeeded.</summary>
        public string Error { get; }

        public bool Succeeded => Config != null;

        internal static ConfigLoadResult Loaded(Config config) => new ConfigLoadResult(config, null);

        internal static ConfigLoadResult Failed(string error) => new ConfigLoadResult(null, error);
    }
}
