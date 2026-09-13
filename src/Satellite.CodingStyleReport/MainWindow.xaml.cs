using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

using Core;
using Core.Checks;

using Satellite.CodingStyleReport.Report;

namespace Satellite.CodingStyleReport
{
    public partial class MainWindow : Window
    {
        public MainWindow(StyleReport report, bool handedOver, string problem)
        {
            InitializeComponent();

            // In the title bar, so it reaches every screenshot a tester sends without them
            // having to go and find it.
            Title = "Coding style report " + Product.Version;

            if (report == null)
            {
                ShowEmpty(handedOver, problem);
                return;
            }

            ProjectLine.Text = Describe(report);
            SummaryLine.Text = Summarise(report.Rows);

            RowList.ItemsSource = report.Rows.Where(row => row != null).Select(row => new ReportLine(row)).ToList();

            StatusLine.Text = report.Rows.Count == 0
                ? "The report arrived with no rows."
                : string.Format(CultureInfo.CurrentCulture, "{0} row(s).", report.Rows.Count);
        }

        private void ShowEmpty(bool handedOver, string problem)
        {
            RowList.Visibility = Visibility.Collapsed;
            EmptyNote.Visibility = Visibility.Visible;

            // Two situations, two sentences: nothing was sent, or something was and it could
            // not be read. Saying the first when the second happened sends the operator back
            // to TIA Portal to repeat what already failed.
            EmptyNote.Text = handedOver
                ? "The report handed over by TIA Portal could not be read."
                : "No report yet. In TIA Portal, right-click the project, a PLC, a folder or some objects and choose Check coding style.";

            StatusLine.Text = problem ?? string.Empty;
        }

        private static string Describe(StyleReport report)
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(report.Project)) parts.Add(report.Project);
            if (!string.IsNullOrWhiteSpace(report.Scope)) parts.Add(report.Scope);
            if (!string.IsNullOrWhiteSpace(report.GeneratedAtUtc)) parts.Add(Local(report.GeneratedAtUtc));

            return string.Join("  -  ", parts);
        }

        /// <summary>The UTC stamp shown in local time, which is what the operator's clock says.</summary>
        private static string Local(string utc)
        {
            return DateTime.TryParse(utc, CultureInfo.InvariantCulture,
                                     DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                     out DateTime parsed)
                ? parsed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : utc;
        }

        private static string Summarise(IReadOnlyCollection<ReportRow> rows)
        {
            List<ReportRow> objects = rows.Where(row => row != null && !row.IsMember).ToList();
            List<ReportRow> members = rows.Where(row => row != null && row.IsMember).ToList();

            return "Objects: " + Counts(objects) + "      Members: " + Counts(members);
        }

        private static string Counts(IReadOnlyCollection<ReportRow> rows) =>
            string.Format(CultureInfo.CurrentCulture, "{0} passed, {1} failed, {2} not configured, {3} skipped",
                          Count(rows, CheckOutcome.Passed), Count(rows, CheckOutcome.Failed),
                          Count(rows, CheckOutcome.NotConfigured), Count(rows, CheckOutcome.Skipped));

        private static int Count(IEnumerable<ReportRow> rows, CheckOutcome outcome) =>
            rows.Count(row => row.OutcomeValue == outcome);
    }
}
