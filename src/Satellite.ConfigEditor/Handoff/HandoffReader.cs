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
    /// empty window instead of killing the process - **saying so**, through
    /// <see cref="EditorRequest.Problem"/>, rather than looking like a window started by hand.
    /// </summary>
    public static class HandoffReader
    {
        public static EditorRequest Read(string[] arguments)
        {
            string problem = null;
            string json = FromArguments(arguments) ?? FromStandardInput(out problem);

            if (problem != null) return EditorRequest.Unreadable(problem);
            if (string.IsNullOrWhiteSpace(json)) return EditorRequest.Empty;

            try
            {
                JObject document = JObject.Parse(json);

                return new EditorRequest(
                    document["projectDirectory"]?.Value<string>(),
                    document["projectName"]?.Value<string>()
                        ?? document["project"]?["name"]?.Value<string>());
            }
            catch (Exception exception)
            {
                // Everything here can be typed by hand, so an empty form beats a dead
                // process - but it is not a window nobody sent anything to.
                return EditorRequest.Unreadable("What TIA Portal sent could not be read: " + exception.Message);
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

        /// <param name="problem">
        /// Why input that was there could not be read; null when there simply was none, which is
        /// a window started by hand rather than a failure.
        /// </param>
        private static string FromStandardInput(out string problem)
        {
            problem = null;

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
            catch (Exception exception)
            {
                problem = "What TIA Portal sent could not be read: " + exception.Message;
                return null;
            }
        }
    }
}
