namespace BlockTemplate.Checking.SimaticMl
{
    /// <summary>
    /// The SimaticML elements and attributes the check knows, each spelled once. Matched by local
    /// name, never by namespace: a network is <c>…/FlgNet/v5</c> in V20 and V21 alike today, and a
    /// <c>v6</c> must not make every network invisible.
    /// </summary>
    internal static class SimaticMlNames
    {
        public const string NetworkSource = "NetworkSource";
        public const string FlgNet = "FlgNet";
        public const string Parts = "Parts";
        public const string Wires = "Wires";
        public const string Wire = "Wire";
        public const string OpenCon = "OpenCon";
        public const string IdentCon = "IdentCon";
        public const string NameCon = "NameCon";
        public const string Access = "Access";
        public const string Part = "Part";
        public const string Call = "Call";

        public const string UId = "UId";
        public const string Id = "ID";
    }
}
