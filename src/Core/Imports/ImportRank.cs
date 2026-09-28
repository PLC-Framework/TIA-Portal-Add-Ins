namespace Core.Imports
{
    /// <summary>
    /// When a file is imported relative to the others, lowest first - the maintainer's order,
    /// 2026-09-28: PLC data types, then tag tables, then FCs and FBs, then data blocks, then
    /// OBs. A type has to exist before the block whose interface names it, and an FB before
    /// its instance DB.
    /// </summary>
    public enum ImportRank
    {
        Type,
        TagTable,

        /// <summary>FCs and FBs.</summary>
        Code,

        /// <summary>Every kind of data block, technology objects included.</summary>
        Data,

        /// <summary>OBs.</summary>
        Organization,

        /// <summary>
        /// A file whose objects could not be read. Last, and still imported: whether it goes
        /// in is TIA's to say, and TIA says why it would not.
        /// </summary>
        Unknown
    }
}
