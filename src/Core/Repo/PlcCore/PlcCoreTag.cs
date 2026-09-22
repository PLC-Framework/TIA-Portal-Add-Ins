namespace Core.Repo.PlcCore
{
    /// <summary>One PLC tag of a tag table.</summary>
    public sealed class PlcCoreTag
    {
        internal PlcCoreTag(string name, string dataType, string address, string comment)
        {
            Name = name;
            DataType = dataType;
            Address = address;
            Comment = comment;
        }

        public string Name { get; }

        public string DataType { get; }

        /// <summary>The logical address, <c>%MB0</c>. Empty for a tag that has none.</summary>
        public string Address { get; }

        public string Comment { get; }

        public override string ToString() => Name + " : " + DataType + " " + Address;
    }
}
