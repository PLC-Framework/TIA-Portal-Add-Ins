using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using Core.Config;

using S7PlcWebserverApi;

using Satellite.DataBlockSnapshot.Capture;
using Satellite.DataBlockSnapshot.Export;
using Satellite.DataBlockSnapshot.Handoff;

namespace Satellite.DataBlockSnapshot
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<DataBlockItem> _blocks =
            new ObservableCollection<DataBlockItem>();

        private CancellationTokenSource _cancellation;

        public MainWindow(SnapshotRequest request)
        {
            InitializeComponent();

            BlockList.ItemsSource = _blocks;

            foreach (string address in request.Addresses) AddressBox.Items.Add(address);
            if (AddressBox.Items.Count > 0) AddressBox.SelectedIndex = 0;

            foreach (string block in request.DataBlocks) _blocks.Add(new DataBlockItem(block));

            // Straight into the project it came from. Empty when the window was started by
            // hand, and then the operator picks.
            FolderBox.Text = ConfigPaths.ExportsFor(request.ProjectDirectory) ?? string.Empty;

            ProjectLine.Text = Describe(request);

            StatusLine.Text = _blocks.Count == 0
                ? "No data blocks were handed over. Start this from the Add-In, with the blocks selected in the project tree."
                : string.Format(CultureInfo.CurrentCulture, "{0} data block(s) ready.", _blocks.Count);
        }

        private static string Describe(SnapshotRequest request)
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(request.PlcName)) parts.Add(request.PlcName);
            if (!string.IsNullOrWhiteSpace(request.ProjectDirectory)) parts.Add(request.ProjectDirectory);

            return parts.Count == 0 ? "Started without a project" : string.Join("  -  ", parts);
        }

        private void OnBrowseFolder(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog dialog =
                   new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Where the captures are written";
                dialog.SelectedPath = FolderBox.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    FolderBox.Text = dialog.SelectedPath;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _cancellation?.Cancel();
            CancelButton.IsEnabled = false;
            StatusLine.Text = "Cancelling after the current block...";
        }

        private async void OnCapture(object sender, RoutedEventArgs e)
        {
            List<DataBlockItem> chosen = _blocks.Where(block => block.Selected).ToList();

            string problem = Validate(chosen);
            if (problem != null)
            {
                StatusLine.Text = problem;
                return;
            }

            foreach (DataBlockItem block in chosen) block.Reset();

            CaptureSettings settings = new CaptureSettings
            {
                Address = AddressBox.Text.Trim(),
                User = UserBox.Text.Trim(),
                Password = PasswordBox.Password,
                Folder = FolderBox.Text.Trim(),
                Timeout = TimeSpan.FromSeconds(ParsedTimeout()),
                DataBlocks = chosen.Select(block => block.Name).ToList()
            };

            Dictionary<string, DataBlockItem> byName =
                chosen.ToDictionary(block => block.Name, StringComparer.OrdinalIgnoreCase);

            // Marshals to this thread by itself, which is the whole reason to report
            // progress this way rather than touching the controls from the worker.
            Progress<CaptureProgress> progress = new Progress<CaptureProgress>(update =>
            {
                DataBlockItem item;
                if (!byName.TryGetValue(update.DataBlock, out item)) return;

                item.Outcome = update.Outcome;
                item.Detail = update.Detail;
            });

            _cancellation = new CancellationTokenSource();
            Working(true);
            StatusLine.Text = "Connecting...";

            try
            {
                // PlcClient blocks and a capture runs for tens of seconds; on the
                // dispatcher thread that would freeze the window solid.
                await Task.Run(() => CaptureRunner.Run(settings, progress, _cancellation.Token));

                StatusLine.Text = Summarise(chosen);
            }
            catch (PlcAuthException exception)
            {
                StatusLine.Text = "The PLC rejected those credentials: " + exception.Message;
            }
            catch (PlcConnectionException exception)
            {
                StatusLine.Text = exception.Message;
            }
            catch (Exception exception)
            {
                StatusLine.Text = exception.Message;
            }
            finally
            {
                Working(false);
                _cancellation.Dispose();
                _cancellation = null;
            }
        }

        private string Validate(IReadOnlyList<DataBlockItem> chosen)
        {
            if (string.IsNullOrWhiteSpace(AddressBox.Text)) return "Enter the PLC address.";
            if (string.IsNullOrWhiteSpace(UserBox.Text)) return "Enter the web server user.";
            if (chosen.Count == 0) return "Select at least one data block.";
            if (ParsedTimeout() <= 0) return "The timeout must be a number of seconds greater than zero.";

            // Before the read, never after: discovering the folder is unusable once the
            // values have been read means reading them all over again.
            return ExportFile.ProblemWith(FolderBox.Text.Trim());
        }

        private double ParsedTimeout()
        {
            double seconds;
            return double.TryParse(TimeoutBox.Text.Trim(), NumberStyles.Any,
                                   CultureInfo.CurrentCulture, out seconds)
                ? seconds
                : 0;
        }

        private void Working(bool busy)
        {
            CaptureButton.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
            AddressBox.IsEnabled = !busy;
            UserBox.IsEnabled = !busy;
            PasswordBox.IsEnabled = !busy;
            TimeoutBox.IsEnabled = !busy;
            FolderBox.IsEnabled = !busy;
            BrowseButton.IsEnabled = !busy;
        }

        private static string Summarise(IReadOnlyList<DataBlockItem> chosen)
        {
            int written = chosen.Count(block => block.Outcome == CaptureOutcome.Written);
            int missing = chosen.Count(block => block.Outcome == CaptureOutcome.Missing);
            int failed = chosen.Count(block => block.Outcome == CaptureOutcome.Failed);
            int cancelled = chosen.Count(block => block.Outcome == CaptureOutcome.Cancelled);

            List<string> parts = new List<string> { written + " written" };

            if (missing > 0) parts.Add(missing + " not on this CPU");
            if (failed > 0) parts.Add(failed + " NOT written");
            if (cancelled > 0) parts.Add(cancelled + " cancelled");

            return string.Join(", ", parts) + ".";
        }
    }
}
