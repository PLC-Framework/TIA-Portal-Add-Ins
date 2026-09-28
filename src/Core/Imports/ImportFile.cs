using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Imports
{
    /// <summary>
    /// One file the operator chose, and what it holds - **the unit an import works in**
    /// (the maintainer's decision, 2026-09-28). A source that declares three blocks is one
    /// unit: TIA generates a source whole or not at all, so it is asked about, imported and
    /// reported as one.
    /// </summary>
    public sealed class ImportFile
    {
        internal ImportFile(string path, ImportFormat format, string companion,
                            IReadOnlyList<DeclaredObject> objects, string problem)
        {
            Path = path;
            Format = format;
            Companion = companion;
            Objects = objects ?? new DeclaredObject[0];
            Problem = problem;
            Rank = Objects.Count == 0 ? ImportRank.Unknown : Objects.Min(o => o.Rank);
        }

        /// <summary>The file as chosen. **TIA reads it from there**, with no copy made first.</summary>
        public string Path { get; }

        public string FileName => System.IO.Path.GetFileName(Path);

        public ImportFormat Format { get; }

        /// <summary>
        /// The <c>.s7res</c> beside a <c>.s7dcl</c>, or null when there is none - **whether or not
        /// it was ticked**. <c>ImportFromDocuments</c> takes a folder and a name, and reads
        /// whatever pair of that name the folder holds, so this says what TIA will read rather
        /// than what was chosen. Always null for the other two formats.
        /// </summary>
        public string Companion { get; }

        /// <summary>
        /// What the file declares, in the order it declares them. Empty when they could not be
        /// read, which <see cref="Problem"/> then says.
        /// </summary>
        public IReadOnlyList<DeclaredObject> Objects { get; }

        /// <summary>
        /// Why the objects inside could not be read, or null. **Not a refusal**: the file is
        /// still imported, and whether it goes in is TIA's to say. What is lost is knowing
        /// beforehand whether it would overwrite something.
        /// </summary>
        public string Problem { get; }

        /// <summary>
        /// Where the file goes in the order: the earliest its objects need. A source holding a
        /// data type and the block that uses it has to go in with the types.
        /// </summary>
        public ImportRank Rank { get; }

        /// <summary>
        /// The objects this file declares that are already in the PLC, by name and ignoring
        /// case, in the file's own order. What the overwrite list shows beside the file - empty
        /// means importing it overwrites nothing that could be seen.
        /// </summary>
        public IReadOnlyList<string> Meets(IEnumerable<string> existing)
        {
            if (existing == null) return new string[0];

            HashSet<string> names = new HashSet<string>(existing.Where(n => n != null), StringComparer.OrdinalIgnoreCase);
            return Objects.Where(o => names.Contains(o.Name)).Select(o => o.Name).ToList();
        }

        public override string ToString() => FileName;
    }
}
