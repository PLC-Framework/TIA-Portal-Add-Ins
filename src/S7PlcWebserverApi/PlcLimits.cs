namespace S7PlcWebserverApi
{
    /// <summary>Guards against a pathological data block locking up the caller.</summary>
    public static class PlcLimits
    {
        public const int MaxBrowseDepth = 12;

        // Runaway guards, not policy. The values inherited from the reference
        // implementation - 5000 nodes and 512 array elements - truncate an ordinary
        // data block: a DB filled to the CPU's memory limit browses well past both.
        // These are set where a genuine loop would still be caught but real data is not.
        public const int MaxBrowseNodes = 200000;
        public const int MaxArrayElements = 65535;

        /// <summary>Variables asked for in one JSON-RPC batch POST.</summary>
        public const int ReadBatchSize = 50;

        /// <summary>Nodes browsed in one JSON-RPC batch POST, all from the same tree level.</summary>
        public const int BrowseBatchSize = 50;
    }
}
