using System;
using System.Globalization;
using System.IO;

using Core.Config;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// Somewhere to put a block while its interface is read, and nothing more.
    ///
    /// Reading an interface means asking TIA to export the block, which writes a file, so one
    /// run of the check gets one folder of its own under
    /// <c>&lt;TIA project&gt;\.plc-framework\tmp\</c> and **gives it back when the run ends**.
    /// A project's code sitting in a temporary folder afterwards is somebody's source, in a
    /// place nobody looks, beside a folder under version control.
    ///
    /// **Inside the project rather than in %TEMP%**: the project directory is where the
    /// framework already writes, `tmp\` is named in <see cref="ConfigPaths"/> and the
    /// editor's .gitignore already covers it, so nothing here can be committed by accident.
    ///
    /// **A folder that cannot be made is not the end of the check.** The names still get
    /// checked; the objects whose interface was wanted say why it was not read, which is a
    /// report with a hole in it that names the hole.
    /// </summary>
    public sealed class ExportScratch
    {
        private const string RunPrefix = "coding-style-";

        private ExportScratch(string folder, string problem)
        {
            Folder = folder;
            Problem = problem;
        }

        /// <summary>The folder, or null when there is none - then <see cref="Problem"/> says why.</summary>
        public string Folder { get; }

        /// <summary>Why there is no folder. Null when there is one.</summary>
        public string Problem { get; }

        public bool Ready => Folder != null;

        /// <summary>
        /// A folder for this run, created. The timestamp is what keeps two checks running at
        /// once out of each other's way - the Add-In is a menu entry, and nothing stops the
        /// operator starting a second one.
        /// </summary>
        public static ExportScratch In(string projectDirectory, DateTime startedUtc)
        {
            string parent = ConfigPaths.FolderFor(projectDirectory, ConfigPaths.Tmp);

            if (parent == null)
                return new ExportScratch(null, "The project directory is unknown, so there was nowhere to export to.");

            string folder = Path.Combine(
                parent, RunPrefix + startedUtc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));

            try
            {
                Directory.CreateDirectory(folder);
                return new ExportScratch(folder, null);
            }
            catch (Exception exception)
            {
                return new ExportScratch(null, "'" + folder + "' could not be created: " + exception.Message);
            }
        }

        /// <summary>
        /// One file inside it, named after nothing in the project.
        ///
        /// **Not after the block**: a block name may hold characters a file name may not, and
        /// two blocks of one name live in two PLCs of the same project. A number cannot
        /// collide with either.
        /// </summary>
        public string FileFor(int number) =>
            Folder == null ? null : Path.Combine(Folder, number.ToString(CultureInfo.InvariantCulture) + ".xml");

        /// <summary>
        /// Deletes the folder and whatever is left in it. Never throws: the check has already
        /// produced its report by the time this runs, and losing that over a locked file
        /// would be the wrong trade.
        /// </summary>
        /// <returns>
        /// Null once the folder is gone, or why it is not. **Said rather than swallowed**, and
        /// this said the opposite for a while: that a file which survives is overwritten by the
        /// next run. It is not - every run gets a folder of its own, named by when it started - so
        /// one that will not go stays for good, holding exports of the project's source beside a
        /// folder under version control, with nothing anywhere to say it is there.
        /// </returns>
        public string Discard()
        {
            if (Folder == null) return null;

            try
            {
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }
    }
}
