using System;
using System.IO;
using System.Text;

using Core.Config;
using Core.Config.Validation;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Satellite.ConfigEditor.Document
{
    /// <summary>
    /// One config.json, open for editing.
    ///
    /// **The JSON tree is the document, not a model bound to a form.** Editing the tree in
    /// place is what lets a key the model has never heard of survive a save - and this file
    /// is meant to be hand-edited, so somebody's extra key is not a bug to clean up.
    ///
    /// What is preserved: every key, and the order they appear in. What is **not**: the
    /// original whitespace. The file in .example writes <c>"version" : "v2.0"</c> with a
    /// space before the colon, and after a save it reads <c>"version": "v2.0"</c>. Keeping
    /// that would take editing the text by character offsets, which buys tidiness at the
    /// cost of every edit being able to corrupt the file.
    /// </summary>
    public sealed class ConfigDocument
    {
        private readonly JObject _root;
        private string _saved;

        private ConfigDocument(JObject root, string path)
        {
            _root = root;
            Path = path;
            _saved = Serialise();
        }

        /// <summary>Where it lives, or will live. Null only if nothing was chosen.</summary>
        public string Path { get; }

        /// <summary>True when there is something to save.</summary>
        public bool IsDirty => !string.Equals(_saved, Serialise(), StringComparison.Ordinal);

        /// <summary>Opens an existing file.</summary>
        public static ConfigDocument Load(string path, out string error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    error = "There is no file at " + (path ?? "(no path)") + ".";
                    return null;
                }

                string text = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(text))
                {
                    error = "The file is empty.";
                    return null;
                }

                return new ConfigDocument(JObject.Parse(text), path);
            }
            catch (JsonReaderException exception)
            {
                // Worth its own message: this one names a line and a column, which is
                // exactly what somebody who broke it by hand needs.
                error = "That is not valid JSON. " + exception.Message;
                return null;
            }
            catch (Exception exception)
            {
                error = "The file could not be read: " + exception.Message;
                return null;
            }
        }

        /// <summary>
        /// A brand new configuration, from the template. Nothing is written until the
        /// operator saves - so backing out of a mistake costs nothing.
        /// </summary>
        public static ConfigDocument FromTemplate(string path, out string note)
        {
            JObject template = ConfigTemplate.Load(out note);
            return template == null ? null : new ConfigDocument(template, path);
        }

        /// <summary>
        /// A value by document path: <c>metadata.coreSource</c>. Null when any step of the
        /// path is missing, which the caller reads as "not set".
        /// </summary>
        public string Get(string path)
        {
            JToken token = _root.SelectToken(path);
            return token == null || token.Type == JTokenType.Null ? null : token.Value<string>();
        }

        /// <summary>
        /// Sets a value by document path, creating the objects on the way if they are not
        /// there. An empty value **removes** the key rather than writing "": a key present
        /// with nothing in it says no more than an absent one, and the validator treats
        /// them the same, so leaving both possible would only produce files that differ
        /// without meaning anything different.
        /// </summary>
        public void Set(string path, string value)
        {
            string[] steps = path.Split('.');
            JObject parent = _root;

            for (int i = 0; i < steps.Length - 1; i++)
            {
                JObject next = parent[steps[i]] as JObject;

                if (next == null)
                {
                    next = new JObject();
                    parent[steps[i]] = next;
                }

                parent = next;
            }

            string leaf = steps[steps.Length - 1];

            if (string.IsNullOrEmpty(value)) parent.Remove(leaf);
            else parent[leaf] = value;
        }

        /// <summary>True when the section exists at all.</summary>
        public bool Has(string path) => _root.SelectToken(path) != null;

        /// <summary>
        /// A list inside the document, created on the way if asked for.
        ///
        /// The tree is handed out rather than copied into a model and back: that is what
        /// keeps a save from disturbing anything the editor did not touch, and it is why
        /// this returns the live <see cref="JArray"/> instead of a snapshot.
        /// </summary>
        public JArray ArrayAt(string path, bool create = false)
        {
            JToken token = _root.SelectToken(path);
            if (token is JArray existing) return existing;

            if (!create) return null;

            JArray created = new JArray();
            Put(path, created);
            return created;
        }

        public JObject ObjectAt(string path, bool create = false)
        {
            JToken token = _root.SelectToken(path);
            if (token is JObject existing) return existing;

            if (!create) return null;

            JObject created = new JObject();
            Put(path, created);
            return created;
        }

        private void Put(string path, JToken value)
        {
            string[] steps = path.Split('.');
            JObject parent = _root;

            for (int i = 0; i < steps.Length - 1; i++)
            {
                JObject next = parent[steps[i]] as JObject;

                if (next == null)
                {
                    next = new JObject();
                    parent[steps[i]] = next;
                }

                parent = next;
            }

            parent[steps[steps.Length - 1]] = value;
        }

        /// <summary>
        /// The structural problems: required fields, closed sets, internal references.
        /// **This is what blocks a save** - a broken file is broken on every machine.
        /// </summary>
        public ValidationResult Validate()
        {
            Config config = AsConfig(out string error);

            if (config == null)
            {
                return new ValidationResult(new[]
                {
                    new ValidationIssue("(document)", error ?? "This is not a configuration file.")
                });
            }

            return ConfigValidator.Validate(config);
        }

        /// <summary>
        /// The problems that depend on this machine: a path that exists, a ${VAR} that
        /// resolves. **Reported but never blocking** - a configuration being prepared here
        /// for another station is not wrong because a drive is not mapped on this one.
        /// </summary>
        public ValidationResult ValidateEnvironment()
        {
            Config config = AsConfig(out _);
            return config == null ? ValidationResult.Valid : EnvironmentValidator.Validate(config);
        }

        /// <returns>Null on success, or a sentence naming what went wrong.</returns>
        public string Save() => Save(out string _);

        /// <param name="note">
        /// Something that is worth saying but did not stop the save — today, only the JSON
        /// Schema failing to be written beside the file. It is deliberately not folded into
        /// the return value: the configuration did save, and a caller that treats "could not
        /// write the schema" as a failed save would be wrong about the one thing that
        /// matters.
        /// </param>
        /// <returns>Null on success, or a sentence naming what went wrong.</returns>
        public string Save(out string note)
        {
            note = null;

            if (string.IsNullOrWhiteSpace(Path)) return "There is nowhere to save this yet.";

            try
            {
                // Before serialising, so the $schema it adds is part of what gets written
                // and part of what _saved compares against - otherwise the document would
                // read as dirty the instant it was saved.
                note = ConfigSchema.Ensure(_root, Path);

                string text = Serialise();

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));

                // No BOM. Core's loader skips one when reading - it has to, because editors
                // add them - but writing one would make this file need that workaround.
                File.WriteAllText(Path, text, new UTF8Encoding(false));

                _saved = text;
                return null;
            }
            catch (Exception exception)
            {
                return "It could not be saved: " + exception.Message;
            }
        }

        /// <summary>
        /// The tree as Core sees it. Round-tripping through the text is deliberate: it is
        /// the same path the Add-In takes when it loads the file, so the editor cannot
        /// validate something the Add-In would read differently.
        /// </summary>
        private Config AsConfig(out string error)
        {
            error = null;

            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(Serialise());

                using (MemoryStream stream = new MemoryStream(bytes, false))
                {
                    ConfigLoadResult result = ConfigLoader.Load(stream, Path);

                    if (result.Succeeded) return result.Config;

                    error = result.Error;
                    return null;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }
        }

        private string Serialise() => _root.ToString(Formatting.Indented);
    }
}
