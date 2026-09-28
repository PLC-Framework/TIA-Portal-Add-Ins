using System.Collections.Generic;

namespace Core.Imports
{
    /// <summary>
    /// What the operator chose, as it will be imported: the files in the order they go in,
    /// and every one that will not, each with its reason.
    /// </summary>
    public sealed class ImportSelection
    {
        internal ImportSelection(IReadOnlyList<ImportFile> files, IReadOnlyList<string> refused)
        {
            Files = files;
            Refused = refused;
        }

        /// <summary>
        /// In the order they will be imported: by <see cref="ImportRank"/>, then by file name,
        /// so the same choice always goes in the same way.
        /// </summary>
        public IReadOnlyList<ImportFile> Files { get; }

        /// <summary>One sentence per chosen file that is not a unit of its own, naming it.</summary>
        public IReadOnlyList<string> Refused { get; }
    }
}
