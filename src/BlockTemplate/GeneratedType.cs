namespace BlockTemplate
{
    /// <summary>
    /// One entry of the CONFIG-JSON's <c>generatedTypes</c>: a PLC data type generated with the
    /// object, from a sub-template of its own, and named after the object.
    /// </summary>
    public sealed class GeneratedType
    {
        public GeneratedType(string id, string prefix, string suffix)
        {
            Id = id;
            Prefix = prefix;
            Suffix = suffix;
        }

        /// <summary>
        /// The middle of its sub-template's file name:
        /// <c>&lt;base&gt;.&lt;id&gt;.v&lt;major&gt;.&lt;extension&gt;</c>. Anything a file name takes
        /// but a dot - so not always a word the template could write bare.
        /// </summary>
        public string Id { get; }

        public string Prefix { get; }

        public string Suffix { get; }

        /// <summary>The type's name for an object whose header says <paramref name="name"/>.</summary>
        public string NameFor(string name) => Prefix + name + Suffix;
    }
}
