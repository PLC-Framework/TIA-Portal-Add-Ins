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

        /// <summary>
        /// The sections an object rule's interface may list, spelled as TIA spells them: the
        /// six of a block interface and the two of a tag table. Taken from Core, the same
        /// constants the validator holds the file to.
        /// </summary>
        public static readonly Applies InterfaceSections = new Applies(
            "interface", "Interface",
            Core.Config.CodingStyleNames.Input, Core.Config.CodingStyleNames.Output,
            Core.Config.CodingStyleNames.InOut, Core.Config.CodingStyleNames.Static,
            Core.Config.CodingStyleNames.Temp, Core.Config.CodingStyleNames.Constant,
            Core.Config.CodingStyleNames.Tag, Core.Config.CodingStyleNames.UserConstant);

        private readonly ConfigDocument _document;

        public CodingStyleEditor(ConfigDocument document)
        {
            _document = document;
            MigrateLegacyCatalogue();
        }

        /// <summary>
        /// Moves the object-rule catalogue off its former key, once, when the document is
        /// opened.
        ///
        /// The rename happens on the JSON tree, so it reaches the file the first time
        /// somebody saves - which is the whole migration story, and it asks the user to know
        /// nothing about a key that used to be called something else.
        ///
        /// **The property is replaced rather than removed and re-added**, so the catalogue
        /// keeps its position in the section. Adding a new one would drop it at the end for
        /// no reason a reader could see, and this file is meant to be read by hand.
        ///
        /// A document carrying both keys is left exactly as it is: Core reports that, and
        /// guessing which of the two the author meant is not a decision to make silently.
        /// </summary>
        private void MigrateLegacyCatalogue()
        {
            JObject style = _document.ObjectAt(Path);
            if (style == null) return;

            JProperty legacy = style.Property(LegacyRulesKey);
            if (legacy == null || style[ObjectRulesKey] != null) return;

            legacy.Replace(new JProperty(ObjectRulesKey, legacy.Value.DeepClone()));
        }

        // ------------------------------------------------------------------- rules

        public const string ObjectRulesKey = "objectRules";
        public const string InterfaceRulesKey = "interfaceRules";
        public const string LegacyRulesKey = "rules";

        /// <summary>
        /// One of the two catalogues.
        ///
        /// The object catalogue is required, so it is created on first touch. The interface
        /// catalogue is optional and is **only created by adding to it**: reading it to fill
        /// a list must not plant an empty <c>interfaceRules</c> in a file that never had one,
        /// or merely opening the section would change what the next save writes.
        /// </summary>
        private JArray Catalogue(RuleCatalogue catalogue, bool create = false) =>
            catalogue == RuleCatalogue.Interface
                ? _document.ArrayAt(Path + "." + InterfaceRulesKey, create)
                : _document.ArrayAt(Path + "." + ObjectRulesKey, true);

        private IEnumerable<JObject> RulesOf(RuleCatalogue catalogue) =>
            Catalogue(catalogue)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

        public IReadOnlyList<string> RuleIds(RuleCatalogue catalogue) =>
            RulesOf(catalogue)
                .Select(rule => rule["id"]?.Value<string>())
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();

        public JObject Rule(RuleCatalogue catalogue, string id) =>
            RulesOf(catalogue)
                .FirstOrDefault(rule => string.Equals(rule["id"]?.Value<string>(), id, StringComparison.Ordinal));

        /// <summary>
        /// Adds a rule with a name nothing else uses, in either catalogue. Appended rather
        /// than inserted: order carries no meaning here, and a new entry at the end is where
        /// the eye looks.
        /// </summary>
        public JObject AddRule(RuleCatalogue catalogue)
        {
            string id = Unused(catalogue == RuleCatalogue.Interface ? "new_interface_rule" : "new_rule");

            JObject rule = new JObject
            {
                ["id"] = id,
                // Matches nothing on purpose. An empty pattern would match *everything*,
                // which is a naming rule that approves every name ever written.
                ["regex"] = "^$",
                ["descriptions"] = new JArray()
            };

            Catalogue(catalogue, create: true).Add(rule);
            return rule;
        }

        /// <summary>Removes a rule and every reference to it, which would otherwise dangle.</summary>
        public void RemoveRule(RuleCatalogue catalogue, string id)
        {
            JObject rule = Rule(catalogue, id);
            if (rule == null) return;

            rule.Remove();

            if (catalogue == RuleCatalogue.Interface)
            {
                RemoveInterfaceReferences(id);

                // Optional, and nothing is left to say: an empty list is noise a hand-edited
                // file does not need, the same call the hierarchy makes for an empty groups.
                JArray remaining = Catalogue(RuleCatalogue.Interface);
                if (remaining != null && remaining.Count == 0) remaining.Parent?.Remove();

                return;
            }

            foreach (JArray implements in ObjectRuleReferences())
            {
                foreach (JToken reference in implements
                             .Where(token => string.Equals(token.Value<string>(), id, StringComparison.Ordinal))
                             .ToList())
                {
                    reference.Remove();
                }
            }
        }

        /// <summary>
        /// Takes an interface rule out of every section that named it.
        ///
        /// **A section left with no rules goes too, and so does an interface left with no
        /// sections.** That is not tidiness. A section that implements nothing is invalid,
        /// so leaving it would block Save over something the operator just did on purpose;
        /// and it would mean nothing anyway - a section no rule covers is a section that is
        /// not checked, which is exactly what removing it says. A type left with nothing to
        /// implement is kept instead, because its row is on screen to be fixed; an interface
        /// section's is not, and removing the last rule was the only thing anyone asked for.
        /// </summary>
        private void RemoveInterfaceReferences(string id)
        {
            foreach (JObject rule in RulesOf(RuleCatalogue.Object).ToList())
            {
                JArray sections = rule["interface"] as JArray;
                if (sections == null) continue;

                foreach (JObject section in sections.OfType<JObject>().ToList())
                {
                    JArray implements = section["implements"] as JArray;
                    if (implements == null) continue;

                    List<JToken> references = implements
                        .Where(token => string.Equals(token.Value<string>(), id, StringComparison.Ordinal))
                        .ToList();

                    if (references.Count == 0) continue;

                    foreach (JToken reference in references) reference.Remove();
                    if (implements.Count == 0) section.Remove();
                }

                DropEmptyInterface(rule);
            }
        }

        /// <summary>
        /// Renames a rule **and carries its references with it**. Without that, renaming
        /// breaks every place that uses it, silently and at a distance.
        /// </summary>
        /// <returns>The name actually used, which may be adjusted to keep ids unique.</returns>
        public string RenameRule(RuleCatalogue catalogue, string oldId, string newId)
        {
            JObject rule = Rule(catalogue, oldId);
            if (rule == null) return oldId;

            newId = (newId ?? string.Empty).Trim();
            if (newId.Length == 0 || string.Equals(newId, oldId, StringComparison.Ordinal)) return oldId;

            // Uniqueness is a rule of the file, and it spans both catalogues so that an id in
            // a report names exactly one rule. Kept here rather than left for the validator
            // to complain about after the fact.
            if (AllRuleIds().Contains(newId)) newId = Unused(newId);

            rule["id"] = newId;

            IEnumerable<JArray> references = catalogue == RuleCatalogue.Interface
                ? InterfaceRuleReferences()
                : ObjectRuleReferences();

            foreach (JArray implements in references)
            {
                for (int i = 0; i < implements.Count; i++)
                {
                    if (string.Equals(implements[i].Value<string>(), oldId, StringComparison.Ordinal))
                        implements[i] = newId;
                }
            }

            return newId;
        }

        private HashSet<string> AllRuleIds() =>
            new HashSet<string>(RuleIds(RuleCatalogue.Object).Concat(RuleIds(RuleCatalogue.Interface)),
                                StringComparer.Ordinal);

        // ------------------------------------------------------------------- an object rule's interface

        /// <summary>The sections of an object rule's interface, in file order. Empty when it has none.</summary>
        public static IReadOnlyList<JObject> InterfaceOf(JObject rule) =>
            (rule?["interface"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();

        /// <summary>
        /// Adds a section with nothing to implement yet - the same starting point a new type
        /// gets, and invalid in the same visible way until a rule is chosen. The interface is
        /// created on the way when the rule had none.
        /// </summary>
        public static JObject AddSection(JObject rule, string type)
        {
            JArray sections = rule["interface"] as JArray;

            if (sections == null)
            {
                sections = new JArray();
                rule["interface"] = sections;
            }

            JObject section = new JObject
            {
                ["type"] = type,
                ["implements"] = new JArray()
            };

            sections.Add(section);
            return section;
        }

        /// <summary>
        /// Drops an interface left with no sections. <c>interface</c> is optional and absent
        /// already means "not checked", so an empty list would only be a second way of
        /// saying the same thing.
        /// </summary>
        public static void DropEmptyInterface(JObject rule)
        {
            if (rule?["interface"] is JArray sections && sections.Count == 0) rule.Remove("interface");
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

        /// <summary>
        /// Every array that references an **object** rule: the <c>implements</c> of each
        /// type object, and nothing else.
        ///
        /// <c>RemoveRule</c> and <c>RenameRule</c> walk this rather than reaching for
        /// <c>entry["implements"]</c> themselves, and that is the point of it existing. A
        /// reference the walk does not reach is a dangling id that survives the edit and
        /// surfaces later in the validator - which is exactly what those two methods exist
        /// to prevent.
        ///
        /// An object rule's <c>interface</c> is deliberately **not** here: those sections
        /// reference interface rules, a different catalogue, so an object rule's id can
        /// never appear in one. See <see cref="InterfaceRuleReferences"/>.
        /// </summary>
        private IEnumerable<JArray> ObjectRuleReferences()
        {
            foreach (JObject entry in AllObjects())
            {
                JArray implements = entry["implements"] as JArray;
                if (implements != null) yield return implements;
            }
        }

        /// <summary>
        /// Every array that references an **interface** rule: the <c>implements</c> of each
        /// section of each object rule. The counterpart of <see cref="ObjectRuleReferences"/>,
        /// and the only place a rename of an interface rule looks.
        /// </summary>
        private IEnumerable<JArray> InterfaceRuleReferences()
        {
            foreach (JObject rule in RulesOf(RuleCatalogue.Object))
            {
                JArray sections = rule["interface"] as JArray;
                if (sections == null) continue;

                foreach (JObject section in sections.OfType<JObject>())
                {
                    JArray implements = section["implements"] as JArray;
                    if (implements != null) yield return implements;
                }
            }
        }

        /// <summary>A name like the one asked for, that no rule in either catalogue is using.</summary>
        private string Unused(string wanted)
        {
            HashSet<string> taken = AllRuleIds();
            if (!taken.Contains(wanted)) return wanted;

            for (int n = 2; ; n++)
            {
                string candidate = wanted + "_" + n;
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }

    /// <summary>
    /// Which of the two rule catalogues an operation is about. They never stand in for one
    /// another: an object rule is reached from a type's implements, an interface rule only
    /// from an object rule's interface.
    /// </summary>
    public enum RuleCatalogue
    {
        Object,
        Interface
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
