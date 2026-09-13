using System;
using System.IO;
using System.Text;

using Core.Checks;

namespace Satellite.CodingStyleReport.Handoff
{
    /// <summary>
    /// Reads the report the Add-In hands over.
    ///
    /// Standard input, as for the other satellites - verified on the VM to survive TIA's
    /// sandbox - with a path passed as an argument kept as the fallback. The document itself is
    /// read by Core, which also wrote it: one contract, no second parser.
    /// </summary>
    public static class HandoffReader
    {
        /// <param name="handedOver">
        /// Whether anything arrived at all. A window started by hand gets nothing and says
        /// how to get a report; one that received something it could not read says that
        /// instead - two different sentences for two different situations.
        /// </param>
        /// <param name="problem">Why what arrived could not be used, or null.</param>
        /// <returns>The report, or null.</returns>
        public static StyleReport Read(string[] arguments, out bool handedOver, out string problem)
        {
            problem = null;

            string json = FromArguments(arguments) ?? FromStandardInput();
            handedOver = !string.IsNullOrWhiteSpace(json);

            if (!handedOver) return null;

            // Never throws: a window that opens and says what went wrong beats a process
            // that dies before anything is on screen.
            return StyleReport.FromJson(json, out problem);
        }

        private static string FromArguments(string[] arguments)
        {
            if (arguments == null) return null;

            foreach (string argument in arguments)
            {
                // A workbook is never a handoff; App imports it instead. Read as text here, it
                // would arrive as a report that "could not be read".
                if (string.IsNullOrWhiteSpace(argument) ||
                    argument.EndsWith(Export.ReportFile.Extension, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    if (File.Exists(argument)) return File.ReadAllText(argument);
                }
                catch (Exception)
                {
                    // Unreadable path: fall through to standard input.
                }
            }

            return null;
        }

        private static string FromStandardInput()
        {
            // Without this check a copy started from Explorer would block forever waiting on
            // a console that is never going to send anything.
            if (!Console.IsInputRedirected) return null;

            try
            {
                Stream input = Console.OpenStandardInput();
                if (input == Stream.Null) return null;

                // Deliberately NOT Console.In. That property builds its reader from
                // Console.InputEncoding, which calls GetConsoleCP() - and this is a GUI
                // subsystem process with no console attached, so it fails.
                using (StreamReader reader = new StreamReader(input, new UTF8Encoding(false)))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
