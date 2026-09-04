using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Satellite.DataBlockSnapshot.Export
{
    /// <summary>Where one capture is written, and under what name.</summary>
    public static class ExportFile
    {
        /// <summary>
        /// &lt;ip&gt;-&lt;DB&gt;-snapshot-&lt;timestamp&gt;.xlsx, and " (n)" appended if that
        /// name is taken.
        ///
        /// Overwriting silently is the one thing not to do: two captures a second apart
        /// are two different readings of the machine, and losing one of them without
        /// saying so defeats the point of taking them.
        /// </summary>
        public static string NameFor(string plcAddress, string dataBlock, DateTime moment) =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}-snapshot-{2:yyyyMMdd-HHmmss}.xlsx",
                Sanitise(plcAddress), Sanitise(dataBlock), moment);

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
        /// Checked before the read starts, never after.
        ///
        /// Finding out that the folder cannot be written to at the end of a thirty second
        /// capture is the worst possible moment: the values are gone and the operator has
        /// to start again.
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
            if (string.IsNullOrEmpty(text)) return "unknown";

            StringBuilder clean = new StringBuilder(text.Length);
            char[] invalid = Path.GetInvalidFileNameChars();

            foreach (char character in text)
                clean.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);

            return clean.ToString();
        }
    }
}
