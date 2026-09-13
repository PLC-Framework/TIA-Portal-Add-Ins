using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Core;

using Newtonsoft.Json.Linq;

namespace Satellite.ConfigEditor.Document
{
    /// <summary>Where a new config.json starts from. The operator chooses; nothing is preferred silently.</summary>
    public enum TemplateSource
    {
        /// <summary>The template compiled into this executable: always the current contract.</summary>
        System,

        /// <summary>The copy at <c>%LOCALAPPDATA%\PLC-Framework\config.template.json</c>, shaped to a plant.</summary>
        User
    }

    /// <summary>
    /// The starting point for a new config.json, from either of two copies.
    ///
    /// **The operator picks the copy; the editor no longer does.** The per-user file used to
    /// win whenever it was there and parsed, with the embedded one as a fallback. But the
    /// editor writes that file itself, so a station that never touched it kept the template
    /// of whichever version first opened the editor - and every upgrade after that was
    /// silently ignored. Found on the VM (2026-09-13): a project created after the catalogue
    /// was split came up with the old catalogue, no interface rules and no interfaces. With
    /// both on offer, the system template is always the current contract and the user
    /// template is a deliberate choice.
    ///
    /// Per user, not the install folder. That was the first plan and it carries the same
    /// defect the old .env did: the install folder is Program Files, where ordinary users
    /// have read and execute but **not write**.
    /// </summary>
    public static class ConfigTemplate
    {
        public const string FileName = "config.template.json";

        public static string Path => InstallPaths.UserFile(FileName);

        /// <summary>Whether a user template is there to offer. Whether it parses is only found out by using it.</summary>
        public static bool UserTemplateExists
        {
            get
            {
                try
                {
                    string path = Path;
                    return !string.IsNullOrEmpty(path) && File.Exists(path);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// The template to start a new configuration from, always a fresh copy so the caller
        /// can edit it freely. Null when that copy could not be used, with the reason in
        /// <paramref name="note"/>; there is **no fallback** to the other one, because the
        /// operator asked for this one.
        /// </summary>
        /// <param name="note">What is worth telling the operator, or null when nothing is.</param>
        public static JObject Load(TemplateSource source, out string note)
        {
            return source == TemplateSource.User ? LoadUser(out note) : LoadSystem(out note);
        }

        private static JObject LoadSystem(out string note)
        {
            note = null;

            string embedded = ReadEmbedded();
            if (embedded == null)
            {
                // Should be impossible: it is compiled in. Worth saying plainly rather than
                // returning an empty document that looks like a template.
                note = "The system template is missing from this build.";
                return null;
            }

            JObject template;
            try
            {
                template = JObject.Parse(embedded);
            }
            catch (Exception exception)
            {
                note = "The system template could not be parsed: " + exception.Message;
                return null;
            }

            note = WriteUserCopyIfAbsent(embedded);
            return template;
        }

        private static JObject LoadUser(out string note)
        {
            note = null;
            string path = Path;

            if (!UserTemplateExists)
            {
                note = "There is no user template at " + path + ".";
                return null;
            }

            try
            {
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                // Reported, never replaced: a template somebody broke by hand is still one
                // somebody shaped, and the system template is a click away.
                note = "The user template at " + path + " could not be read: " + exception.Message;
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

        /// <summary>
        /// Leaves a copy of the system template where the user template lives, **only when
        /// there is none** - so there is something to customise, and never at the cost of one
        /// somebody already shaped, broken or not. Offering the two side by side is what makes
        /// that safe: the copy can age, but it is only ever used when chosen.
        /// </summary>
        /// <returns>A note for the operator, or null when nothing was written.</returns>
        private static string WriteUserCopyIfAbsent(string content)
        {
            string path = Path;
            if (string.IsNullOrEmpty(path) || UserTemplateExists) return null;

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, content, new UTF8Encoding(false));

                return "A copy was left at " + path + " to customise as your user template.";
            }
            catch (Exception exception)
            {
                // Not fatal: the system template is already in hand. Only the customisable
                // copy is lost, so say so and carry on.
                return "A copy could not be left at " + path + " to customise: " + exception.Message;
            }
        }
    }
}
