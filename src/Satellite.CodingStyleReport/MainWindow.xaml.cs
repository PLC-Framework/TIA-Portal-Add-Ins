using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

using Core;
using Core.Checks;

using Satellite.CodingStyleReport.Report;

namespace Satellite.CodingStyleReport
{
    public partial class MainWindow : Window
    {
        private const string AllKinds = "All kinds";

        private readonly List<ReportLine> _lines = new List<ReportLine>();
        private ICollectionView _view;

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

            // First rule of an id wins: the report writes each once, and a hand-made file that
            // repeats one should not stop the window opening.
            Dictionary<string, ReportRule> rules = new Dictionary<string, ReportRule>(StringComparer.Ordinal);
            foreach (ReportRule rule in report.Rules)
            {
                if (rule?.Id != null && !rules.ContainsKey(rule.Id)) rules.Add(rule.Id, rule);
            }

            _lines.AddRange(report.Rows.Where(row => row != null).Select(row => new ReportLine(row, rules)));

            ProjectLine.Text = Describe(report);
            SummaryLine.Text = Summarise(report.Rows);

            FailedCount.Text = Chip("Failed", CheckOutcome.Failed);
            NotConfiguredCount.Text = Chip("Not configured", CheckOutcome.NotConfigured);
            SkippedCount.Text = Chip("Skipped", CheckOutcome.Skipped);
            PassedCount.Text = Chip("Passed", CheckOutcome.Passed);

            KindBox.Items.Add(AllKinds);
            foreach (string kind in _lines.Select(line => line.Kind)
                                          .Where(kind => !string.IsNullOrEmpty(kind))
                                          .Distinct(StringComparer.Ordinal)
                                          .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase))
            {
                KindBox.Items.Add(kind);
            }
            KindBox.SelectedIndex = 0;

            RowList.ItemsSource = _lines;
            _view = CollectionViewSource.GetDefaultView(_lines);
            _view.Filter = Accepts;

            ShowCount();
        }

        private void ShowEmpty(bool handedOver, string problem)
        {
            RowList.Visibility = Visibility.Collapsed;
            FilterBar.Visibility = Visibility.Collapsed;
            SummaryLine.Visibility = Visibility.Collapsed;
            EmptyNote.Visibility = Visibility.Visible;

            // Two situations, two sentences: nothing was sent, or something was and it could
            // not be read. Saying the first when the second happened sends the operator back
            // to TIA Portal to repeat what already failed.
            EmptyNote.Text = handedOver
                ? "The report handed over by TIA Portal could not be read."
                : "No report yet. In TIA Portal, right-click the project, a PLC, a folder or some objects and choose Check coding style.";

            StatusLine.Text = problem ?? string.Empty;
        }

        // ------------------------------------------------------------------ filtering

        /// <summary>
        /// Whether a row passes the three filters. Each row stands on its own: a failing member
        /// shows without its object, whose name its path already carries - pulling the object
        /// row in as context would put a passed row on screen under a filter that hides passes.
        /// </summary>
        private bool Accepts(object item)
        {
            ReportLine line = item as ReportLine;
            if (line == null) return false;

            if (!OutcomeShown(line.Outcome)) return false;

            string kind = KindBox.SelectedItem as string;
            if (kind != null && kind != AllKinds && !string.Equals(line.Kind, kind, StringComparison.Ordinal))
                return false;

            string search = SearchBox.Text.Trim();
            return search.Length == 0 ||
                   line.SearchText.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A row whose outcome this window does not know is always shown: hiding what cannot be classified hides it for good.</summary>
        private bool OutcomeShown(CheckOutcome? outcome)
        {
            switch (outcome)
            {
                case CheckOutcome.Failed: return FailedChip.IsChecked == true;
                case CheckOutcome.NotConfigured: return NotConfiguredChip.IsChecked == true;
                case CheckOutcome.Skipped: return SkippedChip.IsChecked == true;
                case CheckOutcome.Passed: return PassedChip.IsChecked == true;
                default: return true;
            }
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            // Checked fires while InitializeComponent is still setting IsChecked, before the view exists.
            if (_view == null) return;

            _view.Refresh();
            ShowCount();
        }

        private void ShowCount()
        {
            int shown = _view?.Cast<object>().Count() ?? 0;

            StatusLine.Text = _lines.Count == 0
                ? "The report arrived with no rows."
                : shown == 0
                    ? "No row matches the filters."
                    : shown == _lines.Count
                        ? string.Format(CultureInfo.CurrentCulture, "{0} row(s).", _lines.Count)
                        : string.Format(CultureInfo.CurrentCulture, "Showing {0} of {1} rows.", shown, _lines.Count);
        }

        private void OnWindowKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && FilterBar.IsVisible)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || SearchBox.Text.Length == 0) return;

            SearchBox.Clear();
            e.Handled = true;
        }

        // ------------------------------------------------------------------ text

        private string Chip(string label, CheckOutcome outcome) =>
            string.Format(CultureInfo.CurrentCulture, "{0}  {1}", label, _lines.Count(line => line.Outcome == outcome));

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

            return string.Format(CultureInfo.CurrentCulture,
                "{0} object(s) checked, {1} failed      {2} member(s) checked, {3} failed",
                objects.Count, objects.Count(row => row.OutcomeValue == CheckOutcome.Failed),
                members.Count, members.Count(row => row.OutcomeValue == CheckOutcome.Failed));
        }
    }
}
