using System.IO;

namespace Core.Logging
{
    /// <summary>
    /// The layout of <c>%LOCALAPPDATA%\PLC-Framework\logs\</c>: one file per application, and
    /// the name of each.
    ///
    /// **Per user rather than inside the TIA project** (2026-09-24, the maintainer's design).
    /// It is where the `.env`, the remembered credentials and the user template already live,
    /// and it buys four things the project folder could not: a log exists from the first line
    /// of `Main` rather than from whenever a project becomes known, a run that never reaches a
    /// project still leaves one, "send me the logs" is one folder, and nothing a run writes
    /// ends up in a versioned folder.
    ///
    /// **One file per application, not one per project**, which is the same choice the
    /// coding-style report made when it turned PLC, unit and object into columns rather than
    /// one joined path: a joined path can be read but never filtered. The project is a column
    /// on the line, so "what happened in Plant1" is a filter rather than a file - and the run
    /// would not know which file to open anyway, since the core updater learns its project
    /// from an attach that happens well after it starts writing.
    ///
    /// **Measured before it was chosen**: `Environment.GetFolderPath` demands
    /// `FileIOPermission`, which is refused under the tightest partial trust and granted by
    /// what `Config.xml` declares - the same permission writing into the project already
    /// needed, so the Add-In reaches this folder at no extra cost.
    /// </summary>
    public static class LogPaths
    {
        public const string AddInV20 = "add-in-v20";
        public const string AddInV21 = "add-in-v21";
        public const string CoreUpdater = "core-updater";
        public const string ConfigEditor = "config-editor";
        public const string CodingStyleReport = "coding-style-report";
        public const string DataBlockSnapshot = "data-block-snapshot";

        /// <summary>Inside <see cref="InstallPaths.UserRoot"/>.</summary>
        public const string Folder = "logs";

        private const string Extension = ".log";

        /// <summary>The one kept behind the current file when it grows past <see cref="Limit"/>.</summary>
        private const string Previous = ".1";

        /// <summary>
        /// When the current file is rolled, in bytes.
        ///
        /// **Four megabytes, where one was enough while a file held a single project.** Now
        /// every project's work shares one file per application, so the same megabyte holds a
        /// shorter history of each. A line measured 99 bytes and a walk of a four-hundred
        /// object PLC writes one per object, so this is about a hundred such walks. Six
        /// applications, each with its previous copy, is 48 MB in the worst case there is.
        /// </summary>
        public const long Limit = 4 * 1024 * 1024;

        /// <summary>
        /// One application's log, or null when the per-user folder cannot be resolved - which
        /// is what the tightest partial trust answers, and is an ordinary state meaning no log.
        /// </summary>
        public static string FileFor(string component)
        {
            string root = InstallPaths.UserRoot;

            return string.IsNullOrWhiteSpace(component) || string.IsNullOrWhiteSpace(root)
                ? null
                : Path.Combine(root, Folder, component + Extension);
        }

        /// <summary>Where <paramref name="file"/> is moved when it grows past the limit.</summary>
        public static string PreviousOf(string file) =>
            string.IsNullOrWhiteSpace(file) ? null : file + Previous;
    }
}
