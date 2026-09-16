using System.IO;

using Core.Config;

namespace Core.Repo
{
    /// <summary>
    /// What lives inside <c>.plc-framework\repo\</c>, the project's own workspace for the
    /// core it is built on.
    ///
    /// <code>
    /// &lt;TIA project&gt;\.plc-framework\repo\
    /// +-- core\          the core, copied out of the repository exactly as it was
    /// +-- project.json   what the TIA project actually holds, as this Add-In read it
    /// +-- tmp\           what a download is writing before anything is imported
    /// </code>
    ///
    /// **The copy is not a cache, it is the answer to "against what".** A comparison that
    /// read the repository directly would describe a core nobody can point at afterwards -
    /// the repository moves on, and the report keeps claiming a result it can no longer
    /// reproduce. Copying first means the two halves of every comparison are both on disk,
    /// side by side, for as long as the project keeps them.
    ///
    /// **None of it is versioned**, and nothing has to be done for that: the `.gitignore`
    /// the config editor writes is an allow-list of three files, so everything here is
    /// ignored the day it appears. That was the point of an allow-list.
    ///
    /// Pure, like <see cref="ConfigPaths"/>: it builds strings and creates nothing.
    /// </summary>
    public static class RepoPaths
    {
        /// <summary>
        /// The core as it was copied out of the repository.
        ///
        /// **Named `CoreFolder` rather than `Core`**, which is what it holds: inside
        /// <c>namespace Core.Repo</c> a member called `Core` shadows the global namespace,
        /// so the next line that wrote <c>Core.Config.Something</c> in this file would fail
        /// to compile with a message about a missing type. Same trap as `AddIn.Core`.
        /// </summary>
        public const string CoreFolder = "core";

        /// <summary>
        /// What the TIA project holds, written by the walk rather than by the repository -
        /// the other half of every comparison.
        /// </summary>
        public const string ProjectFile = "project.json";

        /// <summary>Where a download lands before anything is imported into the project.</summary>
        public const string Tmp = "tmp";

        /// <summary>
        /// The workspace folder of one TIA project, or null when the project directory is
        /// unknown - which is the case for a window started by hand.
        /// </summary>
        public static string For(string projectDirectory) =>
            ConfigPaths.FolderFor(projectDirectory, ConfigPaths.Repo);

        /// <summary>Where the copied core goes, or null when the project directory is unknown.</summary>
        public static string CoreFor(string projectDirectory) => Under(projectDirectory, CoreFolder);

        /// <summary>Where the project map goes, or null when the project directory is unknown.</summary>
        public static string ProjectFor(string projectDirectory) => Under(projectDirectory, ProjectFile);

        /// <summary>Where a download works, or null when the project directory is unknown.</summary>
        public static string TmpFor(string projectDirectory) => Under(projectDirectory, Tmp);

        private static string Under(string projectDirectory, string name)
        {
            string repo = For(projectDirectory);

            return repo == null ? null : Path.Combine(repo, name);
        }
    }
}
