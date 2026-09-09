using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

namespace Satellite.ConfigEditor.Document
{
    /// <summary>
    /// The <c>codingStyle</c> section, as operations rather than as a shape.
    ///
    /// The rules and the types that implement them are **two halves of one thing**: an
    /// <c>implements</c> entry naming a rule that does not exist compiles, loads and runs,
    /// and simply never matches anything — a naming check that silently passes everything,
    /// which is worse than no check. So renaming and removing live here, where both halves
    /// can be kept in step, rather than in the window.
    /// </summary>
    public sealed class CodingStyleEditor
    {
        public const string Path = "projectConfig.codingStyle";

        /// <summary>
        /// The five lists of type objects, and the closed set of <c>type</c> each accepts.
        /// A value outside them cannot ever match a real TIA object.
        /// </summary>
        public static readonly IReadOnlyList<Applies> Sections = new[]
        {
            new Applies("blocks", "Blocks", "OB", "ArrayDB", "GlobalDB", "InstanceDB", "FC", "FB"),
            new Applies("technologyObjects", "Technology objects", "TechnologicalInstanceDB"),
            new Applies("tagTables", "Tag tables", "PlcTagTable"),
            new Applies("types", "Types", "PlcStruct"),
            new Applies("alarmTextLists", "Alarm text lists", "AlarmTexts")
        };

        private readonly ConfigDocument _document;

        public CodingStyleEditor(ConfigDocument document)
        {
            _document = document;
        }

        // ------------------------------------------------------------------- rules

        public JArray Rules => _document.ArrayAt(Path + ".rules", true);

        public IReadOnlyList<string> RuleIds() =>
            Rules.OfType<JObject>()
                 .Select(rule => rule["id"]?.Value<string>())
                 .Where(id => !string.IsNullOrEmpty(id))
                 .ToList();

        public JObject Rule(string id) =>
            Rules.OfType<JObject>()
                 .FirstOrDefault(rule => string.Equals(rule["id"]?.Value<string>(), id,
                                                       StringComparison.Ordinal));

        /// <summary>
        /// Adds a rule with a name nothing else uses. Appended rather than inserted: order
        /// carries no meaning here, and a new entry at the end is where the eye looks.
        /// </summary>
        public JObject AddRule()
        {
            string id = Unused("new_rule");

            JObject rule = new JObject
            {
                ["id"] = id,
                // Matches nothing on purpose. An empty pattern would match *everything*,
                // which is a naming rule that approves every name ever written.
                ["regex"] = "^$",
                ["descriptions"] = new JArray()
            };

            Rules.Add(rule);
            return rule;
        }

        /// <summary>Removes a rule and every reference to it, which would otherwise dangle.</summary>
        public void RemoveRule(string id)
        {
            JObject rule = Rule(id);
            if (rule == null) return;

            rule.Remove();

            foreach (JObject entry in AllObjects())
            {
                JArray implements = entry["implements"] as JArray;
                if (implements == null) continue;

                foreach (JToken reference in implements
                             .Where(token => string.Equals(token.Value<string>(), id, StringComparison.Ordinal))
                             .ToList())
                {
                    reference.Remove();
                }
            }
        }

        /// <summary>
        /// Renames a rule **and carries its references with it**. Without that, renaming
        /// breaks every type that implements it, silently and at a distance.
        /// </summary>
        /// <returns>The name actually used, which may be adjusted to keep ids unique.</returns>
        public string RenameRule(string oldId, string newId)
        {
            JObject rule = Rule(oldId);
            if (rule == null) return oldId;

            newId = (newId ?? string.Empty).Trim();
            if (newId.Length == 0 || string.Equals(newId, oldId, StringComparison.Ordinal)) return oldId;

            // Uniqueness is a rule of the file, so it is kept here rather than left for the
            // validator to complain about after the fact.
            if (RuleIds().Any(id => string.Equals(id, newId, StringComparison.Ordinal)))
                newId = Unused(newId);

            rule["id"] = newId;

            foreach (JObject entry in AllObjects())
            {
                JArray implements = entry["implements"] as JArray;
                if (implements == null) continue;

                for (int i = 0; i < implements.Count; i++)
                {
                    if (string.Equals(implements[i].Value<string>(), oldId, StringComparison.Ordinal))
                        implements[i] = newId;
                }
            }

            return newId;
        }

        /// <summary>The descriptions as text, one per line - which is how they are edited.</summary>
        public static string DescriptionsOf(JObject rule)
        {
            JArray lines = rule?["descriptions"] as JArray;
            if (lines == null) return string.Empty;

            return string.Join(Environment.NewLine,
                lines.Select(line => line.Value<string>() ?? string.Empty));
        }

        public static void SetDescriptions(JObject rule, string text)
        {
            if (rule == null) return;

            if (string.IsNullOrWhiteSpace(text))
            {
                // Optional, so an emptied box removes the key rather than leaving [] behind.
                rule.Remove("descriptions");
                return;
            }

            JArray lines = new JArray();

            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                lines.Add(line);

            rule["descriptions"] = lines;
        }

        /// <summary>Whether a pattern compiles, and why not when it does not.</summary>
        public static string RegexProblem(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return "Required.";

            try
            {
                new Regex(pattern);
                return null;
            }
            catch (ArgumentException exception)
            {
                return exception.Message;
            }
        }

        /// <summary>
        /// Whether a sample name matches. A pattern that compiles can still be perfectly
        /// wrong, and this is the only way to find that out before the Add-In marks half a
        /// project.
        /// </summary>
        public static bool? Matches(string pattern, string sample)
        {
            if (string.IsNullOrEmpty(sample) || RegexProblem(pattern) != null) return null;

            try { return Regex.IsMatch(sample, pattern); }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------- applies to

        public JArray Objects(string section) =>
            _document.ArrayAt(Path + "." + section, true);

        public JObject AddObject(string section, string type)
        {
            JObject entry = new JObject
            {
                ["type"] = type,
                ["implements"] = new JArray()
            };

            Objects(section).Add(entry);
            return entry;
        }

        public static IReadOnlyList<string> ImplementsOf(JObject entry)
        {
            JArray implements = entry?["implements"] as JArray;

            return implements == null
                ? new List<string>()
                : implements.Select(token => token.Value<string>()).ToList();
        }

        public static void SetImplements(JObject entry, IEnumerable<string> ids)
        {
            if (entry == null) return;

            JArray implements = new JArray();
            foreach (string id in ids) implements.Add(id);

            entry["implements"] = implements;
        }

        private IEnumerable<JObject> AllObjects() =>
            Sections.SelectMany(section => Objects(section.Key).OfType<JObject>());

        /// <summary>A name like the one asked for, that no rule is using.</summary>
        private string Unused(string wanted)
        {
            List<string> taken = RuleIds().ToList();
            if (!taken.Contains(wanted)) return wanted;

            for (int n = 2; ; n++)
            {
                string candidate = wanted + "_" + n;
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }

    /// <summary>One of the five lists, and the type names it accepts.</summary>
    public sealed class Applies
    {
        public Applies(string key, string title, params string[] types)
        {
            Key = key;
            Title = title;
            Types = types;
        }

        /// <summary>The key in the document.</summary>
        public string Key { get; }

        /// <summary>What the window calls it.</summary>
        public string Title { get; }

        /// <summary>The closed set this section's <c>type</c> must come from.</summary>
        public string[] Types { get; }
    }
}
