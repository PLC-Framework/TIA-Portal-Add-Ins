using System;
using System.IO;
using System.Text;

using Newtonsoft.Json.Linq;

namespace Satellite.ConfigEditor.Handoff
{
    /// <summary>
    /// Reads the payload the Add-In hands over, on standard input.
    ///
    /// Same shape as the snapshot satellite's reader, and same two hard-won details: the
    /// stream is opened rather than taken from Console.In, and a malformed payload opens an
    /// empty window instead of killing the process.
    /// </summary>
    public static class HandoffReader
    {
        public static EditorRequest Read(string[] arguments)
        {
            string json = FromArguments(arguments) ?? FromStandardInput();

            if (string.IsNullOrWhiteSpace(json)) return EditorRequest.Empty;

            try
            {
                JObject document = JObject.Parse(json);

                return new EditorRequest(
                    document["projectDirectory"]?.Value<string>(),
                    document["projectName"]?.Value<string>()
                        ?? document["project"]?["name"]?.Value<string>());
            }
            catch (Exception)
            {
                // Everything here can be typed by hand, so an empty form beats a dead
                // process.
                return EditorRequest.Empty;
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
                }
            }

            return null;
        }

        private static string FromStandardInput()
        {
            // Without this check, a copy started from Explorer would block forever waiting
            // on a console that is never going to send anything.
            if (!Console.IsInputRedirected) return null;

            try
            {
                Stream input = Console.OpenStandardInput();
                if (input == Stream.Null) return null;

                // Deliberately NOT Console.In. That property builds its reader from
                // Console.InputEncoding, which calls GetConsoleCP() - and this is a GUI
                // subsystem process with no console attached, so it fails. The handle
                // itself is perfectly good.
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
