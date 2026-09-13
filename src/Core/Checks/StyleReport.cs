using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

using Core.Config;

namespace Core.Checks
{
    /// <summary>
    /// A coding-style check as a document: what the Add-In hands the report window, and what
    /// that window will later export and import.
    ///
    /// **Named StyleReport, not CodingStyleReport, on purpose.** The window that reads it is
    /// Satellite.CodingStyleReport, and inside that namespace a type of the same name loses to
    /// the namespace: every use reads "is a namespace but is used like a type". The same trap
    /// that rules out a namespace called AddIn.Core.
    ///
    /// **One definition for both ends.** The Add-In writes it and the satellite reads it, and
    /// a second copy of the shape on each side is two things that drift apart with nothing
    /// failing - the satellite would simply show less than was sent.
    ///
    /// **Every type here is public, with public setters, and that is not style.** The Add-In
    /// serializes this inside TIA Portal's partial-trust sandbox, which refuses a data
    /// contract that is not visible - measured once already, on the snapshot handoff.
    ///
    /// **It carries the rules it mentions**, pattern and descriptions included. A report is
    /// read after the fact - exported, attached to a ticket, imported next week - and the
    /// configuration it was checked against may have changed by then or be on another
    /// machine. Resolving a rule id against today's config.json would explain an old result
    /// with rules that did not produce it.
    /// </summary>
    [DataContract]
    public sealed class StyleReport
    {
        /// <summary>
        /// The shape of this document. Raised when a change would make an older reader
        /// misread a newer report, so the reader can say so rather than show half of it.
        /// </summary>
        public const int CurrentFormat = 1;

        [DataMember(Name = "format", Order = 0)]
        public int Format { get; set; }

        /// <summary>ISO 8601 in UTC. A string, because the serializer's own date format is <c>\/Date(...)\/</c>.</summary>
        [DataMember(Name = "generatedAtUtc", Order = 1)]
        public string GeneratedAtUtc { get; set; }

        /// <summary>The framework version that produced it, so a report sent back names its build.</summary>
        [DataMember(Name = "framework", Order = 2)]
        public string Framework { get; set; }

        [DataMember(Name = "project", Order = 3)]
        public string Project { get; set; }

        [DataMember(Name = "projectDirectory", Order = 4)]
        public string ProjectDirectory { get; set; }

        /// <summary>What was selected when the check ran: "PLC", "2 block folders". Words, for a person.</summary>
        [DataMember(Name = "scope", Order = 5)]
        public string Scope { get; set; }

        /// <summary>Every rule a row names, in catalogue order: object rules, then interface rules.</summary>
        [DataMember(Name = "rules", Order = 6)]
        public List<ReportRule> Rules { get; set; }

        /// <summary>In the order the checker produced them: each object, then its members.</summary>
        [DataMember(Name = "rows", Order = 7)]
        public List<ReportRow> Rows { get; set; }

        /// <summary>
        /// Turns checker rows into a report. Pure: the caller supplies the clock, so the same
        /// input gives the same document.
        /// </summary>
        public static StyleReport Build(
            CodingStyle style,
            IEnumerable<CheckRow> rows,
            string project,
            string projectDirectory,
            string scope,
            DateTime generatedUtc)
        {
            List<ReportRow> reportRows = (rows ?? Enumerable.Empty<CheckRow>())
                .Where(row => row != null)
                .Select(ReportRow.From)
                .ToList();

            HashSet<string> mentioned = new HashSet<string>(
                reportRows.SelectMany(row => row.Matched.Concat(row.Suggestions)), StringComparer.Ordinal);

            List<ReportRule> rules = new List<ReportRule>();
            AddRules(rules, style?.Catalogue, ReportRule.ObjectCatalogue, mentioned);
            AddRules(rules, style?.InterfaceRules, ReportRule.InterfaceCatalogue, mentioned);

            return new StyleReport
            {
                Format = CurrentFormat,
                GeneratedAtUtc = generatedUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                Framework = Product.Version,
                Project = project,
                ProjectDirectory = projectDirectory,
                Scope = scope,
                Rules = rules,
                Rows = reportRows
            };
        }

        private static void AddRules(List<ReportRule> into, IEnumerable<Rule> catalogue, string name, HashSet<string> mentioned)
        {
            if (catalogue == null) return;

            foreach (Rule rule in catalogue)
            {
                if (rule?.Id == null || !mentioned.Contains(rule.Id)) continue;
                if (into.Any(existing => string.Equals(existing.Id, rule.Id, StringComparison.Ordinal))) continue;

                into.Add(new ReportRule
                {
                    Id = rule.Id,
                    Catalogue = name,
                    Regex = rule.Regex,
                    Descriptions = rule.Descriptions?.ToList() ?? new List<string>()
                });
            }
        }

        public string ToJson()
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StyleReport));

            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// Reads a report back. Null when the text is not one, with the reason in
        /// <paramref name="problem"/> - never an exception, because the caller is a window
        /// that has to open either way.
        /// </summary>
        public static StyleReport FromJson(string json, out string problem)
        {
            problem = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                problem = "The report is empty.";
                return null;
            }

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StyleReport));

                StyleReport report;
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    report = serializer.ReadObject(stream) as StyleReport;

                if (report == null)
                {
                    problem = "The report could not be read.";
                    return null;
                }

                if (report.Format > CurrentFormat)
                {
                    problem = "This report was written by a newer version of the framework (format " +
                              report.Format + "); this window reads format " + CurrentFormat + ".";
                    return null;
                }

                // The serializer skips constructors and leaves an absent list null.
                report.Rules = report.Rules ?? new List<ReportRule>();
                report.Rows = report.Rows ?? new List<ReportRow>();
                foreach (ReportRow row in report.Rows) row?.Normalise();

                return report;
            }
            catch (Exception exception)
            {
                problem = "The report could not be read: " + exception.Message;
                return null;
            }
        }
    }

    /// <summary>One rule as it stood when the check ran.</summary>
    [DataContract]
    public sealed class ReportRule
    {
        public const string ObjectCatalogue = "object";
        public const string InterfaceCatalogue = "interface";

        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        /// <summary><c>object</c> or <c>interface</c>: which catalogue the id came from.</summary>
        [DataMember(Name = "catalogue", Order = 1)]
        public string Catalogue { get; set; }

        [DataMember(Name = "regex", Order = 2)]
        public string Regex { get; set; }

        [DataMember(Name = "descriptions", Order = 3)]
        public List<string> Descriptions { get; set; }
    }

    /// <summary>
    /// A <see cref="CheckRow"/> in a form that survives serialization, partial trust and a
    /// spreadsheet: enums as their names, lists never null.
    /// </summary>
    [DataContract]
    public sealed class ReportRow
    {
        /// <summary><c>Object</c> or <c>Member</c>, the names of <see cref="RowScope"/>.</summary>
        [DataMember(Name = "scope", Order = 0)]
        public string Scope { get; set; }

        /// <summary><c>Passed</c>, <c>Failed</c>, <c>NotConfigured</c> or <c>Skipped</c>: the names of <see cref="CheckOutcome"/>.</summary>
        [DataMember(Name = "outcome", Order = 1)]
        public string Outcome { get; set; }

        [DataMember(Name = "kind", Order = 2)]
        public string Kind { get; set; }

        [DataMember(Name = "name", Order = 3)]
        public string Name { get; set; }

        [DataMember(Name = "path", Order = 4)]
        public string Path { get; set; }

        [DataMember(Name = "matched", Order = 5)]
        public List<string> Matched { get; set; }

        [DataMember(Name = "suggestions", Order = 6)]
        public List<string> Suggestions { get; set; }

        [DataMember(Name = "note", Order = 7)]
        public string Note { get; set; }

        /// <summary>The outcome as the enum, or null when the text names none - a report from elsewhere.</summary>
        public CheckOutcome? OutcomeValue =>
            Enum.TryParse(Outcome, false, out CheckOutcome value) ? value : (CheckOutcome?)null;

        public bool IsMember => string.Equals(Scope, RowScope.Member.ToString(), StringComparison.Ordinal);

        internal static ReportRow From(CheckRow row) => new ReportRow
        {
            Scope = row.Scope.ToString(),
            Outcome = row.Outcome.ToString(),
            Kind = row.Kind,
            Name = row.Name,
            Path = row.Path,
            Matched = row.Matched.ToList(),
            Suggestions = row.Suggestions.ToList(),
            Note = row.Note
        };

        internal void Normalise()
        {
            Matched = Matched ?? new List<string>();
            Suggestions = Suggestions ?? new List<string>();
        }
    }
}
