using System;
using System.Collections.Generic;
using System.Linq;

using BlockTemplate.Manifest.Json;

using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Parsing
{
    /// <summary>
    /// The manifest's <c>variables</c>: typed - a list of strings, a bool, a number or a string -
    /// with a default that must fit its own <c>min</c> and <c>max</c>. A list's bounds count
    /// entries, a number's are values, and a bool or a string has neither.
    /// </summary>
    internal static class VariablesSection
    {
        public const string Key = "variables";

        private static readonly string[] Keys = { "id", "type", "min", "max", "default" };

        /// <param name="names">
        /// Every id a variable took, whatever else was wrong with it - so a TIA value named alike is
        /// said on the same read rather than once the variable's default is fixed. Codex's third
        /// review of stage 1.1 found the collision waiting for that.
        /// </param>
        public static List<TemplateVariable> Read(JsonProblems problems, JToken token, out HashSet<string> names)
        {
            List<TemplateVariable> variables = new List<TemplateVariable>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names = seen;

            JArray array = problems.List(token, Key, "a list of { \"id\": ..., \"type\": ... }");
            if (array == null) return variables;

            for (int i = 0; i < array.Count; i++)
            {
                string at = Key + "[" + i + "]";
                JObject entry = problems.Entry(array[i], at);
                if (entry == null) continue;

                problems.Keys(entry, Keys, at);

                string id = NameRules.Word(problems, entry, "id", at);
                bool unique = id != null && seen.Add(id);
                if (id != null && !unique)
                    problems.Add(entry["id"], at + ".id '" + id + "' is another variable's already.");

                TemplateVariableType? type = Type(problems, entry, at);
                if (type == null) continue;

                // The rest is checked whatever became of the id: a duplicate with a bad default is two
                // problems, and saying one per read is what this reader exists not to do.
                TemplateVariable variable = Variable(problems, entry, at, id ?? string.Empty, type.Value);
                if (variable != null && unique) variables.Add(variable);
            }

            return variables;
        }

        private static TemplateVariableType? Type(JsonProblems problems, JObject entry, string at)
        {
            JToken token = entry["type"];

            if (ManifestJson.Absent(token))
            {
                problems.Add(entry, at + " has no type: list, bool, numeric or string.");
                return null;
            }

            switch (token.Type == JTokenType.String ? (string)token : null)
            {
                case "list": return TemplateVariableType.List;
                case "bool": return TemplateVariableType.Bool;
                case "numeric": return TemplateVariableType.Numeric;
                case "string": return TemplateVariableType.String;
            }

            problems.Add(token, at + ".type must be list, bool, numeric or string, spelled so.");
            return null;
        }

        private static TemplateVariable Variable(JsonProblems problems, JObject entry, string at, string id, TemplateVariableType type)
        {
            JToken minToken = entry["min"];
            JToken maxToken = entry["max"];
            JToken defaultToken = entry["default"];

            if (type == TemplateVariableType.Bool || type == TemplateVariableType.String)
            {
                if (!ManifestJson.Absent(minToken)) problems.Add(minToken, at + ".min means nothing for a " + Spelled(type) + ": only a list and a number have one.");
                if (!ManifestJson.Absent(maxToken)) problems.Add(maxToken, at + ".max means nothing for a " + Spelled(type) + ": only a list and a number have one.");

                if (type == TemplateVariableType.Bool)
                {
                    if (ManifestJson.Absent(defaultToken)) return new TemplateVariable(id, type, null, null, false);
                    if (defaultToken.Type == JTokenType.Boolean) return new TemplateVariable(id, type, null, null, (bool)defaultToken);
                    problems.Add(defaultToken, at + ".default must be true or false.");
                    return null;
                }

                if (ManifestJson.Absent(defaultToken)) return new TemplateVariable(id, type, null, null, string.Empty);
                if (defaultToken.Type == JTokenType.String) return new TemplateVariable(id, type, null, null, (string)defaultToken);
                problems.Add(defaultToken, at + ".default must be a string in quotes.");
                return null;
            }

            bool list = type == TemplateVariableType.List;
            decimal? min = Bound(problems, minToken, at + ".min", list);
            decimal? max = Bound(problems, maxToken, at + ".max", list);

            // Said, and the default still checked for being the right kind of value - only not against
            // a range that does not exist. Codex's second review of stage 1.1 found it stopping here.
            bool inverted = min.HasValue && max.HasValue && min.Value > max.Value;
            if (inverted)
                problems.Add(maxToken, at + ".max is " + RangeText.Show(max.Value) + ", below its min of " + RangeText.Show(min.Value) + ".");

            if (list)
            {
                List<string> entries = new List<string>();

                if (!ManifestJson.Absent(defaultToken))
                {
                    JArray array = defaultToken as JArray;
                    if (array == null || array.Any(item => item.Type != JTokenType.String))
                    {
                        problems.Add(defaultToken, at + ".default must be a list of strings: [\"first\", \"second\"].");
                        return null;
                    }

                    entries.AddRange(array.Select(item => (string)item));
                }

                if (!inverted && RangeText.Outside(entries.Count, min, max))
                {
                    problems.Add(ManifestJson.Absent(defaultToken) ? (JToken)entry : defaultToken,
                                 at + ".default holds " + entries.Count + " entries, outside its own " + RangeText.Of(min, max) + ".");
                    return null;
                }

                return inverted ? null : new TemplateVariable(id, type, min, max, entries.AsReadOnly());
            }

            decimal value;
            if (ManifestJson.Absent(defaultToken))
            {
                value = min ?? 0m;
            }
            else
            {
                decimal? number = ExactNumbers.Read(problems, defaultToken, at + ".default");
                if (number == null) return null;
                value = number.Value;
            }

            if (!inverted && RangeText.Outside(value, min, max))
            {
                problems.Add(ManifestJson.Absent(defaultToken) ? (JToken)entry : defaultToken,
                             at + ".default is " + RangeText.Show(value) + ", outside its own " + RangeText.Of(min, max) + ".");
                return null;
            }

            return inverted ? null : new TemplateVariable(id, type, min, max, value);
        }

        /// <summary>
        /// A list's bound is a whole count, never negative; a number's bound is any number. Null when
        /// absent, or when it is not one - said.
        /// </summary>
        private static decimal? Bound(JsonProblems problems, JToken token, string at, bool count)
        {
            if (ManifestJson.Absent(token)) return null;

            if (count)
            {
                if (token.Type == JTokenType.Integer)
                {
                    decimal? entries = ExactNumbers.Read(problems, token, at);
                    if (entries == null) return null;
                    if (entries.Value >= 0) return entries;
                }

                problems.Add(token, at + " must be a whole number of entries, 0 or more.");
                return null;
            }

            return ExactNumbers.Read(problems, token, at);
        }

        private static string Spelled(TemplateVariableType type) => type.ToString().ToLowerInvariant();
    }
}
