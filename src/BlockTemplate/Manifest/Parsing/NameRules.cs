using System;
using System.Linq;
using System.Text.RegularExpressions;

using BlockTemplate.Manifest.Json;

using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Parsing
{
    /// <summary>
    /// The two kinds of name a manifest gives, each with the rule of where it ends up: a name the
    /// template writes bare, and a name that is part of a file name.
    /// </summary>
    internal static class NameRules
    {
        /// <summary>A name the template writes bare - <c>{{ sensors }}</c> - so it has to be a word.</summary>
        private static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// A required name the template writes bare: a variable's, or a TIA version's value's. A
        /// word, not one the template itself uses, and not one of Scriban's own.
        /// </summary>
        public static string Word(JsonProblems problems, JObject entry, string key, string at)
        {
            string name = Required(problems, entry, key, at);
            if (name == null) return null;

            if (!Identifier.IsMatch(name))
            {
                problems.Add(entry[key], at + "." + key + " '" + name + "' is not a name a template can write: " +
                                         "a letter or _ first, then letters, digits and _.");
                return null;
            }

            if (TemplateNames.Reserved.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add(entry[key], at + "." + key + " '" + name + "' is taken by the template itself.");
                return null;
            }

            if (TemplateNames.ScribanWords.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(entry[key], at + "." + key + " '" + name + "' is a word Scriban keeps for itself, so a template " +
                                         "could not use it as a name.");
                return null;
            }

            return name;
        }

        /// <summary>
        /// A required name that is the middle of a file name - a generated type's id: anything a
        /// Windows file name takes, but a dot, which would make the name read two ways.
        ///
        /// **Not held to the rules of a word**, and the first version was: a variable is written bare
        /// in the template, a type's id is a file name's part, and <c>motor-settings</c> is a
        /// perfectly good one. Codex's second review of stage 1.1 found it.
        /// </summary>
        public static string FilePart(JsonProblems problems, JObject entry, string key, string at)
        {
            string name = Required(problems, entry, key, at);
            if (name == null) return null;

            if (name.Trim().Length == 0 || name != name.Trim() || name.IndexOf('.') >= 0 ||
                name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                problems.Add(entry[key], at + "." + key + " '" + name + "' cannot be part of a file name: no dot, " +
                                         "no space at either end, and nothing Windows refuses in one.");
                return null;
            }

            return name;
        }

        private static string Required(JsonProblems problems, JObject entry, string key, string at)
        {
            JToken token = entry[key];

            if (ManifestJson.Absent(token))
            {
                problems.Add(entry, at + " has no " + key + ".");
                return null;
            }

            if (token.Type != JTokenType.String)
            {
                problems.Add(token, at + "." + key + " must be a name in quotes.");
                return null;
            }

            return (string)token;
        }
    }
}
