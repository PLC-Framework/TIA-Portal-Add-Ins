namespace Core.Imports
{
    /// <summary>
    /// An object the PLC already holds under a name a chosen file declares: what the overwrite
    /// question is about. **It says where it is**, because that is where it is overwritten (the
    /// maintainer's decision, 2026-09-28) - which may not be the folder that was right-clicked.
    /// </summary>
    public sealed class ExistingObject
    {
        public ExistingObject(string name, string kind, string where)
        {
            Name = name;
            Kind = kind;
            Where = where;
        }

        /// <summary>The name as TIA spells it, which may differ in case from the file's.</summary>
        public string Name { get; }

        /// <summary>The kind as <see cref="DeclaredObject.Kind"/> spells it, or null when not known.</summary>
        public string Kind { get; }

        /// <summary>Its folder as a person reads it - the tree, then the folders below it.</summary>
        public string Where { get; }

        public override string ToString() => string.IsNullOrWhiteSpace(Where) ? Name : Name + " in " + Where;
    }
}
