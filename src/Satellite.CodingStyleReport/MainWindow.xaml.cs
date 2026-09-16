using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using Core;
using Core.Checks;
using Core.Config;

using Satellite.CodingStyleReport.Export;
using Satellite.CodingStyleReport.Report;

namespace Satellite.CodingStyleReport
{
    public partial class MainWindow : Window
    {
        private const string AllKinds = "All kinds";

        /// <summary>The waiting bar's track, in device-independent pixels: it is what the pulse slides along.</summary>
        private const double WaitingBarWidth = 320;

        private readonly List<ReportLine> _lines = new List<ReportLine>();
        private ICollectionView _view;

        /// <summary>The report on screen, kept whole for export whatever the filters hide.</summary>
        private StyleReport _report;

        /// <summary>The workbook the report came from, when it was imported rather than handed over.</summary>
        private string _importedFrom;

        /// <param name="handedOver">
        /// Whether TIA Portal sent something. **Only a window that received nothing offers
        /// Import**: one opened from the menu shows that run's report, and loading another
        /// workbook over it would leave a window that says which selection it checked showing a
        /// different one.
        /// </param>
        public MainWindow(StyleReport report, bool handedOver, string problem)
        {
            InitializeComponent();

            // In the title bar, so it reaches every screenshot a tester sends without them
            // having to go and find it.
            Title = "Coding style report " + Product.Version;

            ImportButton.Visibility = handedOver ? Visibility.Collapsed : Visibility.Visible;

            if (report == null)
            {
                ShowEmpty(handedOver, problem);
                return;
            }

            ShowReport(report);
            ShowCount();
        }

        /// <summary>
        /// The window TIA Portal opened before it started checking. It says what is being
        /// worked on until the report arrives on standard input, which is minutes away on a
        /// whole PLC - and a window that only appeared at the end was being read as a failure.
        /// </summary>
        public MainWindow(CheckingNotice notice)
        {
            InitializeComponent();

            Title = "Coding style report " + Product.Version;

            // Launched by TIA, so it is that run's window: importing another report over it
            // would contradict the header it is about to get.
            ImportButton.Visibility = Visibility.Collapsed;

            ShowWaiting(notice);
        }

        /// <summary>
        /// The end of the wait, on the UI thread: the report, a report that could not be read,
        /// or nothing at all - which is the Add-In saying the check did not finish.
        /// </summary>
        public void Arrived(StyleReport report, bool arrived, string problem)
        {
            StopWaiting();

            if (report != null)
            {
                ShowReport(report);
                ShowCount();
                return;
            }

            ShowEmpty(
                arrived
                    ? "The report handed over by TIA Portal could not be read."
                    : "The check did not finish. TIA Portal closed without sending a report - it was stopped, or it ran into something it could not read.",
                problem);
        }

        /// <summary>Puts a report on screen, replacing whatever was there - the filters start over with it.</summary>
        private void ShowReport(StyleReport report)
        {
            _report = report;

            // The view is dropped first: resetting the chips and the kind below fires their
            // handlers, which must not refresh a view over the previous report's rows.
            _view = null;
            _lines.Clear();

            // First rule of an id wins: the report writes each once, and a hand-made file that
            // repeats one should not stop the window opening.
            Dictionary<string, ReportRule> rules = new Dictionary<string, ReportRule>(StringComparer.Ordinal);
            foreach (ReportRule rule in report.Rules)
            {
                if (rule?.Id != null && !rules.ContainsKey(rule.Id)) rules.Add(rule.Id, rule);
            }

            _lines.AddRange(report.Rows.Where(row => row != null).Select(row => new ReportLine(row, rules)));

            Header.Subtitle = Describe(report, _importedFrom);
            SummaryLine.Text = Summarise(report.Rows);

            FailedCount.Text = Chip("Failed", CheckOutcome.Failed);
            NotConfiguredCount.Text = Chip("Not configured", CheckOutcome.NotConfigured);
            SkippedCount.Text = Chip("Skipped", CheckOutcome.Skipped);
            PassedCount.Text = Chip("Passed", CheckOutcome.Passed);

            foreach (ToggleButton chip in new[] { FailedChip, NotConfiguredChip, SkippedChip, PassedChip })
                chip.IsChecked = true;
            SearchBox.Clear();

            KindBox.Items.Clear();
            KindBox.Items.Add(AllKinds);
            foreach (string kind in _lines.Select(line => line.Kind)
                                          .Where(kind => !string.IsNullOrEmpty(kind))
                                          .Distinct(StringComparer.Ordinal)
                                          .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase))
            {
                KindBox.Items.Add(kind);
            }
            KindBox.SelectedIndex = 0;

            // Straight into the project the check came from. An imported report whose project
            // is unknown exports beside the workbook it came from; with neither, the operator picks.
            FolderBox.Text = ConfigPaths.FolderFor(report.ProjectDirectory, ConfigPaths.Reports)
                             ?? (_importedFrom == null ? string.Empty : Path.GetDirectoryName(_importedFrom));

            RowList.ItemsSource = null;
            RowList.ItemsSource = _lines;
            _view = CollectionViewSource.GetDefaultView(_lines);
            _view.Filter = Accepts;
            _view.Refresh();

            RowList.Visibility = Visibility.Visible;
            FilterBar.Visibility = Visibility.Visible;
            ExportBar.Visibility = Visibility.Visible;
            SummaryLine.Visibility = Visibility.Visible;
            EmptyNote.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Two situations, two sentences: nothing was sent, or something was and it could not
        /// be read. Saying the first when the second happened sends the operator back to TIA
        /// Portal to repeat what already failed.
        /// </summary>
        private void ShowEmpty(bool handedOver, string problem) =>
            ShowEmpty(
                handedOver
                    ? "The report handed over by TIA Portal could not be read."
                    : "No report yet. In TIA Portal, right-click the project, a PLC, a folder or some objects and choose Check coding style - or import a report exported earlier.",
                problem);

        private void ShowEmpty(string note, string problem)
        {
            RowList.Visibility = Visibility.Collapsed;
            FilterBar.Visibility = Visibility.Collapsed;
            ExportBar.Visibility = Visibility.Collapsed;
            SummaryLine.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Collapsed;
            EmptyNote.Visibility = Visibility.Visible;

            EmptyNote.Text = note;
            StatusLine.Text = problem ?? string.Empty;
        }

        // ------------------------------------------------------------------ waiting

        private void ShowWaiting(CheckingNotice notice)
        {
            RowList.Visibility = Visibility.Collapsed;
            FilterBar.Visibility = Visibility.Collapsed;
            ExportBar.Visibility = Visibility.Collapsed;
            SummaryLine.Visibility = Visibility.Collapsed;
            EmptyNote.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Visible;

            WaitingLine.Text = notice == null
                ? "Checking the coding style of the TIA Portal project..."
                : notice.Describe();

            // The header says the same, so the window is recognisable among several before
            // any of them has a report.
            Header.Subtitle = notice == null ? string.Empty : Describe(notice);
            StatusLine.Text = "Waiting for TIA Portal.";

            DoubleAnimation slide = new DoubleAnimation
            {
                From = 0,
                To = WaitingBarWidth - WaitingPulse.Width,
                Duration = new Duration(TimeSpan.FromSeconds(1.1)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            WaitingSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        }

        /// <summary>
        /// Stops the animation before the report replaces it. Left running it would keep a
        /// timer alive behind a panel nobody can see.
        /// </summary>
        private void StopWaiting()
        {
            WaitingSlide.BeginAnimation(TranslateTransform.XProperty, null);
            WaitingPanel.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ import

        private void OnImport(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import a coding-style report",
                Filter = "Coding-style report (*" + ReportFile.Extension + ")|*" + ReportFile.Extension,
                CheckFileExists = true
            };

            // Where a report is most likely to be: beside the last one this window touched, or in
            // the reports folder of the project on screen.
            string folder = FolderBox.Text.Trim();
            if (string.IsNullOrEmpty(folder) && _importedFrom != null) folder = Path.GetDirectoryName(_importedFrom);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) dialog.InitialDirectory = folder;

            if (dialog.ShowDialog(this) == true) Import(dialog.FileName);
        }

        /// <summary>
        /// Reads a workbook and shows it. A file that is not a report changes nothing on screen:
        /// the status line says why, and whatever was showing stays - an import that fails
        /// should not cost the report that was already open.
        /// </summary>
        public void Import(string path)
        {
            StyleReport report = ReportWorkbook.Read(path, out string problem);

            if (report == null)
            {
                StatusLine.Text = "'" + Path.GetFileName(path) + "' was not imported. " + problem;
                return;
            }

            _importedFrom = path;
            ShowReport(report);

            StatusLine.Text = string.Format(CultureInfo.CurrentCulture,
                "Imported {0} - {1} row(s).", path, report.Rows.Count);
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

        // ------------------------------------------------------------------ export

        private void OnExport(object sender, RoutedEventArgs e)
        {
            if (_report == null) return;

            string folder = FolderBox.Text.Trim();

            // Checked, and the folder created, before anything is written: .plc-framework\reports
            // does not exist until the first export makes it.
            string problem = ReportFile.ProblemWith(folder);
            if (problem != null)
            {
                StatusLine.Text = problem;
                return;
            }

            string path = null;
            try
            {
                path = ReportFile.Unique(folder, ReportFile.NameFor(_report.Project, CheckedAt()));
                ReportWorkbook.Write(_report, path, DateTime.UtcNow);

                StatusLine.Text = "Exported to " + path;
            }
            catch (Exception exception)
            {
                // A half-written workbook left behind would be opened later as a report. Nothing
                // on disk is better than something that looks like the whole of it.
                TryDelete(path);
                StatusLine.Text = "The report could not be exported: " + exception.Message;
            }
        }

        /// <summary>When the check ran, in local time, for the file name; now, if the report does not say.</summary>
        private DateTime CheckedAt()
        {
            return DateTime.TryParse(_report.GeneratedAtUtc, CultureInfo.InvariantCulture,
                                     DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                     out DateTime parsed)
                ? parsed.ToLocalTime()
                : DateTime.Now;
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        private void OnBrowseFolder(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Where the exported report is written";
                dialog.SelectedPath = FolderBox.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    FolderBox.Text = dialog.SelectedPath;
            }
        }

        /// <summary>
        /// Shows the folder in Explorer, without creating it: an "open" that silently makes a
        /// directory is a surprise, and before the first export there is nothing in it to see.
        /// </summary>
        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            string folder = FolderBox.Text.Trim();

            if (string.IsNullOrEmpty(folder))
            {
                StatusLine.Text = "Choose a destination folder first.";
                return;
            }

            if (!Directory.Exists(folder))
            {
                StatusLine.Text = "That folder does not exist yet - exporting creates it: " + folder;
                return;
            }

            try
            {
                // UseShellExecute is what opens a window rather than trying to run the directory.
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                StatusLine.Text = "The folder could not be opened: " + exception.Message;
            }
        }

        // ------------------------------------------------------------------ text

        private string Chip(string label, CheckOutcome outcome) =>
            string.Format(CultureInfo.CurrentCulture, "{0}  {1}", label, _lines.Count(line => line.Outcome == outcome));

        /// <summary>The header while there is no report: the same facts, in the same order.</summary>
        private static string Describe(CheckingNotice notice)
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(notice.Project)) parts.Add(notice.Project);
            if (!string.IsNullOrWhiteSpace(notice.Scope)) parts.Add(notice.Scope);
            parts.Add("checking...");

            return string.Join("  -  ", parts);
        }

        /// <param name="importedFrom">The workbook it was read from, named in the header so an imported report is never taken for a fresh run.</param>
        private static string Describe(StyleReport report, string importedFrom)
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(report.Project)) parts.Add(report.Project);
            if (!string.IsNullOrWhiteSpace(report.Scope)) parts.Add(report.Scope);
            if (!string.IsNullOrWhiteSpace(report.GeneratedAtUtc)) parts.Add(Local(report.GeneratedAtUtc));
            if (importedFrom != null) parts.Add("imported from " + Path.GetFileName(importedFrom));

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
