using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Core;
using Core.Config;
using Core.Imports;
using Core.Logging;

using Openness.Shared;

using Satellite.ImportObjects.Files;
using Satellite.ImportObjects.Overwrite;

using UI.Shared;

namespace Satellite.ImportObjects
{
    /// <summary>
    /// Imports the files the operator picks into the folder they right-clicked in TIA Portal.
    ///
    /// **It opens before the attach finishes**, in a corner and small, the core updater's lesson
    /// twice over: a window that is not there yet reads as an application that failed to start,
    /// and a large one covers the dialog in which TIA asks whether this may access the project.
    ///
    /// **Nothing here touches TIA Portal directly.** Every call goes through the worker, which
    /// owns the one thread the session belongs to, and every answer comes back on this one.
    ///
    /// The decisions it carries out are the maintainer's, 2026-09-28: the destination is the
    /// folder clicked; the operator picks a Windows folder and then files in it; each file is one
    /// unit; what already exists is asked about in one list and overwritten where it is; and what
    /// TIA would refuse, TIA refuses.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly List<FileRow> _rows = new List<FileRow>();

        private TiaWorker<IImportSession> _worker;
        private ImportPlace _place;
        private string _projectDirectory;
        private Log _log = Log.Nothing();

        /// <summary>Whether TIA said the folder that was clicked is still there.</summary>
        private bool _placeFound;

        /// <summary>
        /// Why there is nowhere to import into, and what the listed folder holds that is not
        /// offered - **kept apart**, so listing another folder cannot wipe the first.
        /// </summary>
        private string _placeProblem;

        private string _notOffered;

        private bool _busy;
        private bool _stopping;

        /// <summary>
        /// Whether this window holds its project's guard. One process, one window, one claim:
        /// `SingleInstance` keeps a single mutex, and a second claim would drop the first.
        /// </summary>
        private bool _claimed;

        public MainWindow()
        {
            InitializeComponent();

            Title = Product.Title + " - Import objects";
            StatusText.Text = "Starting…";

            Corner();
        }

        /// <summary>
        /// Puts the waiting window at the bottom right of the work area, out of the middle of the
        /// screen - where TIA centres the dialog in which it asks whether this may access the
        /// project. Over it, the two would wait for each other. The core updater measured it.
        /// </summary>
        private void Corner()
        {
            Rect work = SystemParameters.WorkArea;

            const double Margin = 24;

            Left = work.Right - Width - Margin;
            Top = work.Bottom - Height - Margin;
        }

        /// <summary>The worker is handed over once the application has built it.</summary>
        public void Uses(TiaWorker<IImportSession> worker)
        {
            _worker = worker;
        }

        /// <summary>Where this window records what it read and wrote. Older than the window.</summary>
        public void Records(Log log)
        {
            _log = log ?? Log.Nothing();
        }

        /// <summary>V20 or V21 beside the title: the two executables are otherwise identical.</summary>
        public void Badge(string tiaVersion)
        {
            Header.Badge = tiaVersion;
        }

        /// <summary>What the window shows while the attach is still running: the project it is for.</summary>
        public void ShowWaiting(ImportPlace place)
        {
            _place = place;

            string project = place == null ? null : TiaWanted.Of(place.Project, null).ProjectName;

            WaitingText.Text = string.IsNullOrWhiteSpace(project)
                ? "Looking for a running TIA Portal…"
                : string.Format(CultureInfo.CurrentCulture,
                    "Attaching to the TIA Portal that has '{0}' open…", project);

            StatusText.Text = string.Empty;
        }

        /// <summary>
        /// A window with nowhere to import into: started by hand, or handed arguments that do not
        /// name a folder. **It does not attach at all** - there is nothing to do once attached -
        /// and says how to get here properly.
        /// </summary>
        public void NotLaunched(string problem)
        {
            WaitingPanel.Visibility = Visibility.Collapsed;
            Grow(Sized.Choosing, 620, 320, 460, 240);

            ProblemPanel.Visibility = Visibility.Visible;
            ProblemText.Text = (string.IsNullOrWhiteSpace(problem) ? string.Empty : problem + "\n\n") +
                               "Open this from TIA Portal: right-click a folder of program blocks, PLC data types, " +
                               "PLC tags or technology objects, and choose Import objects. The folder you right-click " +
                               "is where the files go.";
            ConsideredText.Visibility = Visibility.Collapsed;
            OfferChoices(null);

            StatusText.Text = "Nowhere to import into.";
        }

        // ---- size and place --------------------------------------------------------------------

        /// <summary>
        /// How much of the screen this window has asked for so far. **Growing is one way**: once
        /// the list is up, the size is the operator's.
        /// </summary>
        private enum Sized
        {
            Waiting,
            Choosing,
            Attached
        }

        private Sized _sized = Sized.Waiting;

        /// <summary>
        /// Gives the window the size of what it is about to show. **Leaving the corner means
        /// centring** - the corner was never a place anybody chose - and every growth after that
        /// keeps the centre it has, so a window the operator moved stays where they put it.
        /// </summary>
        private void Grow(Sized wanted, double width, double height, double leastWidth, double leastHeight)
        {
            if (_sized == wanted || _sized == Sized.Attached) return;

            bool parked = _sized == Sized.Waiting;

            _sized = wanted;

            double x = Left + ActualWidth / 2;
            double y = Top + ActualHeight / 2;

            MinWidth = leastWidth;
            MinHeight = leastHeight;
            Width = width;
            Height = height;

            Rect work = SystemParameters.WorkArea;

            if (parked || double.IsNaN(x) || double.IsNaN(y))
            {
                Left = work.Left + (work.Width - width) / 2;
                Top = work.Top + (work.Height - height) / 2;
                return;
            }

            Left = Inside(x - width / 2, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenWidth, width);
            Top = Inside(y - height / 2, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenHeight, height);
        }

        private static double Inside(double wanted, double start, double length, double size)
        {
            double last = start + length - size;

            if (wanted < start) return start;

            return wanted > last ? Math.Max(start, last) : wanted;
        }

        // ---- attaching -------------------------------------------------------------------------

        /// <summary>The answer to the attach, whichever it is.</summary>
        public void Arrived(TiaAttachment attachment)
        {
            WaitingPanel.Visibility = Visibility.Collapsed;

            if (attachment == null)
            {
                _log.Error("attach - nothing came back");
                Refused(TiaAttachment.Failed("Nothing came back from the attach."));
                return;
            }

            if (!attachment.Attached)
            {
                _log.Warn("attach refused - " + attachment.Problem);
                Refused(attachment);
                return;
            }

            if (!Claimed(attachment)) return;

            Grow(Sized.Attached, 1000, 660, 740, 460);

            _projectDirectory = attachment.ProjectDirectory;
            _log.About(attachment.ProjectName, attachment.ProjectDirectory);

            ProblemPanel.Visibility = Visibility.Collapsed;
            AttachedPanel.Visibility = Visibility.Visible;

            Header.Subtitle = string.IsNullOrWhiteSpace(attachment.ProjectName)
                ? "(no project open)"
                : attachment.ProjectName;

            DestinationText.Text = _place == null ? string.Empty : _place.ToString();
            DestinationText.ToolTip = DestinationText.Text;

            FolderBox.Text = DefaultFolder();
            List(FolderBox.Text);

            Check();
        }

        /// <summary>
        /// **One window per project**: the core updater's guard, for the same reason - two windows
        /// writing into one project would each ask what exists and both overwrite it. Taken after
        /// the attach, the first moment the project is certain. A project never saved takes none,
        /// having no folder to key it on; nothing here writes into that folder anyway.
        /// </summary>
        private bool Claimed(TiaAttachment attachment)
        {
            if (_claimed || !attachment.HasProjectFolder) return true;

            if (SingleInstance.Claim("ImportObjects." + SingleInstance.PathKey(attachment.ProjectDirectory)))
            {
                _claimed = true;
                return true;
            }

            _log.Info("another import window has this project - giving way to it");
            Close();
            return false;
        }

        /// <summary>
        /// Asks TIA whether the folder that was clicked is still there. **Until it says yes,
        /// Import stays off**: a folder renamed or deleted since the click is somewhere nothing
        /// can go, and finding that out after the first file would be one refusal too late.
        /// </summary>
        private void Check()
        {
            if (_worker == null || _place == null) return;

            StatusText.Text = "Looking for the folder in TIA Portal…";

            _worker.Post(
                session => session.Check(_place),
                problem =>
                {
                    _placeFound = problem == null;

                    if (_placeFound)
                    {
                        _log.Info("importing into " + _place);
                        StatusText.Text = Counted();
                    }
                    else
                    {
                        _log.Warn("the folder is not there - " + problem);
                        StatusText.Text = "Nowhere to import into.";
                    }

                    _placeProblem = _placeFound ? null : problem + " Right-click the folder in TIA Portal again.";
                    Problems();
                    Ready();
                },
                exception =>
                {
                    _log.Failed("looking for the folder", exception);
                    _placeFound = false;
                    _placeProblem = "The folder could not be looked for: " + exception.Message;
                    Problems();
                    StatusText.Text = "Failed.";
                    Ready();
                });
        }

        // ---- the files ----------------------------------------------------------------------------

        /// <summary>
        /// Where the chooser starts: the project's own exports when there are any - which is where
        /// this framework writes objects out - and the project folder otherwise (the maintainer's
        /// choice, 2026-09-28). A project never saved has neither.
        /// </summary>
        private string DefaultFolder()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_projectDirectory))
                {
                    string exports = ConfigPaths.ExportsFor(_projectDirectory);

                    if (Directory.Exists(exports)) return exports;
                    if (Directory.Exists(_projectDirectory)) return _projectDirectory;
                }
            }
            catch (Exception)
            {
                // A path that will not resolve is simply not a starting point.
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>
        /// The importable files of one folder - **that folder only, not the ones below it**
        /// (the maintainer's decision) - in the order they would go in.
        ///
        /// **A `.s7res` is not a row of its own**: TIA reads it beside its `.s7dcl` whatever is
        /// ticked, so the `.s7dcl`'s row says it goes with it, and one with no `.s7dcl` is named
        /// under the list as not offered.
        /// </summary>
        private void List(string folder)
        {
            _rows.Clear();
            FilesList.ItemsSource = null;
            _notOffered = null;
            Problems();

            folder = (folder ?? string.Empty).Trim();

            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                StatusText.Text = folder.Length == 0 ? "Choose the folder the files are in." : "That folder is not there.";
                Ready();
                return;
            }

            List<string> paths;

            try
            {
                paths = Directory.GetFiles(folder)
                    .Where(p => ImportFiles.Extensions.Contains((Path.GetExtension(p) ?? string.Empty).ToLowerInvariant()))
                    .ToList();
            }
            catch (Exception exception)
            {
                _log.Failed("listing " + folder, exception);
                StatusText.Text = "The folder could not be read: " + exception.Message;
                Ready();
                return;
            }

            ImportSelection offered = ImportFiles.Choose(paths);

            foreach (ImportFile file in offered.Files) _rows.Add(new FileRow(file, Ink, Ticked));

            FilesList.ItemsSource = _rows;

            _notOffered = offered.Refused.Count == 0 ? null : "Not offered: " + string.Join(" ", offered.Refused);
            Problems();

            if (_placeFound) StatusText.Text = Counted();
            Ready();
        }

        /// <summary>A tick changed: the button, and the count - unless something else is being said.</summary>
        private void Ticked()
        {
            Ready();

            if (!_busy && _placeFound) StatusText.Text = Counted();
        }

        private string Counted()
        {
            if (_rows.Count == 0) return "Nothing in this folder can be imported.";

            int taken = _rows.Count(r => r.Take);

            return string.Format(CultureInfo.CurrentCulture,
                "{0} file(s) that can be imported, {1} ticked.", _rows.Count, taken);
        }

        private void FolderKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            e.Handled = true;
            List(FolderBox.Text);
        }

        private void BrowseClicked(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "The folder the files to import are in";
                dialog.SelectedPath = FolderBox.Text;

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                FolderBox.Text = dialog.SelectedPath;
                List(FolderBox.Text);
            }
        }

        private void AllClicked(object sender, RoutedEventArgs e) => TickAll(true);

        private void NoneClicked(object sender, RoutedEventArgs e) => TickAll(false);

        private void TickAll(bool take)
        {
            foreach (FileRow one in _rows) one.Take = take;
        }

        // ---- importing ----------------------------------------------------------------------------

        /// <summary>
        /// Reads the ticked files again - they may have changed since the folder was listed - asks
        /// TIA what their names already are, asks the operator about those, and then imports one
        /// file at a time.
        /// </summary>
        private void ImportClicked(object sender, RoutedEventArgs e)
        {
            if (_worker == null || _place == null || _busy) return;

            List<FileRow> picked = _rows.Where(r => r.Take).ToList();
            if (picked.Count == 0) return;

            foreach (FileRow one in _rows) one.Result = null;

            ImportSelection selection = ImportFiles.Choose(picked.Select(r => r.File.Path));
            Dictionary<string, FileRow> byPath = picked.ToDictionary(r => r.File.Path, StringComparer.OrdinalIgnoreCase);

            foreach (string refused in selection.Refused) _log.Warn("not imported - " + refused);

            List<string> names = selection.Files
                .SelectMany(f => f.Objects.Select(o => o.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _log.Info("import into " + _place + " from " + FolderBox.Text.Trim() + " - " + selection.Files.Count + " file(s)");

            Working(true);
            StatusText.Text = "Asking TIA Portal what is already there…";

            _worker.Post(
                session => session.Existing(_place, names),
                existing => Asked(selection, byPath, existing ?? new ExistingObject[0]),
                exception =>
                {
                    _log.Failed("asking what is already there", exception);
                    Working(false);
                    StatusText.Text = "Nothing was imported: " + exception.Message;
                });
        }

        /// <summary>
        /// What exists is known: every file that would overwrite something is asked about in one
        /// list, and the run is what is left.
        /// </summary>
        private void Asked(ImportSelection selection, Dictionary<string, FileRow> byPath, IReadOnlyList<ExistingObject> existing)
        {
            List<KeyValuePair<ImportFile, IReadOnlyList<ExistingObject>>> meeting =
                new List<KeyValuePair<ImportFile, IReadOnlyList<ExistingObject>>>();

            foreach (ImportFile file in selection.Files)
            {
                IReadOnlyList<ExistingObject> meets = Meets(file, existing);
                if (meets.Count > 0) meeting.Add(new KeyValuePair<ImportFile, IReadOnlyList<ExistingObject>>(file, meets));
            }

            ISet<ImportFile> yes = new HashSet<ImportFile>();

            if (meeting.Count > 0)
            {
                yes = OverwriteWindow.Ask(this, meeting);

                if (yes == null)
                {
                    _log.Info("import cancelled at the overwrite question - nothing was written");
                    Working(false);
                    StatusText.Text = "Nothing was imported.";
                    return;
                }
            }

            List<KeyValuePair<FileRow, FileToImport>> queue = new List<KeyValuePair<FileRow, FileToImport>>();

            foreach (ImportFile file in selection.Files)
            {
                FileRow row;
                if (!byPath.TryGetValue(file.Path, out row)) continue;

                IReadOnlyList<ExistingObject> meets = Meets(file, existing);

                if (meets.Count > 0 && !yes.Contains(file))
                {
                    // Said no to: nothing is asked of TIA, and the row says so.
                    Record(row, FileImport.Left(file));
                    continue;
                }

                queue.Add(new KeyValuePair<FileRow, FileToImport>(row, new FileToImport(file, meets)));
            }

            _stopping = false;
            Run(queue, 0);
        }

        private static IReadOnlyList<ExistingObject> Meets(ImportFile file, IReadOnlyList<ExistingObject> existing)
        {
            HashSet<string> names = new HashSet<string>(file.Objects.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);

            return existing.Where(x => x != null && names.Contains(x.Name)).ToList();
        }

        /// <summary>
        /// One file, then the next - so the status line can say where the run is, and Stop can act
        /// between two files. **A file TIA refuses does not end the run**: nothing is rolled back,
        /// and one refusal is one row, not the whole import.
        /// </summary>
        private void Run(List<KeyValuePair<FileRow, FileToImport>> queue, int next)
        {
            if (_stopping || next >= queue.Count)
            {
                Finish(queue.Count - next);
                return;
            }

            KeyValuePair<FileRow, FileToImport> one = queue[next];

            StatusText.Text = string.Format(CultureInfo.CurrentCulture,
                "Importing {0} of {1} - {2}…", next + 1, queue.Count, one.Key.FileName);

            _worker.Post(
                session => session.Import(_place, one.Value),
                result =>
                {
                    Record(one.Key, result ?? FileImport.Refused(one.Value.File, "Nothing came back from TIA Portal."));
                    Run(queue, next + 1);
                },
                exception =>
                {
                    _log.Failed("importing " + one.Key.FileName, exception);
                    Record(one.Key, FileImport.Refused(one.Value.File, exception.Message));
                    Run(queue, next + 1);
                });
        }

        private void Record(FileRow row, FileImport result)
        {
            row.Result = result;

            if (result.Outcome == ImportOutcome.Refused) _log.Warn(result.ToString());
            else _log.Info(result.ToString());
        }

        /// <summary>What the run came to, counted from the rows it wrote - one set of words for both.</summary>
        private void Finish(int notReached)
        {
            List<FileImport> results = _rows.Where(r => r.Result != null).Select(r => r.Result).ToList();

            List<string> parts = new List<string>();

            Add(parts, results.Count(r => r.Outcome == ImportOutcome.Imported), "imported");
            Add(parts, results.Count(r => r.Outcome == ImportOutcome.Overwritten), "overwritten");
            Add(parts, results.Count(r => r.Outcome == ImportOutcome.Left), "left as it is");
            Add(parts, results.Count(r => r.Outcome == ImportOutcome.Refused), "refused by TIA Portal");

            string said = parts.Count == 0 ? "Nothing was imported." : string.Join(", ", parts) + ".";

            if (notReached > 0)
                said += string.Format(CultureInfo.CurrentCulture, " Stopped with {0} not reached.", notReached);

            _log.Info("done - " + said);

            Working(false);
            StatusText.Text = said;
        }

        private static void Add(List<string> parts, int count, string words)
        {
            if (count > 0) parts.Add(count.ToString(CultureInfo.CurrentCulture) + " " + words);
        }

        private void StopClicked(object sender, RoutedEventArgs e)
        {
            _stopping = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "Stopping after this file…";
        }

        private void Working(bool busy)
        {
            _busy = busy;

            FolderBox.IsEnabled = !busy;
            BrowseButton.IsEnabled = !busy;
            AllButton.IsEnabled = !busy;
            NoneButton.IsEnabled = !busy;
            FilesList.IsEnabled = !busy;

            StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            StopButton.IsEnabled = busy;

            Ready();
        }

        /// <summary>
        /// Import is on only when there is somewhere to import into, something ticked, and nothing
        /// running - the one place that decides it, so no path can leave it on by accident.
        /// </summary>
        private void Ready()
        {
            ImportButton.IsEnabled = !_busy && _placeFound && _rows.Any(r => r.Take);
        }

        /// <summary>The line under the list: why there is nowhere to import into, then what is not offered.</summary>
        private void Problems()
        {
            string text = string.Join("\n\n", new[] { _placeProblem, _notOffered }.Where(t => !string.IsNullOrWhiteSpace(t)));

            ProblemsText.Text = text;
            ProblemsText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private Brush Ink(string key) => TryFindResource(key) as Brush;

        // ---- not attached ---------------------------------------------------------------------------

        /// <summary>
        /// Nothing was attached - and, when there is something to attach to, this is where the
        /// operator picks it rather than where the window gives up.
        /// </summary>
        private void Refused(TiaAttachment attachment)
        {
            bool choose = attachment.CanChoose;

            Grow(Sized.Choosing, 720, 520, 480, 320);

            ProblemPanel.Visibility = Visibility.Visible;
            ProblemText.Text = attachment.Problem;

            string considered = Considered(attachment.Considered, choose);

            ConsideredText.Text = considered;
            ConsideredText.Visibility = considered.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            OfferChoices(choose ? attachment.Considered : null);

            StatusText.Text = choose ? "Pick a TIA Portal." : "Not attached.";
        }

        /// <summary>The running instances to choose from, **nothing preselected**.</summary>
        private void OfferChoices(IReadOnlyList<RunningPortal> choices)
        {
            ChoicesList.Items.Clear();

            if (choices != null)
                foreach (RunningPortal one in choices) ChoicesList.Items.Add(one);

            Visibility shown = choices == null ? Visibility.Collapsed : Visibility.Visible;

            ChoicesList.Visibility = shown;
            ChoicesList.IsEnabled = true;
            AttachButton.Visibility = shown;
            AttachButton.IsEnabled = false;
        }

        private void ChoiceChanged(object sender, SelectionChangedEventArgs e)
        {
            AttachButton.IsEnabled = ChoicesList.SelectedItem is RunningPortal;
        }

        private void AttachClicked(object sender, RoutedEventArgs e)
        {
            RunningPortal chosen = ChoicesList.SelectedItem as RunningPortal;

            if (chosen == null || _worker == null) return;

            ChoicesList.IsEnabled = false;
            AttachButton.IsEnabled = false;
            StatusText.Text = "Attaching…";

            _worker.Post(
                session => session.AttachTo(chosen.Id),
                Arrived,
                exception =>
                {
                    _log.Failed("attaching to process " + chosen.Id, exception);
                    ChoicesList.IsEnabled = true;
                    AttachButton.IsEnabled = true;
                    StatusText.Text = exception.Message;
                });
        }

        /// <summary>
        /// What was running, so "none" and "several, and not that one" read apart. **Null means
        /// nothing ever looked**, and saying "none was running" there would be a claim never checked.
        /// </summary>
        private static string Considered(IReadOnlyList<RunningPortal> considered, bool choosing)
        {
            if (considered == null) return string.Empty;

            if (considered.Count == 0)
                return "No TIA Portal was running when this window opened.";

            string heading = considered.Count == 1
                ? "One TIA Portal was running:"
                : string.Format(CultureInfo.CurrentCulture, "{0} TIA Portals were running:", considered.Count);

            if (choosing) return heading;

            return heading + Environment.NewLine + "    " +
                   string.Join(Environment.NewLine + "    ", considered);
        }
    }
}
