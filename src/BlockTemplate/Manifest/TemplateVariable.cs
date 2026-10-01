namespace BlockTemplate.Manifest
{
    /// <summary>
    /// One entry of the manifest's <c>variables</c>: a value the operator fills in on the form,
    /// with a type that decides the field and a default it starts from.
    /// </summary>
    public sealed class TemplateVariable
    {
        internal TemplateVariable(string id, TemplateVariableType type, decimal? min, decimal? max, object @default)
        {
            Id = id;
            Type = type;
            Min = min;
            Max = max;
            Default = @default;
        }

        /// <summary>The name the template uses for it: <c>{{ sensors }}</c>.</summary>
        public string Id { get; }

        public TemplateVariableType Type { get; }

        /// <summary>
        /// The fewest entries of a list, or the smallest number. Null when the file sets none;
        /// a bool and a string never have one.
        /// </summary>
        public decimal? Min { get; }

        /// <summary>The most entries of a list, or the largest number. Null when the file sets none.</summary>
        public decimal? Max { get; }

        /// <summary>
        /// What the field starts with: an <c>IReadOnlyList&lt;string&gt;</c> for a list, a
        /// <see cref="bool"/>, a <see cref="decimal"/> or a <see cref="string"/>. **Never null**:
        /// a file that sets none gets an empty list, <c>false</c>, the minimum or zero, or an
        /// empty string.
        /// </summary>
        public object Default { get; }
    }
}
