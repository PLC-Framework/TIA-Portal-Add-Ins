namespace Core.Repo.PlcCore
{
    /// <summary>One user constant of a tag table, as the workbook writes it.</summary>
    public sealed class PlcCoreConstant
    {
        internal PlcCoreConstant(string name, string dataType, string value, string comment)
        {
            Name = name;
            DataType = dataType;
            Value = value;
            Comment = comment;
        }

        public string Name { get; }

        /// <summary>As TIA spells it — <c>Int</c>, <c>UInt</c>, <c>Word</c>.</summary>
        public string DataType { get; }

        /// <summary>As the workbook writes it, <c>16#0000</c> included: TIA parses it, not this.</summary>
        public string Value { get; }

        public string Comment { get; }

        public override string ToString() => Name + " : " + DataType + " := " + Value;
    }
}
