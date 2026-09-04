using System;

namespace S7PlcWebserverApi
{
    /// <summary>A JSON-RPC call reached the PLC and was rejected by it.</summary>
    public class PlcException : Exception
    {
        public PlcException(string message, int? code = null, Exception inner = null)
            : base(message, inner) => Code = code;

        /// <summary>The CPU's own error code, when it reported one.</summary>
        public int? Code { get; }
    }

    /// <summary>The session token is missing, invalid or expired.</summary>
    public sealed class PlcAuthException : PlcException
    {
        public PlcAuthException(string message, int? code = null, Exception inner = null)
            : base(message, code, inner) { }
    }

    /// <summary>The PLC could not be reached at all.</summary>
    public sealed class PlcConnectionException : PlcException
    {
        public PlcConnectionException(string message, Exception inner = null)
            : base(message, null, inner) { }
    }
}
