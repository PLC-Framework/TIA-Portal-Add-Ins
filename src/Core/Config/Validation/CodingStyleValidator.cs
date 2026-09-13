using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Core.Config.Validation
{
    /// <summary>
    /// The <c>codingStyle</c> section: two catalogues of naming rules, and which TIA object
    /// types accept each of them.
    ///
    /// This is the section where a typo is invisible without a validator. An
    /// <c>implements</c> entry that names a rule which does not exist compiles, loads and
    /// runs - it simply never matches anything, and a naming check that silently passes
    /// everything is worse than no naming check.
    ///
    /// The catalogue is split in two because the two kinds of rule are referenced from
    /// different places and can never stand in for one another: <c>objectRules</c> name TIA
    /// objects and are referenced by a type's <c>implements</c>; <c>interfaceRules</c> name
    /// what lives inside one and are referenced only from an object rule's
    /// <c>interface</c>. Ids stay unique across both anyway, so that an id in a report or
    /// an error message identifies exactly one rule.
    /// </summary>
    public static class CodingStyleValidator
    {
        // Closed sets, per section. Read off the object types TIA actually exposes; a
        // value outside them cannot ever match a real object, so it is a broken file.
        // The strings themselves live in CodingStyleNames, because the Add-In has to spell
        // them too when it names what it found in a PLC.
        private static readonly string[] BlockTypes =
        {
            CodingStyleNames.OB, CodingStyleNames.ArrayDB, CodingStyleNames.GlobalDB,
            CodingStyleNames.InstanceDB, CodingStyleNames.FC, CodingStyleNames.FB
        };

        private static readonly string[] TechnologyObjectTypes = { CodingStyleNames.TechnologicalInstanceDB };
        private static readonly string[] TagTableTypes = { CodingStyleNames.PlcTagTable };
        private static readonly string[] TypeTypes = { CodingStyleNames.PlcStruct };
        private static readonly string[] AlarmTextListTypes = { CodingStyleNames.AlarmTexts };

        // Where members live, spelled as TIA spells it - the same rule the five sets above
        // follow, and the one that lets a check compare without folding case. The first six
        // are the sections of a block interface; the last two are a tag table's, which are
        // two different things and are named apart on purpose: a tag and a user constant
        // answer to different rules.
        private static readonly string[] InterfaceSectionTypes =
        {
            CodingStyleNames.Input, CodingStyleNames.Output, CodingStyleNames.InOut,
            CodingStyleNames.Static, CodingStyleNames.Temp, CodingStyleNames.Constant,
            CodingStyleNames.Tag, CodingStyleNames.UserConstant
        };

        public static ValidationResult Validate(CodingStyle style, string path = "codingStyle")
        {
            Issues issues = new Issues();
            Collect(style, path, issues);
            return new ValidationResult(issues.All);
        }

        internal static void Collect(CodingStyle style, string path, Issues issues)
        {
            if (!issues.RequiredObject(path, style)) return;

            if (style.ObjectRules != null && style.LegacyRules != null)
            {
                issues.Add(Issues.Field(path, "rules"),
                    "Both 'objectRules' and 'rules' are present. 'rules' is the former name " +
                    "of the same list and is ignored; remove it.");
            }

            // One set across both catalogues. Ids are keys, and a key that identifies two
            // different rules is the kind of thing a report cannot explain to its reader.
            HashSet<string> taken = new HashSet<string>(StringComparer.Ordinal);

            // Interface rules first: an object rule's interface references them, so they
            // have to be known before the references are checked.
            HashSet<string> interfaceIds = Catalogue(
                style.InterfaceRules, Issues.Field(path, "interfaceRules"),
                false, false, taken, null, issues);

            HashSet<string> objectIds = Catalogue(
                style.Catalogue, Issues.Field(path, style.CatalogueKey),
                true, true, taken, interfaceIds, issues);

            Objects(style.Blocks, Issues.Field(path, "blocks"), BlockTypes, objectIds, issues);
            Objects(style.TechnologyObjects, Issues.Field(path, "technologyObjects"), TechnologyObjectTypes, objectIds, issues);
            Objects(style.TagTables, Issues.Field(path, "tagTables"), TagTableTypes, objectIds, issues);
            Objects(style.Types, Issues.Field(path, "types"), TypeTypes, objectIds, issues);
            Objects(style.AlarmTextLists, Issues.Field(path, "alarmTextLists"), AlarmTextListTypes, objectIds, issues);
        }

        /// <summary>
        /// Validates one catalogue and returns the ids it contributes.
        /// </summary>
        /// <param name="required">
        /// Object rules are required; interface rules are not, so that a configuration
        /// written before the catalogue was split stays valid rather than being reported as
        /// broken on a machine that only opened it.
        /// </param>
        /// <param name="interfacesAllowed">
        /// Only an object rule may declare an interface. A rule that names a variable has
        /// nothing inside it, and silently ignoring the key would hide a misplaced rule.
        /// </param>
        private static HashSet<string> Catalogue(
            IReadOnlyList<Rule> rules,
            string path,
            bool required,
            bool interfacesAllowed,
            HashSet<string> taken,
            HashSet<string> interfaceIds,
            Issues issues)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            if (rules == null)
            {
                if (required) issues.RequiredList(path, rules);
                return ids;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                Rule rule = rules[i];
                string here = Issues.At(path, i);

                if (!issues.RequiredObject(here, rule)) continue;

                string id = Issues.Field(here, "id");
                if (issues.Required(id, rule.Id))
                {
                    if (taken.Add(rule.Id)) ids.Add(rule.Id);
                    else issues.Add(id, "'" + rule.Id + "' is already used by another rule. Ids must be unique.");
                }

                string regex = Issues.Field(here, "regex");
                if (issues.Required(regex, rule.Regex)) Compiles(rule.Regex, regex, issues);

                // descriptions is optional: a rule with no prose still works.

                string iface = Issues.Field(here, "interface");

                if (rule.Interface == null) continue;

                if (!interfacesAllowed)
                {
                    issues.Add(iface,
                        "Only an object rule carries an interface. This rule names what " +
                        "lives inside an object, which has no interface of its own.");
                    continue;
                }

                Sections(rule.Interface, iface, interfaceIds, issues);
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

        /// <summary>
        /// The optional <c>interface</c> of one object rule: which rules the members of each
        /// section answer to.
        /// </summary>
        private static void Sections(
            IReadOnlyList<InterfaceSection> sections,
            string path,
            HashSet<string> interfaceIds,
            Issues issues)
        {
            // Ordinal, like a rule id: these are keys matched against TIA's own spelling,
            // not prose.
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < sections.Count; i++)
            {
                InterfaceSection section = sections[i];
                string here = Issues.At(path, i);

                if (!issues.RequiredObject(here, section)) continue;

                string type = Issues.Field(here, "type");
                if (issues.Required(type, section.Type))
                {
                    issues.OneOf(type, section.Type, InterfaceSectionTypes);

                    // A second entry for the same section says nothing the first does not,
                    // and leaves the reader asking which of the two applies.
                    if (!seen.Add(section.Type))
                        issues.Add(type, "'" + section.Type + "' is already listed for this rule.");
                }

                string implements = Issues.Field(here, "implements");

                if (!issues.RequiredObject(implements, section.Implements)) continue;

                if (section.Implements.Count == 0)
                {
                    issues.Add(implements,
                        "Empty. A section that implements no rule is never checked, which " +
                        "is the same as leaving it out.");
                    continue;
                }

                References(section.Implements, implements, interfaceIds, "interface rule", issues);
            }
        }

        private static void Objects(
            IReadOnlyList<PlcTypeObject> objects,
            string path,
            string[] allowedTypes,
            HashSet<string> objectIds,
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

                References(entry.Implements, implements, objectIds, "object rule", issues);
            }
        }

        /// <summary>
        /// Every id must name a rule in the catalogue it is allowed to reach. The two
        /// catalogues never stand in for one another, so naming an object rule where an
        /// interface rule belongs reads as "no such rule" - which it is, from here.
        /// </summary>
        private static void References(
            IReadOnlyList<string> ids,
            string path,
            HashSet<string> known,
            string kind,
            Issues issues)
        {
            for (int r = 0; r < ids.Count; r++)
            {
                string reference = Issues.At(path, r);
                string ruleId = ids[r];

                if (!issues.Required(reference, ruleId)) continue;

                // Only worth reporting when the catalogue itself was readable; otherwise
                // every reference would be flagged for a problem already reported once.
                if (known.Count > 0 && !known.Contains(ruleId))
                    issues.Add(reference, "No " + kind + " with id '" + ruleId + "'.");
            }
        }
    }
}
