using System;
using System.Globalization;
using System.IO;
using System.Text;

using Core.Config;

namespace Core.Exports
{
    /// <summary>
    /// Where an exported object goes: under <c>.plc-framework\exports\</c>, in a tree shaped
    /// like the project's own.
    ///
    /// <code>
    /// exports\PLC_1\Program blocks\03-ALL\_oc_seq2_picking.xml
    /// exports\PLC_1\Software units\Filling\PLC data types\type_recipe.xml
    /// </code>
    ///
    /// **A mirror rather than a folder per run**, which is what decides what an export is
    /// good for. Exporting again writes over the same files, so `git diff` between two
    /// exports says what changed in the project - where a timestamped folder keeps history
    /// nobody can compare without work.
    ///
    /// **The folder names are TIA's own**, so the tree reads like the one in the project tree.
    /// The cost is that they follow the interface language: a project opened in Spanish
    /// exports into `Bloques de programa`, and changing language renames every folder. The
    /// level a software unit's objects sit under is the exception - it is ours, fixed, and
    /// named below.
    ///
    /// Pure: it builds strings and creates nothing. Whoever writes the first file makes the
    /// folders, as everywhere else in <see cref="ConfigPaths"/>.
    /// </summary>
    public static class ExportTree
    {
        /// <summary>What the families Openness exports one by one are written as.</summary>
        public const string SimaticMl = ".xml";

        /// <summary>
        /// What the alarm text lists are written as, because Openness offers nothing else for
        /// them: one workbook holding every list of a PLC. It is also the framework's own
        /// export format everywhere else, which is a coincidence worth having.
        /// </summary>
        public const string Workbook = ".xlsx";

        /// <summary>
        /// The level a software unit's objects sit under, mirroring the node TIA shows. **Ours
        /// and not translated**, unlike the folders below it: a unit's name is the engineer's,
        /// and this is the only level that has to be predictable to find one.
        /// </summary>
        public const string Units = "Software units";

        /// <summary>
        /// The longest path Windows creates through the ordinary API, which is what the
        /// Add-In has inside TIA Portal's process. A path over this is reported rather than
        /// attempted: the failure it produces otherwise names nothing an operator can act on.
        /// </summary>
        public const int LongestPath = 259;

        /// <summary>What a segment becomes when there is nothing left of it.</summary>
        private const string Fallback = "_";

        /// <summary>Separates a sanitised segment from the mark that says it was changed.</summary>
        private const char Marked = '~';

        private static readonly char[] Separators = { '/', '\\' };

        // Windows drops these from the end of a name, so "block." and "block" would be one
        // file - and the first cannot be created at all.
        private static readonly char[] Dropped = { ' ', '.' };

        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// The folder one object's file belongs in, or null when the project directory is
        /// unknown.
        /// </summary>
        /// <param name="unit">The software unit, or empty for the general program.</param>
        /// <param name="folders">
        /// Where it sits inside the PLC or the unit, as the walk found it:
        /// <c>Program blocks/03-ALL</c>.
        /// </param>
        public static string FolderFor(string projectDirectory, string plc, string unit, string folders)
        {
            string root = ConfigPaths.ExportsFor(projectDirectory);
            if (root == null) return null;

            string path = Path.Combine(root, Segment(plc));

            if (!string.IsNullOrWhiteSpace(unit))
                path = Path.Combine(path, Units, Segment(unit));

            foreach (string folder in Split(folders))
                path = Path.Combine(path, Segment(folder));

            return path;
        }

        /// <summary>
        /// The file one object is exported to, or null when the project directory is unknown.
        /// </summary>
        public static string FileFor(
            string projectDirectory, string plc, string unit, string folders, string name, string extension = SimaticMl)
        {
            string folder = FolderFor(projectDirectory, plc, unit, folders);

            return folder == null ? null : Path.Combine(folder, Segment(name) + (extension ?? SimaticMl));
        }

        /// <summary>
        /// Why this path cannot be written here, or null when it can.
        ///
        /// Long paths are the one limit an engineer meets by accident: a project three folders
        /// deep inside a user profile, a TIA tree four folders deep and a block name of forty
        /// characters together pass 259 without anything looking unusual.
        /// </summary>
        public static string TooLong(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= LongestPath) return null;

            return string.Format(
                CultureInfo.CurrentCulture,
                "The path would be {0} characters and Windows allows {1} here, so it was not written: {2}",
                path.Length, LongestPath, path);
        }

        /// <summary>
        /// One name TIA allows, as a name Windows allows.
        ///
        /// **A changed name is marked, and the mark comes from the original.** Two names TIA
        /// keeps apart can become one file - <c>DB:1</c> and <c>DB_1</c>, <c>block.</c> and
        /// <c>block</c> - and the second would then quietly overwrite the first. The mark is
        /// computed from the name itself rather than from the order objects were reached, so
        /// the same project exports to the same file names every time and a diff between two
        /// exports shows what changed in the project rather than what changed in the run.
        ///
        /// A name Windows already accepts is left exactly as it is, which is almost all of
        /// them.
        /// </summary>
        public static string Segment(string name)
        {
            if (string.IsNullOrEmpty(name)) return Fallback;

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder clean = new StringBuilder(name.Length);
            bool changed = false;

            foreach (char character in name)
            {
                if (Array.IndexOf(invalid, character) >= 0)
                {
                    clean.Append('_');
                    changed = true;
                    continue;
                }

                clean.Append(character);
            }

            string kept = clean.ToString();
            string cleaned = kept.TrimEnd(Dropped);

            if (cleaned.Length != kept.Length) changed = true;

            if (cleaned.Length == 0)
            {
                cleaned = Fallback;
                changed = true;
            }

            // CON, NUL, COM1: names the file system still reserves for devices, extension or
            // no extension. A file cannot be created with one.
            if (IsReserved(cleaned))
            {
                cleaned = Fallback + cleaned;
                changed = true;
            }

            return changed ? cleaned + Marked + Mark(name) : cleaned;
        }

        private static bool IsReserved(string name)
        {
            foreach (string reserved in Reserved)
            {
                if (string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// Eight hex digits of the original name, so two names that clean to the same thing do
        /// not clean to the same file.
        ///
        /// FNV-1a, written out here rather than taken from the framework: `GetHashCode` is not
        /// promised to be the same from one run to the next, and a file name that moves
        /// between exports would turn every diff into noise. Nothing here is a secret, so a
        /// cryptographic hash would only be a dependency to explain.
        /// </summary>
        private static string Mark(string name)
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            uint hash = offset;

            foreach (char character in name)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        private static string[] Split(string folders) =>
            string.IsNullOrWhiteSpace(folders)
                ? new string[0]
                : folders.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    }
}
