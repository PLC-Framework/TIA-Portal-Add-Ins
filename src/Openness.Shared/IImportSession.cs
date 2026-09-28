using System.Collections.Generic;

using Core.Imports;

namespace Openness.Shared
{
    /// <summary>
    /// What a satellite that imports asks of TIA Portal once attached: whether the folder is
    /// still there, what it would overwrite, and to take one file in. `Satellite.ImportObjects`
    /// is the first to ask it, and the template satellite after it imports what it generates the
    /// same way - which is why it lives here rather than in either.
    ///
    /// **One file per call**, so the window can say where it has got to and stop between two
    /// files when asked. **Nothing is compiled and nothing is rolled back** - the framework's rule
    /// wherever it writes into a project: TIA refuses an inconsistent block and compiling would
    /// change the project behind somebody who asked for an import, and Openness has no
    /// transaction, so what went in before a refusal stays, named in the report.
    /// </summary>
    public interface IImportSession : ITiaClient
    {
        /// <summary>
        /// Null when the place exists in the attached project, or why not - a folder renamed or
        /// deleted since it was right-clicked, a PLC that is not there.
        /// </summary>
        string Check(ImportPlace place);

        /// <summary>
        /// Which of <paramref name="names"/> the place's PLC - or unit - already holds among the
        /// objects of its tree, and where each one is. What the overwrite question is built from.
        /// </summary>
        IReadOnlyList<ExistingObject> Existing(ImportPlace place, IReadOnlyCollection<string> names);

        /// <summary>
        /// Takes one file in. **What it overwrites is overwritten where it is**, not in the place
        /// (the maintainer's decision, 2026-09-28); everything else goes into the place. Never
        /// throws for a refusal TIA gives - that comes back as <see cref="ImportOutcome.Refused"/>
        /// with TIA's reason.
        /// </summary>
        FileImport Import(ImportPlace place, FileToImport file);
    }
}
