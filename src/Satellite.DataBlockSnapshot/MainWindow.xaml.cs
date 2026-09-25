using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Core;
using Core.Config;
using Core.Logging;

using S7PlcWebserverApi;

using Satellite.DataBlockSnapshot.Capture;
using Satellite.DataBlockSnapshot.Credentials;
using Satellite.DataBlockSnapshot.Export;
using Satellite.DataBlockSnapshot.Handoff;

namespace Satellite.DataBlockSnapshot
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<DataBlockItem> _blocks =
            new ObservableCollection<DataBlockItem>();

        /// <summary>Kept because the credential store is keyed by project and CPU.</summary>
        private readonly SnapshotRequest _request;

        /// <summary>
        /// The application's log. **Never handed the password** - not the typed one, not the
        /// remembered one - and the address and the user are enough for a line to say which
        /// CPU it was about.
        /// </summary>
        private readonly Log _log;

        private CancellationTokenSource _cancellation;

        /// <summary>
        /// Whether a capture is running. Remove's enabled state depends on two things at
        /// once - this, and whether a row is selected - and only one of them arrives as an
        /// event, so the other has to be remembered.
        /// </summary>
        private bool _busy;

        public MainWindow(SnapshotRequest request) : this(request, null)
        {
        }

        public MainWindow(SnapshotRequest request, Log log)
        {
            InitializeComponent();

            // In the title bar, so it reaches every screenshot a tester sends
            // without them having to go and find it.
            Title = "Data block snapshot " + Product.Version;

            _request = request;
            _log = log ?? Log.Nothing();

            if (request.DataBlocks.Count > 0)
                _log.Info(request.DataBlocks.Count + " data blocks handed over - " + Named(request.DataBlocks));

            BlockList.ItemsSource = _blocks;

            foreach (string address in request.Addresses) AddressBox.Items.Add(address);
            if (AddressBox.Items.Count > 0) AddressBox.SelectedIndex = 0;

            foreach (string block in request.DataBlocks) _blocks.Add(new DataBlockItem(block));

            // Straight into the project it came from. Empty when the window was started by
            // hand, and then the operator picks.
            FolderBox.Text = ConfigPaths.ExportsFor(request.ProjectDirectory) ?? string.Empty;

            Header.Subtitle = Describe(request);

            StatusLine.Text = Recall() ? DescribeBlocks() + "  Credentials remembered." : DescribeBlocks();

            // Instead of the empty-list sentence, which would send the operator back to the
            // Add-In they have just used: this is the one case where that advice is wrong.
            if (request.Problem != null)
                StatusLine.Text = request.Problem + " Type the PLC and the blocks by hand, or try again from TIA Portal.";
        }

        /// <summary>
        /// What the status line says about the list itself.
        ///
        /// The empty case names **both** ways of filling it. It used to say only "start this
        /// from the Add-In", which stopped being true the moment blocks could be typed in —
        /// and a message that sends the operator back to TIA Portal for something the window
        /// in front of them can do is worse than no message.
        /// </summary>
        private string DescribeBlocks() =>
            _blocks.Count == 0
                ? "No data blocks yet. Type a name below, or start this from the Add-In with the blocks selected in the project tree."
                : string.Format(CultureInfo.CurrentCulture, "{0} data block(s) ready.", _blocks.Count);

        /// <summary>
        /// Fill in what was last used successfully against this CPU.
        ///
        /// The fields stay editable and show exactly what was loaded, so nothing is being
        /// done behind the operator's back - the window looks the same as if they had
        /// typed it. A capture session is an hour of these, and retyping a password per
        /// launch is the friction this removes.
        /// </summary>
        private bool Recall()
        {
            Credential stored = CredentialStore.Find(
                _request.ProjectDirectory, _request.PlcName, AddressBox.Text);

            if (stored == null)
            {
                _log.Info("no credentials remembered for " + Cpu());
                return false;
            }

            // The user and the address, which are what somebody asking "why did it log in as
            // that" needs; what came back as the password stays in the field it was put in.
            _log.Info("credentials remembered for " + Cpu() + " - user " + stored.User +
                      (string.IsNullOrWhiteSpace(stored.Address) ? string.Empty : ", last at " + stored.Address));

            UserBox.Text = stored.User ?? string.Empty;
            SetPassword(stored.Password);
            RememberBox.IsChecked = true;

            // The address that last worked, but only if the project still offers it. An
            // address the CPU no longer has is worse than the project's own first choice.
            if (!string.IsNullOrWhiteSpace(stored.Address) &&
                AddressBox.Items.Contains(stored.Address))
                AddressBox.SelectedItem = stored.Address;

            return true;
        }

        private static string Describe(SnapshotRequest request)
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(request.PlcName)) parts.Add(request.PlcName);
            if (!string.IsNullOrWhiteSpace(request.ProjectDirectory)) parts.Add(request.ProjectDirectory);

            return parts.Count == 0 ? "Started without a project" : string.Join("  -  ", parts);
        }

        /// <summary>
        /// The CPU a line is about, named the way the credential store keys it: the device name
        /// the project handed over, or the address when the window was started by hand.
        /// </summary>
        private string Cpu() =>
            string.IsNullOrWhiteSpace(_request.PlcName) ? AddressBox.Text.Trim() : _request.PlcName;

        /// <summary>
        /// Block names for a log line, the first twenty and a count of the rest. A selection of
        /// three hundred is one line either way, and the per-block lines that follow say the rest.
        /// </summary>
        private static string Named(IReadOnlyList<string> blocks)
        {
            const int Shown = 20;

            string named = string.Join(", ", blocks.Take(Shown));

            return blocks.Count > Shown ? named + " and " + (blocks.Count - Shown) + " more" : named;
        }

        /// <summary>
        /// Whichever of the twin fields is on screen holds the truth. Everything else asks
        /// through here, so no caller has to know the password is kept in two places.
        /// </summary>
        private string CurrentPassword() =>
            RevealButton.IsChecked == true ? PasswordPlain.Text : PasswordBox.Password;

        private void SetPassword(string password)
        {
            PasswordBox.Password = password ?? string.Empty;
            PasswordPlain.Text = password ?? string.Empty;
        }

        /// <summary>
        /// Swap which twin is showing. The text moves across first, then the focus and the
        /// caret - without that last part the operator is left typing into a field that is
        /// no longer there, which looks exactly like a dead keyboard.
        /// </summary>
        private void OnRevealChanged(object sender, RoutedEventArgs e)
        {
            bool reveal = RevealButton.IsChecked == true;

            if (reveal)
            {
                PasswordPlain.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordPlain.Visibility = Visibility.Visible;

                PasswordPlain.Focus();
                PasswordPlain.CaretIndex = PasswordPlain.Text.Length;
            }
            else
            {
                PasswordBox.Password = PasswordPlain.Text;
                PasswordPlain.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;

                PasswordBox.Focus();
            }
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

        /// <summary>
        /// Show the destination in Explorer.
        ///
        /// The folder is not created here, on purpose: an "open" that silently makes a
        /// directory is a surprise, and before the first capture there is nothing in it
        /// to look at anyway. Creating it is the capture's job, and it does that before
        /// reading rather than after.
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
                StatusLine.Text = "That folder does not exist yet: " + folder;
                return;
            }

            try
            {
                // UseShellExecute is what makes this open a window rather than try to
                // run the directory as a program.
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                _log.Warn("the folder could not be opened - " + folder + " - " + exception.Message);
                StatusLine.Text = "The folder could not be opened: " + exception.Message;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _log.Info("cancel asked - stopping after the current block");

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
                _log.Warn("capture refused - " + problem);
                StatusLine.Text = problem;
                return;
            }

            foreach (DataBlockItem block in chosen) block.Reset();

            CaptureSettings settings = new CaptureSettings
            {
                Address = AddressBox.Text.Trim(),
                User = UserBox.Text.Trim(),
                Password = CurrentPassword(),
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

            // Read here, on the dispatcher thread: the callback below runs on a worker,
            // where touching a control would throw.
            bool remember = RememberBox.IsChecked == true;
            string project = _request.ProjectDirectory;
            string plc = _request.PlcName;
            string cpu = Cpu();
            Log log = _log;

            // What the store did, now that it says. It runs on the worker's thread, so a failure
            // is kept here and put in the status line once the capture is over - the capture
            // itself is not affected by it, and saying so mid-read would be overwritten anyway.
            string unremembered = null;

            Action authenticated = () =>
            {
                string problem = remember
                    ? CredentialStore.Save(project, plc, settings.Address, settings.User, settings.Password)
                    : CredentialStore.Forget(project, plc, settings.Address);

                if (problem == null)
                {
                    log.Info(remember
                        ? "credentials remembered for " + cpu + " - user " + settings.User
                        : "credentials not remembered for " + cpu + ", and any kept before forgotten");
                    return;
                }

                unremembered = remember
                    ? "The credentials could not be remembered: " + problem
                    : "The credentials kept for this PLC could not be forgotten: " + problem;

                log.Warn(unremembered);
            };

            _log.Info(string.Format(
                CultureInfo.InvariantCulture,
                "capture - {0} data blocks from {1} as {2}, timeout {3:0.#} s, into {4}",
                settings.DataBlocks.Count, settings.Address, settings.User,
                settings.Timeout.TotalSeconds, settings.Folder));

            _cancellation = new CancellationTokenSource();
            Working(true);
            StatusLine.Text = "Connecting...";

            try
            {
                // PlcClient blocks and a capture runs for tens of seconds; on the
                // dispatcher thread that would freeze the window solid.
                await Task.Run(() =>
                    CaptureRunner.Run(settings, progress, _cancellation.Token, authenticated, log));

                StatusLine.Text = Summarise(chosen);

                _log.Info("capture done - " + StatusLine.Text);
            }
            catch (PlcAuthException exception)
            {
                // Nothing was stored, and that is the line worth having: the callback that
                // remembers credentials never ran, so a wrong password never reached disk.
                _log.Warn("login refused by " + settings.Address + " as " + settings.User + " - " +
                          exception.Message + " - nothing remembered, nothing written");
                StatusLine.Text = "The PLC rejected those credentials: " + exception.Message;
            }
            catch (PlcConnectionException exception)
            {
                _log.Warn("no connection to " + settings.Address + " - " + exception.Message);
                StatusLine.Text = exception.Message;
            }
            catch (Exception exception)
            {
                _log.Failed("capture from " + settings.Address, exception);
                StatusLine.Text = exception.Message;
            }
            finally
            {
                // Whatever the capture came to: a login that went through asked the store, and
                // what the store could not do is worth the same line either way.
                if (unremembered != null) StatusLine.Text += "  " + unremembered;

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

        private void OnAddBlock(object sender, RoutedEventArgs e) => AddTypedBlock();

        /// <summary>
        /// Enter adds, because the gesture this exists for is typing a handful of names in a
        /// row. Handled, or the key travels on and the window's default button fires a
        /// capture instead - which is the opposite of what was meant.
        /// </summary>
        private void OnNewBlockKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            AddTypedBlock();
            e.Handled = true;
        }

        private void AddTypedBlock()
        {
            string name = NewBlockBox.Text.Trim();
            if (name.Length == 0) return;

            // Case-insensitively: it is one block on the CPU either way, so a second row
            // would capture it twice and write two workbooks differing only by the " (2)"
            // that a filename collision adds. Refusing says more than silently allowing it.
            if (_blocks.Any(block => string.Equals(block.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                StatusLine.Text = name + " is already in the list.";
                NewBlockBox.SelectAll();
                NewBlockBox.Focus();
                return;
            }

            _blocks.Add(new DataBlockItem(name));

            NewBlockBox.Clear();
            NewBlockBox.Focus();

            StatusLine.Text = DescribeBlocks();
        }

        private void OnRemoveBlock(object sender, RoutedEventArgs e)
        {
            DataBlockItem selected = BlockList.SelectedItem as DataBlockItem;
            if (selected == null) return;

            int index = _blocks.IndexOf(selected);
            _blocks.Remove(selected);

            // Keep a neighbour selected, so removing several in a row is one click each
            // rather than a click to select and a click to remove.
            if (_blocks.Count > 0) BlockList.SelectedIndex = Math.Min(index, _blocks.Count - 1);

            StatusLine.Text = DescribeBlocks();
        }

        /// <summary>
        /// Remove names a row, so it stays disabled until there is one. A button that looks
        /// live and does nothing teaches the operator to distrust the others.
        /// </summary>
        private void OnBlockSelectionChanged(object sender, SelectionChangedEventArgs e) =>
            RemoveBlockButton.IsEnabled = !_busy && BlockList.SelectedItem != null;

        private void Working(bool busy)
        {
            _busy = busy;

            NewBlockBox.IsEnabled = !busy;
            AddBlockButton.IsEnabled = !busy;
            RemoveBlockButton.IsEnabled = !busy && BlockList.SelectedItem != null;

            CaptureButton.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
            AddressBox.IsEnabled = !busy;
            UserBox.IsEnabled = !busy;
            PasswordBox.IsEnabled = !busy;
            PasswordPlain.IsEnabled = !busy;
            RevealButton.IsEnabled = !busy;
            RememberBox.IsEnabled = !busy;
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
