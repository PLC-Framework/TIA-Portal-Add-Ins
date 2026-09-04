namespace S7PlcWebserverApi
{
    /// <summary>Guards against a pathological data block locking up the caller.</summary>
    public static class PlcLimits
    {
        public const int MaxBrowseDepth = 12;
        public const int MaxBrowseNodes = 5000;
        public const int MaxArrayElements = 512;
        public const int ReadBatchSize = 50;
    }
}
