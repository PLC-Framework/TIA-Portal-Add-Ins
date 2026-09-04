
namespace S7PlcWebserverApi
{
    /// <summary>The value read for one variable, or the reason it could not be read.</summary>
    public sealed class PlcValue
    {
        public string Path { get; }

        /// <summary>bool, long, double or string, as the CPU reported it. Null on failure.</summary>
        public object Value { get; }

        /// <summary>Null when the read succeeded.</summary>
        public string Error { get; }

        public bool Succeeded => Error == null;

        public PlcValue(string path, object value, string error)
        {
            Path = path;
            Value = value;
            Error = error;
        }
    }
}
