using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Core.Config;

namespace Core.Checks
{
    /// <summary>
    /// Holds a project's names against its <c>codingStyle</c>, and reports what it found.
    ///
    /// Pure: it is handed what the Add-In saw and returns rows. Nothing here opens a file,
    /// touches TIA or knows what a report looks like, which is what lets the whole thing be
    /// exercised with invented objects and no TIA installed.
    ///
    /// **A name passes if it matches ANY of the rules offered to it.** An FC offering
    /// function, safety_function, subroutine and safety_subroutine is offering four
    /// spellings; no name could satisfy two at once. The rules it did not match are reported
    /// as suggestions, because on a failing name they are what it was aiming at.
    /// </summary>
    public sealed class CodingStyleChecker
    {
        /// <summary>
        /// How long one name is allowed to be matched against one pattern.
        ///
        /// **This is not a performance setting; it protects TIA Portal.** The patterns come
        /// from a file somebody edits by hand, and a regular expression that backtracks
        /// catastrophically - easy to write by accident, hard to spot by reading - would
        /// spin the thread running it. That thread is inside TIA's own process. The config
        /// validator checks that a pattern *compiles*, which says nothing about whether it
        /// terminates; this is what covers the difference.
        ///
        /// Generous for the job: a block name is a few dozen characters, so a sane pattern
        /// finishes in microseconds and only a pathological one ever reaches this.
        /// </summary>
        public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(100);

        private readonly CodingStyle _style;
        private readonly Dictionary<string, Regex> _objectRules;
        private readonly Dictionary<string, Regex> _interfaceRules;
        private readonly HashSet<string> _broken;

        public CodingStyleChecker(CodingStyle style, TimeSpan? matchTimeout = null)
        {
            _style = style;

            TimeSpan timeout = matchTimeout ?? DefaultMatchTimeout;
            _broken = new HashSet<string>(StringComparer.Ordinal);

            // Compiled once here, which is the other half of a note in CodingStyleValidator:
            // it throws its Regex away because caching belongs to whoever actually matches
            // names against it. Several thousand objects against a dozen patterns is exactly
            // the shape that wants one compile each.
            _objectRules = Compile(style?.Catalogue, timeout);
            _interfaceRules = Compile(style?.InterfaceRules, timeout);
        }

        /// <summary>Rule ids whose pattern would not compile. They match nothing and say so.</summary>
        public IReadOnlyCollection<string> BrokenRules => _broken;

        private Dictionary<string, Regex> Compile(IEnumerable<Rule> rules, TimeSpan timeout)
        {
            Dictionary<string, Regex> compiled = new Dictionary<string, Regex>(StringComparer.Ordinal);
            if (rules == null) return compiled;

            foreach (Rule rule in rules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.Id) || compiled.ContainsKey(rule.Id))
                    continue;

                try
                {
                    compiled[rule.Id] = new Regex(rule.Regex ?? string.Empty, RegexOptions.None, timeout);
                }
                catch (ArgumentException)
                {
                    // The validator reports this properly, with a path into the document.
                    // Here it only has to not throw: a broken rule matches nothing, and every
                    // row that was offered it says so rather than quietly passing.
                    _broken.Add(rule.Id);
                }
            }

            return compiled;
        }

        /// <summary>
        /// Checks one object and whatever it was found to contain. Returns the object's own
        /// row first, then a row per member that a rule actually covers.
        /// </summary>
        public IReadOnlyList<CheckRow> Check(CheckedObject subject)
        {
            List<CheckRow> rows = new List<CheckRow>();
            if (subject == null) return rows;

            PlcTypeObject entry = EntryFor(subject.Family, subject.Type);

            if (entry?.Implements == null || entry.Implements.Count == 0)
            {
                rows.Add(new CheckRow(
                    RowScope.Object, CheckOutcome.NotConfigured,
                    subject.Plc, subject.Unit, subject.Name,
                    subject.Type, null, subject.Name, subject.Path,
                    note: "No rule for this type under codingStyle." + KeyOf(subject.Family) + "."));

                return rows;
            }

            Match(entry.Implements, subject.Name, _objectRules,
                  out List<string> matched, out List<string> missed, out string trouble);

            bool passed = matched.Count > 0;

            rows.Add(new CheckRow(
                RowScope.Object,
                trouble != null && !passed ? CheckOutcome.Skipped
                                           : passed ? CheckOutcome.Passed : CheckOutcome.Failed,
                subject.Plc, subject.Unit, subject.Name,
                subject.Type, null, subject.Name, subject.Path, matched, missed, trouble));

            bool unreadable = subject.MembersUnreadable != null;
            if (subject.Members.Count == 0 && !unreadable) return rows;

            if (!passed)
            {
                // No rule matched, so no interface is known. Checking against the union of
                // rules the name failed would report members against an expectation nobody
                // set, and bury the one finding that matters: the object's own name.
                rows.Add(new CheckRow(
                    RowScope.Member, CheckOutcome.Skipped,
                    subject.Plc, subject.Unit, subject.Name,
                    string.Empty, null, string.Empty, subject.Path,
                    note: "Interface not checked: the object's own name matched no rule, so " +
                          "which interface applies is unknown."));

                return rows;
            }

            Dictionary<string, List<string>> expected = ExpectedSections(matched);

            if (unreadable)
            {
                // Only worth a row when the matched rules expected something inside. An
                // object whose interface nobody configured has nothing to miss, and a row
                // saying otherwise would be noise the reader learns to skip.
                if (expected.Count > 0)
                {
                    rows.Add(new CheckRow(
                        RowScope.Member, CheckOutcome.Skipped,
                        subject.Plc, subject.Unit, subject.Name,
                        string.Empty, null, string.Empty, subject.Path,
                        note: "Interface not checked. " + subject.MembersUnreadable));
                }

                return rows;
            }

            foreach (CheckedMember member in subject.Members)
            {
                if (member == null || !expected.TryGetValue(member.Section ?? string.Empty, out List<string> ids))
                    continue;   // no rule covers this section, so there is nothing to report

                Match(ids, member.Name, _interfaceRules,
                      out List<string> hit, out List<string> gone, out string memberTrouble);

                bool ok = hit.Count > 0;

                rows.Add(new CheckRow(
                    RowScope.Member,
                    memberTrouble != null && !ok ? CheckOutcome.Skipped
                                                 : ok ? CheckOutcome.Passed : CheckOutcome.Failed,
                    subject.Plc, subject.Unit, subject.Name,
                    member.Section, member.Parent, member.Name, subject.Path, hit, gone, memberTrouble));
            }

            return rows;
        }

        /// <summary>
        /// The sections every matched rule expects, merged.
        ///
        /// **A union, and only where a name matches more than one rule.** The right answer to
        /// that is a pattern that excludes the other - which is why the reference
        /// configuration carries (?!seq[0-9]) and (?!_oc_) - but a checker cannot demand
        /// that of a file it did not write. Given two readings of a name, accepting what
        /// either would allow is the only choice that invents no rule of its own: taking the
        /// first would make the order of a list meaningful, and taking the intersection would
        /// reject a member that one perfectly good reading permits.
        /// </summary>
        private Dictionary<string, List<string>> ExpectedSections(IEnumerable<string> matchedRuleIds)
        {
            Dictionary<string, List<string>> expected =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);

            List<Rule> catalogue = _style?.Catalogue;
            if (catalogue == null) return expected;

            foreach (string id in matchedRuleIds)
            {
                Rule rule = catalogue.FirstOrDefault(
                    r => r != null && string.Equals(r.Id, id, StringComparison.Ordinal));

                if (rule?.Interface == null) continue;

                foreach (InterfaceSection section in rule.Interface)
                {
                    if (section?.Type == null || section.Implements == null) continue;

                    if (!expected.TryGetValue(section.Type, out List<string> ids))
                    {
                        ids = new List<string>();
                        expected[section.Type] = ids;
                    }

                    foreach (string ruleId in section.Implements)
                        if (ruleId != null && !ids.Contains(ruleId, StringComparer.Ordinal))
                            ids.Add(ruleId);
                }
            }

            return expected;
        }

        /// <summary>
        /// Splits the rules offered into those the name satisfies and those it does not.
        /// </summary>
        /// <param name="trouble">
        /// Set when a rule could not answer at all - it would not compile, or matching it
        /// ran out of time. Never silent: a rule that cannot be evaluated is not a rule that
        /// passed.
        /// </param>
        private void Match(
            IEnumerable<string> offered,
            string name,
            Dictionary<string, Regex> compiled,
            out List<string> matched,
            out List<string> missed,
            out string trouble)
        {
            matched = new List<string>();
            missed = new List<string>();

            List<string> unusable = new List<string>();
            List<string> timedOut = new List<string>();

            foreach (string id in offered)
            {
                if (id == null) continue;

                if (!compiled.TryGetValue(id, out Regex pattern))
                {
                    // Either the pattern does not compile, or the id names nothing at all -
                    // and the validator reports the second with a path, so here they are the
                    // same thing: a rule that cannot judge this name.
                    unusable.Add(id);
                    continue;
                }

                try
                {
                    if (pattern.IsMatch(name ?? string.Empty)) matched.Add(id);
                    else missed.Add(id);
                }
                catch (RegexMatchTimeoutException)
                {
                    timedOut.Add(id);
                }
            }

            List<string> notes = new List<string>();

            if (unusable.Count > 0)
                notes.Add("Unusable rule" + (unusable.Count > 1 ? "s" : "") + ": " + string.Join(", ", unusable) +
                          " - the pattern does not compile, or no rule has that id.");

            if (timedOut.Count > 0)
                notes.Add("Pattern timed out: " + string.Join(", ", timedOut) +
                          " - it takes too long on this name to be used.");

            trouble = notes.Count == 0 ? null : string.Join(" ", notes);
        }

        private PlcTypeObject EntryFor(ObjectFamily family, string type)
        {
            List<PlcTypeObject> list = ListFor(family);
            if (list == null || type == null) return null;

            return list.FirstOrDefault(
                e => e != null && string.Equals(e.Type, type, StringComparison.Ordinal));
        }

        private List<PlcTypeObject> ListFor(ObjectFamily family)
        {
            switch (family)
            {
                case ObjectFamily.Blocks: return _style?.Blocks;
                case ObjectFamily.TechnologyObjects: return _style?.TechnologyObjects;
                case ObjectFamily.TagTables: return _style?.TagTables;
                case ObjectFamily.Types: return _style?.Types;
                case ObjectFamily.AlarmTextLists: return _style?.AlarmTextLists;
                default: return null;
            }
        }

        private static string KeyOf(ObjectFamily family)
        {
            switch (family)
            {
                case ObjectFamily.Blocks: return "blocks";
                case ObjectFamily.TechnologyObjects: return "technologyObjects";
                case ObjectFamily.TagTables: return "tagTables";
                case ObjectFamily.Types: return "types";
                case ObjectFamily.AlarmTextLists: return "alarmTextLists";
                default: return "?";
            }
        }
    }
}
