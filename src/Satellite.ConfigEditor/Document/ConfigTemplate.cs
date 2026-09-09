using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Core;

using Newtonsoft.Json.Linq;

namespace Satellite.ConfigEditor.Document
{
    /// <summary>
    /// The starting point for a new config.json.
    ///
    /// **Two copies, and the order between them is the whole design.** The one on disk at
    /// <c>%LOCALAPPDATA%\PLC-Framework\config.template.json</c> wins when it is there and
    /// parses, so an operator can shape the default their plant actually uses. The embedded
    /// one is the fallback, and it is **written out** when the file is missing or broken -
    /// so a station repairs itself and there is something to customise.
    ///
    /// Per user, not <c>tools\</c>. That was the first plan and it carries the same defect
    /// the old .env did: <c>tools\</c> lives under %ProgramData%, where ordinary users have
    /// read and execute but **not write**, so the self-repair would work on a developer's
    /// machine and fail on a real station.
    /// </summary>
    public static class ConfigTemplate
    {
        public const string FileName = "config.template.json";

        public static string Path => InstallPaths.UserFile(FileName);

        /// <summary>
        /// The template to start a new configuration from, always a fresh copy so the
        /// caller can edit it freely.
        /// </summary>
        /// <param name="note">
        /// What happened, when it is worth telling the operator - the file was repaired, or
        /// it could not be written. Null when everything was ordinary.
        /// </param>
        public static JObject Load(out string note)
        {
            note = null;

            JObject fromDisk = ReadFromDisk();
            if (fromDisk != null) return fromDisk;

            string embedded = ReadEmbedded();
            if (embedded == null)
            {
                // Should be impossible: it is compiled in. Worth saying plainly rather than
                // returning an empty document that looks like a template.
                note = "The built-in template is missing from this build.";
                return null;
            }

            note = WriteToDisk(embedded);

            try
            {
                return JObject.Parse(embedded);
            }
            catch (Exception exception)
            {
                note = "The built-in template could not be parsed: " + exception.Message;
                return null;
            }
        }

        private static JObject ReadFromDisk()
        {
            try
            {
                string path = Path;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception)
            {
                // Present but unreadable, or not JSON at all. Treated exactly like absent:
                // the embedded copy takes over and replaces it.
                return null;
            }
        }

        private static string ReadEmbedded()
        {
            try
            {
                Assembly assembly = typeof(ConfigTemplate).Assembly;

                // Matched on the tail, never on a full literal: MSBuild derives the name
                // from the root namespace and the folder, and a hardcoded prefix does not
                // fail at compile time when it stops matching - it returns null at runtime.
                string name = assembly.GetManifestResourceNames()
                    .FirstOrDefault(candidate =>
                        candidate.EndsWith(FileName, StringComparison.OrdinalIgnoreCase));

                if (name == null) return null;

                using (Stream stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null) return null;

                    using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false)))
                        return reader.ReadToEnd();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <returns>A note for the operator, or null when nothing needs saying.</returns>
        private static string WriteToDisk(string content)
        {
            string path = Path;
            if (string.IsNullOrEmpty(path)) return null;

            try
            {
                bool existed = File.Exists(path);

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, content, new UTF8Encoding(false));

                return existed
                    ? "The template at " + path + " could not be read, so it was replaced with the built-in one."
                    : null;
            }
            catch (Exception exception)
            {
                // Not fatal: the embedded copy still works. Only the customisable copy is
                // lost, so say so and carry on.
                return "The built-in template was used, but it could not be saved to " +
                       path + ": " + exception.Message;
            }
        }
    }
}
