namespace BlockTemplate
{
    /// <summary>
    /// A value that differs between TIA Portal versions - a schema version, typically - and that
    /// the operator never sees: the executable running decides which version's value is used.
    /// </summary>
    public sealed class TiaVersionVariable
    {
        public TiaVersionVariable(string id, object value)
        {
            Id = id;
            Value = value;
        }

        public string Id { get; }

        /// <summary>A <see cref="string"/>, a <see cref="decimal"/> or a <see cref="bool"/>, as the file wrote it.</summary>
        public object Value { get; }
    }
}
