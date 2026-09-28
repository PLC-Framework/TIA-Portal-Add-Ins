using System.Collections.Generic;

namespace Core.Imports
{
    /// <summary>
    /// One file on its way in, with what the operator said about it: whether it may overwrite
    /// what the PLC already holds under its names. A file the operator said no to is never one
    /// of these - it is left as it is, and nothing is asked of TIA about it.
    /// </summary>
    public sealed class FileToImport
    {
        public FileToImport(ImportFile file, IReadOnlyList<ExistingObject> existing)
        {
            File = file;
            Existing = existing ?? new ExistingObject[0];
        }

        public ImportFile File { get; }

        /// <summary>
        /// What it would overwrite, and where each one is - empty for a file that meets nothing.
        /// **Non-empty is the operator's yes**: the question was asked and this file was ticked.
        /// </summary>
        public IReadOnlyList<ExistingObject> Existing { get; }

        public bool Overwrites => Existing.Count > 0;
    }
}
