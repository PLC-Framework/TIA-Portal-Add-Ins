namespace BlockTemplate.Manifest
{
    /// <summary>
    /// One entry of the manifest's <c>generatedTypes</c>: a PLC data type generated with the object,
    /// from a sub-template of its own, and named after the object.
    /// </summary>
    public sealed class GeneratedType
    {
        internal GeneratedType(string id, string prefix, string suffix)
        {
            Id = id;
            Prefix = prefix;
            Suffix = suffix;
        }

        /// <summary>
        /// The middle of its sub-template's file name:
        /// <c>&lt;base&gt;.&lt;id&gt;.v&lt;major&gt;.&lt;extension&gt;</c>. Anything a file name takes
        /// but a dot - so not always a word the template could write bare, which is why the template
        /// reaches its name as <c>types["seq-link"]</c> when it is not.
        /// </summary>
        public string Id { get; }

        public string Prefix { get; }

        public string Suffix { get; }

        /// <summary>The type's name for an object whose header says <paramref name="name"/>.</summary>
        public string NameFor(string name) => Prefix + name + Suffix;
    }
}
