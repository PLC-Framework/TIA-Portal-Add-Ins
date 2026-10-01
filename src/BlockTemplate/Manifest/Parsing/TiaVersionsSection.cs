using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using BlockTemplate.Manifest.Json;

using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Parsing
{
    /// <summary>
    /// The manifest's <c>tiaVersions</c>: what differs between TIA Portal versions and the operator
    /// never sees, each version setting the same names.
    /// </summary>
    internal static class TiaVersionsSection
    {
        public const string Key = "tiaVersions";

        private static readonly string[] Keys = { "version", "variables" };
        private static readonly string[] ValueKeys = { "id", "value" };

        /// <summary>
        /// A TIA Portal major version, spelled one way only: <c>"020"</c> would be a second entry for
        /// V20 and the one the executable never asks for. Codex's thirteenth review of stage 1.1.
        /// </summary>
        private static readonly Regex TiaMajor = new Regex("^[1-9][0-9]*$", RegexOptions.CultureInvariant);

        /// <param name="taken">The variables' names, which a TIA version's value may not take too.</param>
        public static List<TiaVersion> Read(JsonProblems problems, JToken token, HashSet<string> taken)
        {
            List<TiaVersion> versions = new List<TiaVersion>();
            JArray array = problems.List(token, Key, "a list of { \"version\": \"20\", \"variables\": [...] }");
            if (array == null) return versions;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<JToken> entries = new List<JToken>();
            List<string> labels = new List<string>();
            List<List<string>> names = new List<List<string>>();

            for (int i = 0; i < array.Count; i++)
            {
                string at = Key + "[" + i + "]";
                JObject entry = problems.Entry(array[i], at);
                if (entry == null) continue;

                problems.Keys(entry, Keys, at);

                string version = Major(problems, entry, at);
                if (version != null && !seen.Add(version))
                {
                    problems.Add(entry["version"], at + ".version " + version + " is listed already.");
                    version = null;
                }

                List<TiaVersionVariable> values = Values(problems, entry["variables"], at, taken, out List<string> named);

                // An entry whose version did not read is still held to the names the others set, and
                // still says which names they share: the label and the names are two facts. Codex's
                // ninth and tenth reviews of stage 1.1 found each waiting on the label being fixed.
                entries.Add(entry);
                labels.Add(version == null ? at : Key + " entry " + version);
                names.Add(named);

                if (version != null) versions.Add(new TiaVersion(version, values));
            }

            // Every version names the same values, or rendering for one of them leaves a hole where
            // the template expected a value. **Spelled exactly the same**: Scriban tells schemaVersion
            // from SchemaVersion, so a comparison that did not would pass the very hole it exists to
            // catch (the third review). **By the names each version gives, whatever their values**: a
            // name with a bad value is still set (the eighth).
            List<string> all = names.SelectMany(n => n).Distinct(StringComparer.Ordinal).ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                foreach (string id in all.Where(id => !names[i].Contains(id, StringComparer.Ordinal)))
                {
                    string near = names[i].FirstOrDefault(n => string.Equals(n, id, StringComparison.OrdinalIgnoreCase));

                    problems.Add(entries[i], labels[i] + " does not set '" + id +
                                             "', which another version does: rendering for it would leave it empty." +
                                             (near == null ? string.Empty
                                                           : " It sets '" + near + "', and a template reads the two as different names."));
                }
            }

            return versions;
        }

        private static string Major(JsonProblems problems, JObject entry, string at)
        {
            JToken token = entry["version"];

            if (ManifestJson.Absent(token))
            {
                problems.Add(entry, at + " has no version: the TIA Portal major version, \"20\" or \"21\".");
                return null;
            }

            // A number is taken as written: Json.NET reads 020 as octal 16, which is not a TIA Portal.
            string version = token.Type == JTokenType.String ? (string)token
                           : token.Type == JTokenType.Integer ? problems.Literal(token) ?? Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture)
                           : null;

            if (version != null && token.Type == JTokenType.Integer && !ExactNumbers.IsJsonNumber(version)) version = null;

            if (version == null || !TiaMajor.IsMatch(version))
            {
                problems.Add(token, at + ".version must be a TIA Portal major version in digits, such as \"20\", with no leading zero.");
                return null;
            }

            return version;
        }

        /// <param name="named">Every name this version gives a value, whether or not the value read.</param>
        private static List<TiaVersionVariable> Values(JsonProblems problems, JToken token, string at, HashSet<string> taken,
                                                       out List<string> named)
        {
            List<TiaVersionVariable> values = new List<TiaVersionVariable>();
            named = new List<string>();
            JArray array = problems.List(token, at + ".variables", "a list of { \"id\": ..., \"value\": ... }");
            if (array == null) return values;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < array.Count; i++)
            {
                string where = at + ".variables[" + i + "]";
                JObject entry = problems.Entry(array[i], where);
                if (entry == null) continue;

                problems.Keys(entry, ValueKeys, where);

                string id = NameRules.Word(problems, entry, "id", where);
                if (id != null && taken.Contains(id))
                {
                    problems.Add(entry["id"], where + ".id '" + id + "' is the name of a variable too: the template could not tell the two apart.");
                    id = null;
                }
                else if (id != null && !seen.Add(id))
                {
                    problems.Add(entry["id"], where + ".id '" + id + "' is set already for this version.");
                    id = null;
                }

                if (id != null) named.Add(id);

                object value = Scalar(problems, entry, "value", where);
                if (id != null && value != null) values.Add(new TiaVersionVariable(id, value));
            }

            return values;
        }

        /// <summary>A required string, number or bool - or null, with the problem said.</summary>
        private static object Scalar(JsonProblems problems, JObject entry, string key, string at)
        {
            JToken token = entry[key];

            if (ManifestJson.Absent(token))
            {
                problems.Add(entry, at + " has no " + key + ".");
                return null;
            }

            switch (token.Type)
            {
                case JTokenType.String: return (string)token;
                case JTokenType.Boolean: return (bool)token;
                case JTokenType.Integer:
                case JTokenType.Float: return ExactNumbers.Read(problems, token, at + "." + key);
            }

            problems.Add(token, at + "." + key + " must be a string, a number or true or false.");
            return null;
        }
    }
}
