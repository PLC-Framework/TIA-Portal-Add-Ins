using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Newtonsoft.Json.Linq;

namespace Satellite.DataBlockSnapshot.Handoff
{
    /// <summary>
    /// Reads the payload the Add-In hands over.
    ///
    /// The transport is deliberately behind this one method. The chosen route is standard
    /// input - Siemens' Process wrapper exposes RedirectStandardInput, so nothing is
    /// written to disk, nothing needs cleaning up and no stale handoff survives a crash.
    /// Whether that redirection survives TIA's permission sandbox has not been verified
    /// on the VM yet, so the fallback stays wired: a path passed as an argument. When the
    /// answer comes in, one of the two branches goes away and nothing else changes.
    /// </summary>
    public static class HandoffReader
    {
        public static SnapshotRequest Read(string[] arguments)
        {
            string json = FromArguments(arguments) ?? FromStandardInput();

            if (string.IsNullOrWhiteSpace(json)) return SnapshotRequest.Empty;

            try
            {
                return Parse(json);
            }
            catch (Exception)
            {
                // A malformed handoff must not stop the window from opening: everything
                // in it can be typed by hand, so an empty form beats a dead process.
                return SnapshotRequest.Empty;
            }
        }

        private static string FromArguments(string[] arguments)
        {
            if (arguments == null) return null;

            foreach (string argument in arguments)
            {
                if (string.IsNullOrWhiteSpace(argument)) continue;

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
            // Without this check a satellite started from Explorer would block forever
            // waiting on a console that is never going to send anything.
            if (!Console.IsInputRedirected) return null;

            try
            {
                Stream input = Console.OpenStandardInput();
                if (input == Stream.Null) return null;

                // Deliberately NOT Console.In. That property builds its reader from
                // Console.InputEncoding, which calls GetConsoleCP() - and this is a GUI
                // subsystem process with no console attached, so it fails. The handle
                // itself is perfectly good: measured in a WinExe with a redirected pipe,
                // IsInputRedirected is true and the payload arrives whole through here.
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

        private static SnapshotRequest Parse(string json)
        {
            JObject document = JObject.Parse(json);

            return new SnapshotRequest(
                document["projectDirectory"]?.Value<string>(),
                document["plc"]?["name"]?.Value<string>(),
                Strings(document["plc"]?["addresses"]),
                Strings(document["dataBlocks"]));
        }

        private static IReadOnlyList<string> Strings(JToken token)
        {
            List<string> values = new List<string>();
            if (!(token is JArray array)) return values;

            foreach (JToken entry in array)
            {
                string value = entry?.Value<string>();
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value.Trim());
            }

            return values;
        }
    }
}
