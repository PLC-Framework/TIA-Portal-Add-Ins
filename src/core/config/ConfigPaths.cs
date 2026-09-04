using System.IO;

namespace Core.Config
{
    /// <summary>
    /// The framework's folder inside a TIA project, and what lives in it.
    ///
    /// The literals sit here because the Add-In and the satellites both have to agree on
    /// them. If each wrote its own string, the day the convention changes one of the two
    /// keeps the old one and nothing fails at compile time.
    /// </summary>
    public static class ConfigPaths
    {
        public const string Folder = ".plc-framework";
        public const string File = "config.json";

        /// <summary>Where captures and other generated files are written.</summary>
        public const string Exports = "exports";

        /// <summary>
        /// The exports folder of one TIA project, or null when the project directory is
        /// unknown - which is the case when a satellite is started by hand rather than
        /// from the Add-In.
        /// </summary>
        public static string ExportsFor(string projectDirectory) =>
            string.IsNullOrWhiteSpace(projectDirectory)
                ? null
                : Path.Combine(projectDirectory, Folder, Exports);
    }
}
