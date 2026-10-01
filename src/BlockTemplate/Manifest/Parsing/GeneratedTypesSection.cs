using System;
using System.Collections.Generic;
using System.Linq;

using BlockTemplate.Manifest.Json;

using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Parsing
{
    /// <summary>
    /// The manifest's <c>generatedTypes</c>: the PLC data types made with the object, each from a
    /// sub-template of its own and named <c>prefix + header.name + suffix</c>.
    /// </summary>
    internal static class GeneratedTypesSection
    {
        public const string Key = "generatedTypes";

        private static readonly string[] Keys = { "id", "prefix", "suffix" };

        /// <param name="whole">
        /// Every entry gave an id. Only then can a sub-template no entry describes be called a stray:
        /// otherwise it may be the one a broken entry meant.
        /// </param>
        public static List<GeneratedType> Read(JsonProblems problems, JToken token, IReadOnlyList<string> header, out bool whole)
        {
            List<GeneratedType> types = new List<GeneratedType>();
            JArray array = problems.List(token, Key, "a list of { \"id\": ..., \"prefix\": ..., \"suffix\": ... }");
            whole = array != null || ManifestJson.Absent(token);
            if (array == null) return types;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> naming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < array.Count; i++)
            {
                string at = Key + "[" + i + "]";
                JObject entry = problems.Entry(array[i], at);
                if (entry == null)
                {
                    whole = false;
                    continue;
                }

                problems.Keys(entry, Keys, at);

                string id = NameRules.FilePart(problems, entry, "id", at);

                // An id given twice is still an id that read: both entries name the same file, so no
                // other file can be the one either meant, and a stray beside them is still said.
                // Codex's thirteenth review of stage 1.1 found it held back.
                if (id == null) whole = false;
                else if (!seen.Add(id))
                {
                    problems.Add(entry["id"], at + ".id '" + id + "' names another type already.");
                    id = null;
                }

                string prefix = Text(problems, entry, "prefix", at);
                string suffix = Text(problems, entry, "suffix", at);

                // Two types named alike for every header.name could never both be generated: the
                // second would meet the first in the project. Whatever the case, the conservative
                // reading of a name. Codex's ninth review of stage 1.1 found two passing.
                if (prefix != null && suffix != null)
                {
                    string named = prefix + "\0" + suffix;
                    if (naming.TryGetValue(named, out int first))
                        problems.Add(entry, at + " names its type exactly as " + Key + "[" + first + "] does: both would be '" +
                                            prefix + "<name>" + suffix + "'.");
                    else
                        naming.Add(named, i);
                }

                // Kept with its id whatever became of the rest, so its sub-template is still looked
                // for: the id is all the matching reads, and a type with a bad suffix is never handed
                // out - the problem beside it sees to that.
                if (id != null) types.Add(new GeneratedType(id, prefix ?? string.Empty, suffix ?? string.Empty));
            }

            // A type's name is prefix + header.name + suffix, so with no name in the header there is
            // nothing to build it from.
            if (array.Count > 0 && !header.Contains(TemplateNames.NameField, StringComparer.Ordinal))
                problems.Add(token, Key + " are named prefix + header." + TemplateNames.NameField + " + suffix, and the header has no \"" +
                                    TemplateNames.NameField + "\".");

            return types;
        }

        /// <summary>An optional string, empty when absent - or null, with the problem said, when it is not one.</summary>
        private static string Text(JsonProblems problems, JObject entry, string key, string at)
        {
            JToken token = entry[key];
            if (ManifestJson.Absent(token)) return string.Empty;
            if (token.Type == JTokenType.String) return (string)token;

            problems.Add(token, at + "." + key + " must be a string in quotes.");
            return null;
        }
    }
}
