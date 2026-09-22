using System;
using System.Collections.Generic;
using System.IO;

using Core.Exports;

namespace Core.Repo
{
    /// <summary>
    /// <c>repo\stranded\</c>: where an object's export waits while the object is out of the
    /// project, and where it stays when the object could not go back in.
    ///
    /// **Openness has no move**, so moving an object is an export, a delete and an import, and
    /// between the delete and the import the export is the only copy. A download taking an
    /// object out of the wrong folder does the same. **So the export is written here from the
    /// start** rather than written somewhere else and rescued on failure: when the object goes
    /// back in, the file is deleted; when it does not, it is already where no clean-up reaches,
    /// and the report names it.
    ///
    /// **It was `repo\tmp\` until 2026-09-22**, which the next run that ended well cleared -
    /// and every V17-V20 Load, whose map reads titles through that folder. A stranded block
    /// survived exactly until somebody did the next ordinary thing.
    /// </summary>
    public static class StrandedFiles
    {
        /// <summary>
        /// A path in <c>repo\stranded\</c> for an export about to be taken: <c>move-_queue.xml</c>,
        /// the folder made on the way.
        ///
        /// **Never one that is already there.** A file with that name may be the only copy of
        /// an object an earlier run stranded, and exporting over it would destroy it; the next
        /// free <c>move-_queue (2).xml</c> is taken instead.
        ///
        /// **Throws with a sentence when there is nowhere to write it**, and the callers ask for
        /// the path before they export - so a folder that cannot be made refuses the move before
        /// anything has been deleted.
        /// </summary>
        /// <param name="purpose">What the export is for, <c>move</c> or <c>replace</c> - so a file
        /// found here later says what happened to the object it holds.</param>
        public static string PathFor(string projectDirectory, string purpose, string name)
        {
            string folder = RepoPaths.StrandedFor(projectDirectory);

            if (folder == null)
                throw new InvalidOperationException(
                    "This project has no folder, so there is nowhere to keep the object while it is out of the project.");

            Directory.CreateDirectory(folder);

            string stem = purpose + "-" + ExportTree.Segment(name ?? string.Empty);
            string path = Path.Combine(folder, stem + ".xml");

            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(folder, stem + " (" + n + ").xml");

            return path;
        }

        /// <summary>
        /// Every export left in <c>repo\stranded\</c>, oldest first - each one an object that is
        /// no longer in the project, or one whose export could not be deleted after it went back.
        /// Empty when there are none or the folder cannot be read: this is a warning, never a
        /// reason to stop.
        /// </summary>
        public static IReadOnlyList<string> In(string projectDirectory)
        {
            List<string> files = new List<string>();
            string folder = RepoPaths.StrandedFor(projectDirectory);

            try
            {
                if (folder == null || !Directory.Exists(folder)) return files;

                FileInfo[] found = new DirectoryInfo(folder).GetFiles("*.xml");
                Array.Sort(found, (left, right) => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc));

                foreach (FileInfo one in found) files.Add(one.FullName);
            }
            catch (Exception)
            {
                // See above.
            }

            return files;
        }
    }
}
