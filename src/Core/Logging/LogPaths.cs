using System.IO;

using Core.Config;

namespace Core.Logging
{
    /// <summary>
    /// The layout of <c>.plc-framework\logs\</c>: one file per component, and the name of each.
    ///
    /// **One log per satellite and per Add-In** (2026-09-24, the maintainer's decision), which
    /// is also what keeps most writers apart: the Add-In and the satellite it launches are
    /// writing at the same moment and never to the same file. What it does not keep apart is
    /// two windows of the *same* satellite - the snapshot and the coding-style report carry no
    /// instance guard by design - and that case is <see cref="LogFile"/>'s to survive.
    ///
    /// **The names live here rather than at each call site**, for the reason
    /// <see cref="ConfigPaths"/> already records: a literal spelled in two places is two
    /// components one edit away from writing two different files.
    /// </summary>
    public static class LogPaths
    {
        public const string AddInV20 = "add-in-v20";
        public const string AddInV21 = "add-in-v21";
        public const string CoreUpdater = "core-updater";
        public const string ConfigEditor = "config-editor";
        public const string CodingStyleReport = "coding-style-report";
        public const string DataBlockSnapshot = "data-block-snapshot";

        private const string Extension = ".log";

        /// <summary>The one kept behind the current file when it grows past <see cref="Limit"/>.</summary>
        private const string Previous = ".1";

        /// <summary>
        /// When the current file is rolled, in bytes.
        ///
        /// **One megabyte, which is about twenty-five walks of a four-hundred-object PLC**:
        /// a line measured 99 bytes and such a walk writes one per object, so a Load costs
        /// some 40 KB. By size rather than by age (the maintainer's choice) because size is a
        /// property of the file already open, where age means reading the clock of every file
        /// in the folder to decide.
        /// </summary>
        public const long Limit = 1024 * 1024;

        /// <summary>
        /// One component's log inside a TIA project, or null when the project directory is
        /// unknown.
        ///
        /// **Null is an ordinary answer and means no log at all**: a project that was never
        /// saved has no folder to write into, which is the same rule the core updater already
        /// follows, and a satellite with no project of its own - the About window - never has
        /// one either.
        /// </summary>
        public static string FileFor(string projectDirectory, string component) =>
            string.IsNullOrWhiteSpace(component)
                ? null
                : Combine(ConfigPaths.FolderFor(projectDirectory, ConfigPaths.Logs), component + Extension);

        /// <summary>Where <paramref name="file"/> is moved when it grows past the limit.</summary>
        public static string PreviousOf(string file) =>
            string.IsNullOrWhiteSpace(file) ? null : file + Previous;

        private static string Combine(string folder, string name) =>
            string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, name);
    }
}
