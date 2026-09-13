using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Satellite.CodingStyleReport.Export
{
    /// <summary>Where an exported report is written, and under what name.</summary>
    public static class ReportFile
    {
        public const string Extension = ".xlsx";

        /// <summary>
        /// &lt;project&gt;-coding-style-&lt;timestamp&gt;.xlsx, the timestamp being when the check
        /// ran - the report's own moment - rather than when it was exported. The same shape
        /// as the snapshot's &lt;ip&gt;-&lt;DB&gt;-snapshot-&lt;timestamp&gt;.xlsx, so the two sort and
        /// read alike in a project's folders.
        /// </summary>
        public static string NameFor(string project, DateTime moment) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}-coding-style-{1:yyyyMMdd-HHmmss}{2}",
                Sanitise(project), moment, Extension);

        /// <summary>
        /// The path to write, with " (n)" appended when the name is taken. Never overwrites: two
        /// exports of one check are harmless side by side, and silently replacing a file
        /// somebody may already have annotated is not.
        /// </summary>
        public static string Unique(string folder, string fileName)
        {
            string candidate = Path.Combine(folder, fileName);
            if (!File.Exists(candidate)) return candidate;

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);

            for (int n = 2; n < 1000; n++)
            {
                candidate = Path.Combine(folder,
                    string.Format(CultureInfo.InvariantCulture, "{0} ({1}){2}", stem, n, extension));

                if (!File.Exists(candidate)) return candidate;
            }

            throw new IOException("Too many files already exist with that name in " + folder + ".");
        }

        /// <summary>
        /// Why the folder cannot take the file, or null. Creates it when missing: nothing makes
        /// .plc-framework\reports ahead of time, so the first export is what brings it into being.
        /// </summary>
        public static string ProblemWith(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return "Choose a destination folder.";

            try
            {
                DirectoryInfo directory = new DirectoryInfo(folder);
                if (!directory.Exists) directory.Create();

                string probe = Path.Combine(directory.FullName, ".plc-framework-write-test");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);

                return null;
            }
            catch (Exception exception)
            {
                return "The destination folder cannot be written to: " + exception.Message;
            }
        }

        private static string Sanitise(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "report";

            StringBuilder clean = new StringBuilder(text.Length);
            char[] invalid = Path.GetInvalidFileNameChars();

            foreach (char character in text.Trim())
                clean.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);

            return clean.ToString();
        }
    }
}
