namespace Core.Imports
{
    /// <summary>
    /// How TIA Portal takes a file in, which is decided by its extension and nothing else.
    /// Each is a different Openness call, measured identical in V20 and V21.
    /// </summary>
    public enum ImportFormat
    {
        /// <summary>
        /// <c>.xml</c>: <c>Import(FileInfo, ImportOptions)</c> on the composition of the folder
        /// it goes into - blocks, PLC data types, tag tables or technology objects.
        /// </summary>
        SimaticMl,

        /// <summary>
        /// <c>.s7dcl</c>, with the <c>.s7res</c> of the same name beside it when there is one:
        /// <c>ImportFromDocuments(DirectoryInfo, name, ImportDocumentOptions)</c>, which exists
        /// for blocks and PLC data types only.
        /// </summary>
        Document,

        /// <summary>
        /// <c>.scl</c>, <c>.udt</c>, <c>.db</c>, <c>.awl</c>: an external source created from
        /// the file, and the blocks generated from it into a folder. One source can declare
        /// several objects.
        /// </summary>
        Source
    }
}
