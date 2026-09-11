using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Newtonsoft.Json.Linq;

namespace Satellite.ConfigEditor.Document
{
    /// <summary>
    /// The JSON Schema that makes a text editor validate config.json while somebody types
    /// in it — the one place none of the other two validators reach. Core's run before a
    /// save and after a load; this one runs on every keystroke, in whatever editor opened
    /// the file, and it is the only safety net the hand-edit path has ever had.
    ///
    /// **It is written into the TIA project, beside config.json, and referenced relatively.**
    /// The alternatives were each worse in a way that only shows later: a URL needs the
    /// network and answers 404 on a private repository, which stops validation with no
    /// message at all; an absolute path to the install folder gets committed and is wrong on
    /// the next station; and an editor setting is per machine, so whoever clones the project
    /// inherits nothing. A copy per project is the cost of it working for a stranger who
    /// opens the file. Nothing secret is in it, so <c>.plc-framework\</c> being under version
    /// control is fine here — the rule it must not break is about credentials.
    ///
    /// **It is rewritten on every save**, because it is generated rather than authored:
    /// a project that stays open across a framework upgrade would otherwise keep validating
    /// against last month's contract. That does mean hand edits to it are lost, which is the
    /// right trade for a derived file and the opposite of the rule for config.template.json,
    /// where customising is the whole point.
    ///
    /// **It never fails a save.** An unwritable schema costs autocomplete; it does not cost
    /// the configuration, so it is reported and stepped over.
    /// </summary>
    public static class ConfigSchema
    {
        public const string FileName = "config.schema.json";

        /// <summary>
        /// What <c>$schema</c> is set to. Relative, so it resolves wherever the project is
        /// copied, and on whichever machine.
        /// </summary>
        public const string Reference = "./" + FileName;

        private const string SchemaProperty = "$schema";

        /// <summary>
        /// Points the document at the schema and writes the schema beside it.
        ///
        /// A <c>$schema</c> the operator set to something else is left exactly as it is, and
        /// no file is written: pointing at a shared copy on a network drive is a deliberate
        /// thing to do, and overwriting that decision on every save would be the editor
        /// arguing with its user.
        /// </summary>
        /// <returns>A sentence worth showing, or null when everything was ordinary.</returns>
        public static string Ensure(JObject root, string configPath)
        {
            if (root == null || string.IsNullOrWhiteSpace(configPath)) return null;

            JToken existing = root[SchemaProperty];

            if (existing == null)
            {
                // First, not appended: every editor and every reader expects $schema at the
                // top, and the surgical writes this editor makes preserve key order.
                root.AddFirst(new JProperty(SchemaProperty, Reference));
            }
            else if (!string.Equals(existing.Type == JTokenType.String ? (string)existing : null,
                                    Reference, StringComparison.Ordinal))
            {
                return null;
            }

            return Write(configPath);
        }

        private static string Write(string configPath)
        {
            string content = ReadEmbedded();

            if (content == null)
            {
                // Compiled in, so this should be impossible. Say it plainly rather than
                // leaving a $schema pointing at a file that was never created.
                return "The built-in " + FileName + " is missing from this build, so " +
                       "config.json points at a schema that is not there.";
            }

            try
            {
                string folder = Path.GetDirectoryName(configPath);
                string target = Path.Combine(folder ?? string.Empty, FileName);

                Directory.CreateDirectory(folder);

                // No BOM, same as config.json: this file is read by editors that mostly
                // cope with one and by tools that mostly do not.
                File.WriteAllText(target, content, new UTF8Encoding(false));

                return null;
            }
            catch (Exception exception)
            {
                return "Saved, but " + FileName + " could not be written beside it: " +
                       exception.Message + " Editing config.json by hand will not be checked.";
            }
        }

        private static string ReadEmbedded()
        {
            try
            {
                Assembly assembly = typeof(ConfigSchema).Assembly;

                // Matched on the tail, never on a full literal. MSBuild derives the resource
                // name from the root namespace and the folder, and a hardcoded prefix does
                // not fail at compile time when it stops matching - it returns null at
                // runtime, which is the worst place to find out.
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
    }
}
