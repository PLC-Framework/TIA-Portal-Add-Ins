using System.IO;

namespace Core.Config
{
    /// <summary>
    /// The framework's folder inside a TIA project, and what lives in it.
    ///
    /// The literals sit here because the Add-In and the satellites both have to agree on
    /// them. If each wrote its own string, the day the convention changes one of the two
    /// keeps the old one and nothing fails at compile time.
    ///
    ///     &lt;TIA project&gt;\.plc-framework\
    ///     +-- config.json          the configuration, hand-edited and edited by the editor
    ///     +-- config.schema.json   written by the editor, so the hand edits are checked
    ///     +-- .gitignore           written by the editor: everything here but those three
    ///     +-- exports\             captures, for a person to open
    ///     +-- logs\                what a run recorded about itself
    ///     +-- repo\                the core as this project last saw it, and its workspace
    ///     +-- tmp\                 scratch space for one action
    ///     +-- reports\             generated reports
    ///
    /// **Only the first three are versioned**, and the editor's .gitignore is what keeps it
    /// that way: a workbook under exports\ holds values read out of a live CPU, and this
    /// folder sits beside .version-control\. The four generated folders are named here rather
    /// than spelled out at each call site for the reason above — three of them have no writer
    /// yet, and that is precisely when two components drift apart on a string.
    /// </summary>
    public static class ConfigPaths
    {
        public const string Folder = ".plc-framework";
        public const string File = "config.json";

        /// <summary>Captures and anything else meant for a person to open afterwards.</summary>
        public const string Exports = "exports";

        /// <summary>What a run recorded about itself, for reading when something went wrong.</summary>
        public const string Logs = "logs";

        /// <summary>
        /// Everything to do with the core this project is built on: the copy it was last
        /// compared against, the map of the project itself, and the sources a later update
        /// downloads. See <see cref="Core.Repo.RepoPaths"/> for what goes where inside it.
        /// </summary>
        public const string Repo = "repo";

        /// <summary>Scratch space for a single action. Nothing here survives being deleted.</summary>
        public const string Tmp = "tmp";

        /// <summary>Generated reports - the coding-style check and whatever follows it.</summary>
        public const string Reports = "reports";

        /// <summary>
        /// One of the folders above inside a TIA project, or null when the project directory
        /// is unknown - which is the case when a satellite is started by hand rather than
        /// from the Add-In.
        ///
        /// It does not create anything. Whoever writes the first file creates it then, which
        /// keeps a project that never ran an action free of four empty folders - and git
        /// would not record them anyway.
        /// </summary>
        public static string FolderFor(string projectDirectory, string name) =>
            string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(name)
                ? null
                : Path.Combine(projectDirectory, Folder, name);

        /// <summary>
        /// The exports folder of one TIA project, or null when the project directory is
        /// unknown.
        /// </summary>
        public static string ExportsFor(string projectDirectory) =>
            FolderFor(projectDirectory, Exports);
    }
}
