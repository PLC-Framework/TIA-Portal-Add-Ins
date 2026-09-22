namespace Core.Repo
{
    /// <summary>One object to move, and where from and to.</summary>
    public sealed class MisplacedObject
    {
        internal MisplacedObject(string name, string kind, string from, string family)
        {
            Name = name;
            Kind = kind;
            From = from;
            Family = family;
        }

        /// <summary>
        /// The object's own name. **This is what finds it**, not the folder: a block's name is
        /// unique across a PLC's software, so a walk by name is exact where a path would have to
        /// be taken apart into a tree root whose name follows the interface language.
        /// </summary>
        public string Name { get; }

        /// <summary>As the map spells it - <c>FB</c>, <c>GlobalDB</c>, <c>PlcStruct</c>, <c>PlcTagTable</c>.</summary>
        public string Kind { get; }

        /// <summary>Where it is now, as the map wrote it. For the report; nothing resolves it.</summary>
        public string From { get; }

        /// <summary>Where the core says it belongs: <c>core/adt/queue</c>.</summary>
        public string Family { get; }

        public override string ToString() => Name + ": " + From + " -> " + Family;
    }
}
