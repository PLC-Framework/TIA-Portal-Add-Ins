using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Core.Config.Validation
{
    /// <summary>
    /// The <c>codingStyle</c> section: a catalogue of naming rules, and which TIA object
    /// types accept each of them.
    ///
    /// This is the section where a typo is invisible without a validator. An
    /// <c>implements</c> entry that names a rule which does not exist compiles, loads and
    /// runs - it simply never matches anything, and a naming check that silently passes
    /// everything is worse than no naming check.
    /// </summary>
    public static class CodingStyleValidator
    {
        // Closed sets, per section. Read off the object types TIA actually exposes; a
        // value outside them cannot ever match a real object, so it is a broken file.
        private static readonly string[] BlockTypes =
            { "OB", "ArrayDB", "GlobalDB", "InstanceDB", "FC", "FB" };

        private static readonly string[] TechnologyObjectTypes = { "TechnologicalInstanceDB" };
        private static readonly string[] TagTableTypes = { "PlcTagTable" };
        private static readonly string[] TypeTypes = { "PlcStruct" };
        private static readonly string[] AlarmTextListTypes = { "AlarmTexts" };

        public static ValidationResult Validate(CodingStyle style, string path = "codingStyle")
        {
            Issues issues = new Issues();
            Collect(style, path, issues);
            return new ValidationResult(issues.All);
        }

        internal static void Collect(CodingStyle style, string path, Issues issues)
        {
            if (!issues.RequiredObject(path, style)) return;

            HashSet<string> ruleIds = Rules(style.Rules, Issues.Field(path, "rules"), issues);

            Objects(style.Blocks, Issues.Field(path, "blocks"), BlockTypes, ruleIds, issues);
            Objects(style.TechnologyObjects, Issues.Field(path, "technologyObjects"), TechnologyObjectTypes, ruleIds, issues);
            Objects(style.TagTables, Issues.Field(path, "tagTables"), TagTableTypes, ruleIds, issues);
            Objects(style.Types, Issues.Field(path, "types"), TypeTypes, ruleIds, issues);
            Objects(style.AlarmTextLists, Issues.Field(path, "alarmTextLists"), AlarmTextListTypes, ruleIds, issues);
        }

        /// <summary>Validates the catalogue and returns the ids that can be referenced.</summary>
        private static HashSet<string> Rules(IReadOnlyList<Rule> rules, string path, Issues issues)
        {
            // Ordinal: a rule id is a key, not prose. "type" and "Type" are two ids, and
            // an implements entry has to spell one of them exactly.
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            if (!issues.RequiredList(path, rules)) return ids;

            for (int i = 0; i < rules.Count; i++)
            {
                Rule rule = rules[i];
                string here = Issues.At(path, i);

                if (!issues.RequiredObject(here, rule)) continue;

                string id = Issues.Field(here, "id");
                if (issues.Required(id, rule.Id) && !ids.Add(rule.Id))
                    issues.Add(id, "'" + rule.Id + "' is already used by another rule. Ids must be unique.");

                string regex = Issues.Field(here, "regex");
                if (issues.Required(regex, rule.Regex)) Compiles(rule.Regex, regex, issues);

                // descriptions is optional: a rule with no prose still works.
            }

            return ids;
        }

        /// <summary>
        /// A pattern that does not compile is a **structural** error, not an environmental
        /// one: the file is broken, and it is broken the same way on every machine.
        /// </summary>
        private static void Compiles(string pattern, string path, Issues issues)
        {
            try
            {
                // Constructing is what parses it. The result is thrown away on purpose;
                // caching belongs to whoever actually matches names against it.
                new Regex(pattern);
            }
            catch (ArgumentException exception)
            {
                issues.Add(path, "Not a valid regular expression: " + exception.Message);
            }
        }

        private static void Objects(
            IReadOnlyList<PlcTypeObject> objects,
            string path,
            string[] allowedTypes,
            HashSet<string> ruleIds,
            Issues issues)
        {
            if (!issues.RequiredList(path, objects)) return;

            for (int i = 0; i < objects.Count; i++)
            {
                PlcTypeObject entry = objects[i];
                string here = Issues.At(path, i);

                if (!issues.RequiredObject(here, entry)) continue;

                string type = Issues.Field(here, "type");
                if (issues.Required(type, entry.Type))
                    issues.OneOf(type, entry.Type, allowedTypes);

                string implements = Issues.Field(here, "implements");

                // RequiredObject, not RequiredList: an empty list is meaningful for a
                // section - "this concern has no folders" - but meaningless here, so the
                // "use [] to say there are none" wording would be pointing the reader the
                // wrong way.
                if (!issues.RequiredObject(implements, entry.Implements)) continue;

                if (entry.Implements.Count == 0)
                {
                    issues.Add(implements,
                        "Empty. A type that implements no rule is never checked, which is " +
                        "the same as leaving it out.");
                    continue;
                }

                for (int r = 0; r < entry.Implements.Count; r++)
                {
                    string reference = Issues.At(implements, r);
                    string ruleId = entry.Implements[r];

                    if (!issues.Required(reference, ruleId)) continue;

                    // Only worth reporting when the catalogue itself was readable;
                    // otherwise every reference would be flagged for a problem that is
                    // already reported once, above.
                    if (ruleIds.Count > 0 && !ruleIds.Contains(ruleId))
                        issues.Add(reference, "No rule with id '" + ruleId + "'.");
                }
            }
        }
    }
}
