using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Core;
using Core.Config.Validation;
using Core.Repo;

using Satellite.CoreUpdater.Compare;
using Satellite.CoreUpdater.Tia;

namespace Satellite.CoreUpdater
{
    /// <summary>
    /// Says which TIA Portal this window is attached to, lets the operator pick a PLC and a
    /// software unit, and writes what that scope holds into <c>repo\project.json</c>.
    ///
    /// **It opens before the attach finishes**, which is the lesson the coding-style report
    /// already paid for: work that takes a noticeable moment behind a window that is not
    /// there yet reads as an application that failed to start.
    ///
    /// **Nothing here touches TIA Portal directly.** Every call goes through
    /// <see cref="TiaWorker"/>, which owns the one thread the session belongs to, and every
    /// answer comes back on this one.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>How many children of `FilterPanel` are its headings rather than its rows.</summary>
        private const int HeadingCount = 2;

        private readonly List<KindRow> _rows = new List<KindRow>();
        private readonly List<CheckBox> _chips = new List<CheckBox>();
        private readonly List<CheckBox> _repoChips = new List<CheckBox>();
        private readonly List<Pick> _picks = new List<Pick>();

        private CoreComparison _compared;

        /// <summary>The comparison on screen, kept whole: a download needs its catalogue too.</summary>
        private Comparison _shown;

        /// <summary>The unit the compared map covers, which names the project tree's root.</summary>
        private string _scope;

        private TiaWorker _worker;
        private string _projectDirectory;
        private string _plc;
        private string _unit;
        private bool _filling;
        private bool _busy;

        /// <summary>
        /// Whether the status line is currently holding a complaint about the tick boxes, so
        /// that clearing it puts back "Ready." rather than leaving the complaint on screen
        /// beside a button that now works.
        /// </summary>
        private bool _complaining;

        /// <summary>
        /// Whether a whole column of tick boxes is being set at once, so the handler on each of
        /// them does not rebuild a tree that is about to be rebuilt anyway.
        /// </summary>
        private bool _settling;

        /// <summary>Whether the window has already done its one automatic map-and-compare.</summary>
        private bool _started;

        public MainWindow()
        {
            InitializeComponent();

            Title = Product.Title + " - Core updater";
            StatusText.Text = "Starting…";
        }

        /// <summary>The worker is handed over once the application has built it.</summary>
        public void Uses(TiaWorker worker)
        {
            _worker = worker;
        }

        /// <summary>
        /// Which TIA version this executable was built for, shown beside the title.
        ///
        /// **Because the two windows are otherwise identical**: one binary per version is
        /// what Openness forces, and an operator with both installed has no way to tell from
        /// looking which one they opened - or which one an Add-In opened for them.
        /// </summary>
        public void Badge(string tiaVersion)
        {
            Header.Badge = tiaVersion;
        }

        /// <summary>
        /// What the window shows while the attach is still running.
        ///
        /// **It names the project, not the process that launched this.** It used to say
        /// "Attaching to the TIA Portal that opened this window (process 12896)" - which named
        /// a number that turned out not to identify any TIA Portal at all, and that no operator
        /// could act on either. The project is both what decides the attach and what somebody
        /// recognises on screen.
        /// </summary>
        public void ShowWaiting(string project)
        {
            WaitingText.Text = string.IsNullOrWhiteSpace(project)
                ? "Looking for a running TIA Portal…"
                : string.Format(CultureInfo.CurrentCulture,
                    "Attaching to the TIA Portal that has '{0}' open…", project);

            StatusText.Text = string.Empty;
        }

        /// <summary>The answer to the attach, whichever of the three it is.</summary>
        public void Arrived(TiaAttachment attachment, string plc, string unit)
        {
            // Kept, because the operator may pick an instance out of the failure panel and
            // arrive back here - at which point this needs to know what to fill in again.
            _plc = plc;
            _unit = unit;

            WaitingPanel.Visibility = Visibility.Collapsed;

            if (attachment == null)
            {
                Refused(TiaAttachment.Failed("Nothing came back from the attach."));
                return;
            }

            if (!attachment.Attached)
            {
                Refused(attachment);
                return;
            }

            _projectDirectory = attachment.ProjectDirectory;

            // A second attempt succeeding has to clear the first one's refusal, or the window
            // shows a project and the reason it could not be reached at the same time.
            ProblemPanel.Visibility = Visibility.Collapsed;
            AttachedPanel.Visibility = Visibility.Visible;

            Header.Subtitle = string.IsNullOrWhiteSpace(attachment.ProjectName)
                ? "(no project open)"
                : attachment.ProjectName;

            // A project that was never saved is attached and still unusable here: the map is
            // written into the project's own folder, and there is no folder to write into.
            if (!attachment.HasProjectFolder)
            {
                ProjectSays("This project has not been saved, so it has no folder - and the map is " +
                     "written inside it. Save the project and open this window again.");
                MapButton.IsEnabled = false;
                StatusText.Text = "Attached, with nowhere to write.";
                return;
            }

            ProjectSays(attachment.ProjectDirectory);
            StatusText.Text = "Reading the PLCs…";

            Plcs(plc, unit);
        }

        /// <summary>
        /// One line under the project tree, with the whole of it on hover.
        ///
        /// **Each panel carries its own caption** (the maintainer's layout), which is what makes
        /// a line read as that tree's own rather than as one sentence trying to describe both.
        /// They are one line, so anything longer - a map's counts, kind by kind - is trimmed on
        /// screen and whole in the tooltip, which is what this framework already does to a cell
        /// it cannot fit.
        /// </summary>
        private void ProjectSays(string text)
        {
            ProjectStatus.Text = text ?? string.Empty;
            ProjectStatus.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>The same, under the core tree.</summary>
        private void RepositorySays(string text)
        {
            RepositoryStatus.Text = text ?? string.Empty;
            RepositoryStatus.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>
        /// What a run could not do, across the width of both panels.
        ///
        /// **Never folded into a caption.** A folder that would not read, an object that would
        /// not export: a map with holes that does not say where they are reads as a whole one,
        /// which is the single outcome this feature is shaped to avoid.
        /// </summary>
        private void Problems(string text)
        {
            ProblemsText.Text = text ?? string.Empty;
            ProblemsText.ToolTip = string.IsNullOrEmpty(text) ? null : text;
            ProblemsText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// The filters open from a button over each tree, because they are decided once per run
        /// and as three columns of tick boxes they were taking a third of the window away from
        /// the two trees somebody opened it to read.
        ///
        /// **`StaysOpen="False"` closes a popup on any click outside it - the button included -
        /// and that click would then arrive here and open it again.** <see cref="FilterPressed"/>
        /// swallows the press while a popup is open, so a second press closes it the way anybody
        /// would expect it to.
        /// </summary>
        private void ProjectFilterClicked(object sender, RoutedEventArgs e)
        {
            ProjectFilterPopup.IsOpen = true;
        }

        private void RepoFilterClicked(object sender, RoutedEventArgs e)
        {
            RepoFilterPopup.IsOpen = true;
        }

        private void FilterPressed(object sender, MouseButtonEventArgs e)
        {
            if (ProjectFilterPopup.IsOpen || RepoFilterPopup.IsOpen) e.Handled = true;
        }

        private void Plcs(string plc, string unit)
        {
            _worker.Post(
                session => session.Plcs(),
                found =>
                {
                    Fill(PlcBox, found, plc);

                    StatusText.Text = found.Count == 0
                        ? "This project holds no PLC."
                        : "Ready.";

                    // The unit list belongs to whichever PLC ended up selected, which
                    // PlcChanged is already the one place that knows.
                    Units(unit);
                },
                Failed);
        }

        private void PlcChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filling) return;

            Units(Places.GeneralProgram);
        }

        private void UnitChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filling) return;

            Survey();
        }

        private void Units(string unit)
        {
            string plc = PlcBox.SelectedItem as string;

            if (plc == null)
            {
                Fill(UnitBox, new string[0], null);

                // Still through Survey, which is the one place that clears the tick boxes and
                // settles the button. Returning here instead left a project with no PLC
                // showing an enabled Map button.
                Survey();
                return;
            }

            _worker.Post(
                session => session.Units(plc),
                found =>
                {
                    // The general program is always first: it is what the Add-In asks for,
                    // and a PLC without units still needs something in the list.
                    List<string> choices = new List<string> { Places.GeneralProgram };
                    choices.AddRange(found);

                    Fill(UnitBox, choices, unit);

                    // Filling suppresses the selection event, so the survey of whatever ended
                    // up selected is asked for here rather than left to fire by itself.
                    Survey();
                },
                Failed);
        }

        /// <summary>
        /// Counts what the chosen scope holds, and offers it as tick boxes.
        ///
        /// **Run for every scope the operator picks, before they ask for anything.** It reads
        /// no object - a kind and a language are typed properties - so it costs one pass over
        /// the tree, where the map costs one export per object in V17-V20. Paying that second
        /// for four hundred objects is what lets somebody map the thirty they wanted.
        /// </summary>
        private void Survey()
        {
            string plc = PlcBox.SelectedItem as string;
            string unit = UnitBox.SelectedItem as string;

            if (plc == null)
            {
                Offer(null);
                Ready();
                return;
            }

            Working(true);
            StatusText.Text = "Counting what " + plc + " holds…";

            _worker.Post(
                session => session.Survey(plc, unit),
                survey =>
                {
                    Offer(survey);
                    Working(false);

                    StatusText.Text = survey == null || survey.Total == 0
                        ? "Nothing to map in this scope."
                        : "Ready — " + survey.Total + " objects.";

                    Start();
                },
                exception =>
                {
                    Offer(null);
                    Working(false);
                    Failed(exception);
                });
        }

        /// <summary>
        /// What the window does by itself, once — compare, and map first when there is nothing
        /// to compare from (the maintainer asked for it, with both buttons kept).
        ///
        /// **Because the two trees are the window.** Opening onto two empty panels and a row of
        /// buttons makes somebody press one to see what they came to see, and the answer is the
        /// same every time. A project that has been mapped before is compared in the time it
        /// takes to copy a folder; one that has not is walked first.
        ///
        /// **It runs on the first survey and never again.** Changing the PLC or the unit is the
        /// operator steering, and re-mapping several hundred objects under them because they
        /// looked at another unit is the opposite of helpful.
        ///
        /// **The file is looked for rather than read.** Whether it parses, and whether its
        /// format is one this version knows, is the comparison's own question and it already
        /// answers it in a sentence; parsing a map of four thousand objects here, on the UI
        /// thread, to learn only that it is there would be paying twice.
        ///
        /// **The whole scope, because nothing has been ticked yet.** The survey has just put
        /// every box on, so the filter narrows nothing - which is the same "empty means
        /// everything" the map itself runs on.
        /// </summary>
        private void Start()
        {
            if (_started || _busy || string.IsNullOrWhiteSpace(_projectDirectory)) return;

            _started = true;

            string plc = PlcBox.SelectedItem as string;

            if (plc == null) return;

            if (Mapped()) CompareClicked(null, null);
            else Map(plc, UnitBox.SelectedItem as string, Chosen(), true);
        }

        private bool Mapped()
        {
            try
            {
                return File.Exists(RepoPaths.ProjectFor(_projectDirectory));
            }
            catch (Exception)
            {
                // A path this window cannot even ask about is one the comparison will report
                // properly; guessing "yes" here would only send it looking.
                return false;
            }
        }

        /// <summary>
        /// The tick boxes, built from the counts this very PLC gave back: one row per kind,
        /// and on that row the languages found inside it.
        ///
        /// **Everything starts ticked**, which is what the window did before there were any:
        /// somebody who ignores this panel gets the whole scope, and "empty means everything"
        /// stays true from the window down to the map.
        /// </summary>
        private void Offer(ProjectSurvey survey)
        {
            Clear();

            if (survey == null || survey.Total == 0)
            {
                FilterPanel.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (SurveyedKind kind in survey.Kinds) Add(kind);

            FilterPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Takes the rows off, leaving the two headings that live in the XAML.
        ///
        /// **Counted from the front rather than remembered**, so a heading added to that file
        /// later cannot be deleted here by a number that quietly stopped matching.
        /// </summary>
        private void Clear()
        {
            _rows.Clear();

            while (FilterPanel.Children.Count > HeadingCount)
                FilterPanel.Children.RemoveAt(FilterPanel.Children.Count - 1);

            while (FilterPanel.RowDefinitions.Count > 1)
                FilterPanel.RowDefinitions.RemoveAt(FilterPanel.RowDefinitions.Count - 1);
        }

        private void Add(SurveyedKind kind)
        {
            int row = FilterPanel.RowDefinitions.Count;

            FilterPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            KindRow built = new KindRow { Kind = Box(kind.Name, kind.ToString()) };

            built.Kind.Checked += KindChanged;
            built.Kind.Unchecked += KindChanged;

            Grid.SetRow(built.Kind, row);
            Grid.SetColumn(built.Kind, 0);
            FilterPanel.Children.Add(built.Kind);

            // A WrapPanel rather than a row of columns: a kind can hold six languages and this
            // window resizes from 560 upwards, where they have to drop to the next line rather
            // than be cut off.
            WrapPanel languages = new WrapPanel { Margin = new Thickness(18, 0, 0, 0) };

            foreach (Counted one in kind.Languages)
            {
                CheckBox box = Box(one.Name, one.ToString());

                box.Checked += TickChanged;
                box.Unchecked += TickChanged;

                built.Languages.Add(box);
                languages.Children.Add(box);
            }

            Grid.SetRow(languages, row);
            Grid.SetColumn(languages, 1);
            FilterPanel.Children.Add(languages);

            _rows.Add(built);
        }

        private CheckBox Box(string name, string label)
        {
            CheckBox box = new CheckBox
            {
                IsChecked = true,
                Tag = name,
                Margin = new Thickness(0, 5, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,

                // **A TextBlock, never a string in Content.** WPF reads an underscore as a
                // keyboard accelerator and hides it, so `F_DB` would render as `FDB` and
                // `Motion_DB` as `MotionDB` - and these are exact names out of the enum.
                Content = new TextBlock { Text = label }
            };

            Brush ink = TryFindResource("Ink") as Brush;
            if (ink != null) box.Foreground = ink;

            return box;
        }

        private void TickChanged(object sender, RoutedEventArgs e)
        {
            Ready();
        }

        /// <summary>
        /// A kind's own box also gates its languages: with the kind off, what it is written in
        /// decides nothing, and leaving those boxes live invites the operator to set a filter
        /// that has no effect.
        /// </summary>
        private void KindChanged(object sender, RoutedEventArgs e)
        {
            foreach (KindRow row in _rows)
                foreach (CheckBox language in row.Languages)
                    language.IsEnabled = row.Kind.IsChecked == true;

            Ready();
        }

        private void AllKinds(object sender, RoutedEventArgs e)
        {
            foreach (KindRow row in _rows) row.Kind.IsChecked = true;
        }

        private void NoKinds(object sender, RoutedEventArgs e)
        {
            foreach (KindRow row in _rows) row.Kind.IsChecked = false;
        }

        private void AllLanguages(object sender, RoutedEventArgs e)
        {
            Tick(true);
        }

        private void NoLanguages(object sender, RoutedEventArgs e)
        {
            Tick(false);
        }

        private void Tick(bool ticked)
        {
            foreach (KindRow row in _rows)
                foreach (CheckBox language in row.Languages)
                    language.IsChecked = ticked;
        }

        private void MapClicked(object sender, RoutedEventArgs e)
        {
            string plc = PlcBox.SelectedItem as string;
            string unit = UnitBox.SelectedItem as string;

            if (plc == null) return;

            MapFilter filter = Chosen();

            Map(plc, unit, filter, false);
        }

        /// <summary>
        /// The walk itself, shared by the button and by the first run of a project that has no
        /// map yet — <paramref name="thenCompare"/> being the only difference between them.
        /// </summary>
        private void Map(string plc, string unit, MapFilter filter, bool thenCompare)
        {
            Working(true);
            StatusText.Text = "Reading " + plc + "…";

            // A walk is about to replace whatever a comparison said, and the two describe
            // different moments: leaving the trees up would show a comparison of the scope
            // before this one, with nothing saying so.
            Empty(ProjectTree);
            Empty(RepositoryTree);

            RepositorySays(string.Empty);
            Problems(null);
            ProjectSays(filter.Narrows
                ? "Walking the project, keeping what is ticked."
                : "Walking the project. A PLC of a few thousand objects takes a moment.");

            _worker.Post(
                session => session.Map(plc, unit, filter, Say),
                map =>
                {
                    Mapped(map);

                    if (thenCompare && map != null) CompareClicked(null, null);
                },
                exception =>
                {
                    Working(false);
                    Failed(exception);
                });
        }

        private static void Empty(TreeView tree)
        {
            tree.Items.Clear();
        }

        /// <summary>
        /// Holds the project's map against the core it is built on.
        ///
        /// **Not on the worker's thread**, and that is worth saying: `TiaWorker` owns the one
        /// thread Openness objects belong to, and none of this touches TIA at all — it reads a
        /// configuration, copies a folder and parses two JSON files. Putting it there would
        /// queue a file copy behind a walk, or a walk behind it, for no reason but habit.
        /// </summary>
        private void CompareClicked(object sender, RoutedEventArgs e)
        {
            string directory = _projectDirectory;

            if (string.IsNullOrWhiteSpace(directory)) return;

            Working(true);
            StatusText.Text = "Reading the core…";

            Problems(null);
            ProjectSays("Copying the core into the project and reading it back.");

            Dispatcher dispatcher = Dispatcher;

            Task.Run(() => Compared(directory)).ContinueWith(done =>
                dispatcher.BeginInvoke(new Action(() =>
                {
                    Working(false);

                    if (done.Exception != null) Failed(done.Exception.GetBaseException());
                    else Show(done.Result);
                })));
        }

        /// <summary>
        /// The whole comparison, off the UI thread: the map as it was written, the core as the
        /// project now holds it, and the one held against the other.
        ///
        /// **The map is read back off disk rather than kept from the last walk.** It makes
        /// *Compare* work on a window reopened tomorrow, and it is what exercises the round trip
        /// through `repo\project.json` — including its format guard, which is the one thing that
        /// stands between an old map and a comparison that reads it as covering everything.
        /// </summary>
        private static Comparison Compared(string directory)
        {
            string problem;
            ProjectMap map = ProjectMapFile.Read(directory, out problem);

            if (map == null)
                return Comparison.Failed(problem ?? "This project has no map yet. Press Map project first.");

            CoreRefreshResult core = CoreRefresh.Run(directory);

            if (!core.Ready) return Comparison.Without(core);

            return Comparison.Of(map, core, CoreComparison.Of(core.Catalog, map));
        }

        private void Show(Comparison done)
        {
            if (done == null)
            {
                Told("Nothing came back from the comparison.", "Not compared.");
                return;
            }

            if (done.Result == null)
            {
                // A project that names no core is a fact about the project; a repository that is
                // not on this machine is something to go and fix. The two read alike in one line,
                // so the status line is what tells them apart.
                Told(done.Problem, done.NamesCore ? "Not compared." : "No core.");
                return;
            }

            // The scope the *map* covers, which is not always the one the combo boxes show: a
            // comparison is of the map on disk, and that may be yesterday's unit.
            _shown = done;
            _scope = done.Map?.Unit;

            ProjectSays(Summary(done));
            RepositorySays(Offered(done.Result));

            Chips(done.Result);
            Project(done.Result);

            RepoChips(done.Result);
            Repository(done.Result);

            List<string> problems = new List<string>();

            if (done.Core.Issues != null)
                foreach (ValidationIssue issue in done.Core.Issues.Issues)
                    problems.Add(issue.Path + ": " + issue.Message);

            Problems(problems.Count == 0 ? null : Listed(problems));

            // What *Sync folders with core* would act on is exactly what the comparison just
            // found misplaced, so the button is decided here rather than kept in step by hand.
            SyncButton.IsEnabled = !_busy && done.Result.Count(Finding.Misplaced) > 0;

            StatusText.Text = "Compared.";
        }

        /// <summary>
        /// One sentence under the project tree, with both trees emptied.
        ///
        /// **A comparison that did not happen must not leave the last one on screen.** They
        /// describe different moments, and two trees nobody can date is worse than none.
        /// </summary>
        private void Told(string text, string status)
        {
            Empty(ProjectTree);
            Empty(RepositoryTree);

            _compared = null;
            _shown = null;

            SyncButton.IsEnabled = false;
            DownloadButton.IsEnabled = false;

            RepositorySays(string.Empty);
            ProjectSays(text);

            StatusText.Text = status;
        }

        private static string Summary(Comparison done)
        {
            int all = done.Result.Objects.Count;
            int foreign = done.Result.Count(Finding.NotFromCore);

            return string.Format(
                CultureInfo.CurrentCulture,
                "Project — {0} of {1} objects come from the core",
                all - foreign, all);
        }

        private static string Offered(CoreComparison result)
        {
            int held = 0;
            int other = 0;

            foreach (CoreNodeState one in result.Repository)
            {
                if (one.State == NodeState.Held) held++;
                else if (one.State == NodeState.AtAnotherVersion) other++;
            }

            string counts = string.Format(
                CultureInfo.CurrentCulture,
                "Repository — {0} current nodes, {1} in the project, {2} at another version",
                result.Repository.Count, held, other);

            // A filtered map cannot say what is missing, and a panel that quietly showed 241
            // absent rows would be answering a question it was never asked.
            return result.AbsentKnown
                ? counts + ", " + result.Absent.Count + " absent"
                : counts + Environment.NewLine + "(the map was filtered, so what is missing is not known from it)";
        }

        // ---- The project panel ----------------------------------------------------------------

        /// <summary>
        /// One tick box per finding, carrying its count — the counts *are* the filter, which is
        /// the shape the coding-style report already settled on.
        ///
        /// **"Not from the core" starts off.** In a real project it is most of the rows, and this
        /// window is about the core; the box stays on screen with its count, so nothing is hidden
        /// without saying how much.
        /// </summary>
        private void Chips(CoreComparison result)
        {
            ChipsPanel.Children.Clear();
            _chips.Clear();

            Chip("Up to date", null, result.Clean, true);
            Chip("Outdated", Finding.Outdated, result.Count(Finding.Outdated), true);
            Chip("Unknown version", Finding.UnknownVersion, result.Count(Finding.UnknownVersion), true);
            // "In the wrong folder", not "Split family": the finding stopped being about a family
            // spanning two folders when it became an exact comparison against what the block's
            // own TITLE says, and a tick box has to be spelled the way the rows it filters are -
            // which here read "belongs in core/node".
            Chip("In the wrong folder", Finding.Misplaced, result.Count(Finding.Misplaced), true);
            Chip("Disagreeing", Finding.Disagrees, result.Count(Finding.Disagrees), true);
            Chip("Not from the core", Finding.NotFromCore, result.Count(Finding.NotFromCore), false);
        }

        private void Chip(string label, Finding? finding, int count, bool ticked)
        {
            CheckBox box = new CheckBox
            {
                IsChecked = ticked,
                Tag = finding,
                Margin = new Thickness(0, 4, 14, 0),
                Content = new TextBlock { Text = label + " (" + count + ")" }
            };

            Brush ink = TryFindResource("Ink") as Brush;
            if (ink != null) box.Foreground = ink;

            box.Checked += ChipChanged;
            box.Unchecked += ChipChanged;

            _chips.Add(box);
            ChipsPanel.Children.Add(box);
        }

        private void ChipChanged(object sender, RoutedEventArgs e)
        {
            if (_settling) return;

            Project(_compared);
        }

        private void AllFindings(object sender, RoutedEventArgs e)
        {
            Findings(true);
        }

        private void NoFindings(object sender, RoutedEventArgs e)
        {
            Findings(false);
        }

        /// <summary>
        /// **Every box is set before the tree is rebuilt, not one rebuild per box.** Each of
        /// them redraws the whole tree on its own, so *All* over six findings would build a
        /// few hundred objects into it six times over.
        /// </summary>
        private void Findings(bool ticked)
        {
            _settling = true;

            try
            {
                foreach (CheckBox box in _chips) box.IsChecked = ticked;
            }
            finally
            {
                _settling = false;
            }

            Project(_compared);
        }

        private void AllStates(object sender, RoutedEventArgs e)
        {
            States(true);
        }

        private void NoStates(object sender, RoutedEventArgs e)
        {
            States(false);
        }

        private void States(bool ticked)
        {
            _settling = true;

            try
            {
                foreach (CheckBox box in _repoChips) box.IsChecked = ticked;
            }
            finally
            {
                _settling = false;
            }

            Repository(_compared);
        }

        /// <summary>
        /// The project as a tree, so the two panels read alike (the maintainer asked for it).
        ///
        /// **The shape comes out of the map, not out of a rule here.** A mapped object's folder
        /// already begins with TIA's own name for its tree — `Program blocks/03-ALL/adt`,
        /// `PLC data types/node` — because the walk starts at the software's root groups. All
        /// this adds is the one root the map covers: the software unit, or `*`.
        ///
        /// **A folder left empty by the tick boxes is not drawn.** Folders exist here only
        /// because something is in them, so filtering to the outdated blocks shows the folders
        /// that hold outdated blocks rather than the whole tree with four leaves in it.
        /// </summary>
        private void Project(CoreComparison result)
        {
            _compared = result;

            ProjectTree.Items.Clear();

            if (result == null) return;

            TreeViewItem root = new TreeViewItem { Tag = Places.UnitOrGeneral(_scope) };

            foreach (ComparedObject one in result.Objects)
                if (Wanted(one)) Under(root, one.Found?.Folder).Items.Add(Leaf(one));

            ProjectTree.Items.Add(root);

            // A root holding nothing is a tree of one empty folder, which says less than an
            // empty panel does.
            if (Number(root) == 0) ProjectTree.Items.Clear();
            else Open(ProjectTree.Items);
        }

        /// <summary>
        /// The folder one object belongs in, made on the way down if it is not there yet. **A
        /// folder the map never put anything in cannot appear at all** — the map records objects
        /// with their paths, not folders, so an empty folder somebody created is invisible to
        /// this window. Worth knowing before it is read as "there is no such folder".
        /// </summary>
        private static TreeViewItem Under(TreeViewItem root, string folder)
        {
            TreeViewItem parent = root;

            if (string.IsNullOrEmpty(folder)) return parent;

            foreach (string name in folder.Split(new[] { Places.Separator }, StringSplitOptions.RemoveEmptyEntries))
                parent = Child(parent, name);

            return parent;
        }

        private static TreeViewItem Child(TreeViewItem parent, string name)
        {
            foreach (object one in parent.Items)
            {
                TreeViewItem item = one as TreeViewItem;
                string folder = item?.Tag as string;

                if (folder != null && string.Equals(folder, name, StringComparison.OrdinalIgnoreCase)) return item;
            }

            TreeViewItem made = new TreeViewItem { Tag = name };

            parent.Items.Add(made);

            return made;
        }

        /// <summary>
        /// One mapped object: <c>name v3.0 FB up to date</c>.
        ///
        /// **The kind is spelled as the map spells it** — `GlobalDB`, `InstanceDB`, `PlcStruct` —
        /// which is `CodingStyleNames`' vocabulary, the same one `config.json`, the coding-style
        /// report and the filter rows at the top of this very window use. `DB` would read faster
        /// and would hide the difference between a global and an instance data block, which the
        /// map does draw.
        /// </summary>
        private TreeViewItem Leaf(ComparedObject one)
        {
            StackPanel header = new StackPanel { Orientation = Orientation.Horizontal };

            Say(header, one.Found?.Name, "Ink");
            Say(header, string.IsNullOrWhiteSpace(one.Version) ? null : "  v" + one.Version, "Ink");
            Say(header, string.IsNullOrWhiteSpace(one.Found?.Kind) ? null : "  " + one.Found.Kind, "InkMuted");
            Say(header, "   " + ComparedRow.Message(one), ComparedRow.InkKey(one));

            return new TreeViewItem { Header = header, Tag = one };
        }

        /// <summary>
        /// Whether a row passes the tick boxes. **Any of its findings ticked is enough**: a block
        /// that is both outdated and split belongs under either, and hiding it unless *both* were
        /// ticked would lose it from the one filter somebody opened.
        /// </summary>
        private bool Wanted(ComparedObject one)
        {
            foreach (CheckBox box in _chips)
            {
                if (box.IsChecked != true) continue;

                Finding? finding = box.Tag as Finding?;

                if (finding == null)
                {
                    if (one.Clean) return true;
                }
                else if (one.Is(finding.Value)) return true;
            }

            return false;
        }

        // ---- The repository panel -------------------------------------------------------------

        /// <summary>
        /// One tick box per state — how "show me what is missing" gets asked with both trees
        /// open, and the first thing importing will want.
        ///
        /// **`Not covered` appears only when there is something in it.** It can only happen on a
        /// filtered map, so on every other run it would be a box that never does anything.
        /// </summary>
        private void RepoChips(CoreComparison result)
        {
            RepoChipsPanel.Children.Clear();
            _repoChips.Clear();

            RepoChip("In the project", NodeState.Held, result);
            RepoChip("At another version", NodeState.AtAnotherVersion, result);
            RepoChip("Absent", NodeState.Absent, result);

            if (Counted(result, NodeState.Unknown) > 0) RepoChip("Not covered", NodeState.Unknown, result);
        }

        private void RepoChip(string label, NodeState state, CoreComparison result)
        {
            CheckBox box = new CheckBox
            {
                IsChecked = true,
                Tag = state,
                Margin = new Thickness(0, 4, 14, 0),
                Content = new TextBlock { Text = label + " (" + Counted(result, state) + ")" }
            };

            Brush ink = TryFindResource("Ink") as Brush;
            if (ink != null) box.Foreground = ink;

            box.Checked += RepoChipChanged;
            box.Unchecked += RepoChipChanged;

            _repoChips.Add(box);
            RepoChipsPanel.Children.Add(box);
        }

        private static int Counted(CoreComparison result, NodeState state)
        {
            int found = 0;

            foreach (CoreNodeState one in result.Repository)
                if (one.State == state) found++;

            return found;
        }

        private void RepoChipChanged(object sender, RoutedEventArgs e)
        {
            Repository(_compared);
        }

        /// <summary>
        /// The core as a tree of its own folders, each node saying what the project has of it —
        /// and **open**, which the maintainer asked for: both panels are read side by side, and
        /// one of them folded away is one you have to go looking through.
        /// </summary>
        private void Repository(CoreComparison result)
        {
            RepositoryTree.Items.Clear();
            _picks.Clear();
            DownloadButton.IsEnabled = false;

            if (result == null) return;

            string folder = null;
            TreeViewItem group = null;

            foreach (CoreNodeState one in result.Repository)
            {
                if (!WantedNode(one)) continue;

                // A folder is only made once something in it has passed, so one the tick boxes
                // emptied never appears.
                if (group == null || !string.Equals(one.Folder, folder, StringComparison.OrdinalIgnoreCase))
                {
                    folder = one.Folder;
                    group = new TreeViewItem { Tag = folder };

                    RepositoryTree.Items.Add(group);
                }

                group.Items.Add(Leaf(one));
            }

            foreach (object one in RepositoryTree.Items) Number((TreeViewItem)one);

            Open(RepositoryTree.Items);
        }

        private bool WantedNode(CoreNodeState one)
        {
            if (_repoChips.Count == 0) return true;

            foreach (CheckBox box in _repoChips)
                if (box.IsChecked == true && (NodeState)box.Tag == one.State) return true;

            return false;
        }

        /// <summary>
        /// One core node, with the tick box that decides whether a download takes it.
        ///
        /// **The box is on the node, not on the folder.** A folder tick would be a second thing
        /// to keep in step with what is under it after every filter change; what a folder is for
        /// here is reading, and the dependency closure is what saves the clicking — ticking one
        /// block brings everything it needs with it.
        /// </summary>
        private TreeViewItem Leaf(CoreNodeState one)
        {
            StackPanel header = new StackPanel { Orientation = Orientation.Horizontal };

            CheckBox box = new CheckBox { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

            box.Checked += PickChanged;
            box.Unchecked += PickChanged;

            header.Children.Add(box);
            _picks.Add(new Pick(box, one));

            Say(header, one.Node.Base, "Ink");
            Say(header, "  v" + one.Node.Version, "Ink");
            Say(header, "   " + State(one), StateInk(one.State));

            return new TreeViewItem { Header = header, Tag = one };
        }

        private void PickChanged(object sender, RoutedEventArgs e)
        {
            DownloadButton.IsEnabled = !_busy && Picked().Count > 0;
        }

        private List<string> Picked()
        {
            List<string> ids = new List<string>();

            foreach (Pick one in _picks)
                if (one.Box.IsChecked == true && one.Node.Node?.Id != null) ids.Add(one.Node.Node.Id);

            return ids;
        }

        /// <summary>One node in the repository panel and the box that chooses it.</summary>
        private sealed class Pick
        {
            public Pick(CheckBox box, CoreNodeState node)
            {
                Box = box;
                Node = node;
            }

            public CheckBox Box { get; }

            public CoreNodeState Node { get; }
        }

        // ---- Both trees -------------------------------------------------------------------------

        private void Say(StackPanel header, string text, string ink)
        {
            if (string.IsNullOrEmpty(text)) return;

            header.Children.Add(new TextBlock { Text = text, Foreground = TryFindResource(ink) as Brush });
        }

        /// <summary>
        /// Writes each folder's header once its contents are known, so it can say how many, and
        /// answers how many leaves are under it. **Counted rather than remembered**, because a
        /// folder several levels up holds what its children hold.
        /// </summary>
        private int Number(TreeViewItem folder)
        {
            int leaves = 0;

            foreach (object child in folder.Items)
            {
                TreeViewItem item = child as TreeViewItem;

                if (item == null) continue;

                leaves += item.Tag is string ? Number(item) : 1;
            }

            string name = folder.Tag as string;

            folder.Header = new TextBlock
            {
                Text = (string.IsNullOrEmpty(name) ? "(root)" : name) + "  (" + leaves + ")",
                Foreground = TryFindResource("Ink") as Brush
            };

            return leaves;
        }

        /// <summary>**Open, all of it**, which is what the maintainer asked for on both sides.</summary>
        private static void Open(ItemCollection items)
        {
            foreach (object one in items)
            {
                TreeViewItem item = one as TreeViewItem;

                if (item == null) continue;

                item.IsExpanded = true;

                Open(item.Items);
            }
        }

        private static string State(CoreNodeState one)
        {
            switch (one.State)
            {
                case NodeState.Held: return "in the project";
                case NodeState.AtAnotherVersion: return "the project has v" + one.HeldVersion;
                case NodeState.Absent: return "absent";
            }

            return "not covered by the map";
        }

        /// <summary>
        /// **Absent is muted, not marked.** It is 241 of 247 rows in a project that has taken a
        /// handful of blocks, and colouring the majority says nothing; what is worth picking out
        /// is what the project already has, and where it has something else.
        /// </summary>
        private static string StateInk(NodeState state)
        {
            if (state == NodeState.Held) return "InkGood";
            if (state == NodeState.AtAnotherVersion) return "InkWarn";

            return "InkMuted";
        }

        // ---- Downloading ------------------------------------------------------------------------

        /// <summary>
        /// Works out what the ticked blocks would do to the project, asks when it would reach
        /// past them, and only then writes.
        ///
        /// **The plan is made before anything is opened.** It is pure, so what the operator is
        /// shown and what the import then does come from the same answer rather than from two
        /// walks that could disagree.
        /// </summary>
        private void DownloadClicked(object sender, RoutedEventArgs e)
        {
            if (_shown?.Core?.Catalog == null || _compared == null || _worker == null) return;

            List<string> chosen = Picked();

            if (chosen.Count == 0) return;

            DownloadPlan plan = DownloadPlan.Of(_shown.Core.Catalog, _compared, chosen);

            if (plan.Nodes.Count == 0)
            {
                Told("Nothing to download: the core has none of what was ticked.", "Nothing to do.");
                return;
            }

            if (plan.NeedsConfirming && !Confirmed(plan))
            {
                StatusText.Text = "Cancelled.";
                return;
            }

            string plc = _shown.Map?.Plc;
            string unit = _shown.Map?.Unit;

            Working(true);
            DownloadButton.IsEnabled = false;
            StatusText.Text = "Importing…";

            Problems(null);
            ProjectSays("Writing " + plan.Nodes.Count + " objects into the project.");

            _worker.Post(
                session => session.Import(plc, unit, plan, Say),
                report => { Working(false); Wrote(report, plan); },
                exception =>
                {
                    Working(false);
                    Failed(exception);
                });
        }

        /// <summary>
        /// Asks before changing something other blocks depend on.
        ///
        /// **The only prompt in this window, and it earns it**: replacing a dependency the
        /// project already holds at another version changes the behaviour of blocks nobody
        /// selected, which is the one way a download can break a project without anything
        /// looking wrong afterwards.
        ///
        /// **It says what it cannot see.** The project's dependency lists come from each block's
        /// TITLE, so blocks of the plant declare nothing and are not counted — an operator
        /// reading a short list has to know it is a floor rather than a total.
        /// </summary>
        private static bool Confirmed(DownloadPlan plan)
        {
            List<string> lines = new List<string>
            {
                "This download would replace blocks that other blocks depend on:",
                string.Empty
            };

            foreach (PlannedNode one in plan.Impact)
            {
                lines.Add("    " + one.Node.Base + ": v" + one.HeldVersion + " becomes v" + one.Node.Version);
                lines.Add("        used by " + string.Join(", ", one.Users));
            }

            lines.Add(string.Empty);
            lines.Add(
                "Only blocks that carry the core's metadata declare what they depend on, so the " +
                "plant's own blocks are not counted here. There may be more.");

            return MessageBox.Show(
                string.Join(Environment.NewLine, lines),
                Product.Title + " - Download",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) == MessageBoxResult.OK;
        }

        private void Wrote(ImportReport report, DownloadPlan plan)
        {
            if (report == null)
            {
                Told("Nothing came back from the import.", "Not imported.");
                return;
            }

            // The comparison on screen described the project as it was a moment ago, and it no
            // longer does. Told empties both trees, which is what says so.
            Told(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} of {1} went in, {2} were already at the core's version, {3} refused. " +
                    "Map the project again to compare it.",
                    report.Imported,
                    plan.Nodes.Count - report.Skipped,
                    report.Skipped,
                    report.Failed),
                report.Failed > 0 ? "Imported, with refusals." : "Imported.");

            List<string> problems = new List<string>(report.Problems);

            foreach (ImportedNode one in report.Results)
                if (!one.Done) problems.Add(one.Name + ": " + one.Problem);

            Problems(problems.Count == 0 ? null : Listed(problems));
        }

        // ---- Putting a misplaced object back where its family says ------------------------------

        /// <summary>
        /// Moves every object the comparison found in a folder its family does not name.
        ///
        /// **It acts on exactly the rows the panel above it shows as misplaced**, which is why
        /// the plan comes out of the comparison rather than being worked out again: two answers
        /// to one question would eventually move something the tree called correct.
        ///
        /// **It asks first, and it is the second prompt in this window.** A move is an export, a
        /// delete and an import — Openness has no move at all — so for a moment the object exists
        /// only as a file, and that is worth saying before it happens rather than afterwards.
        /// </summary>
        private void SyncClicked(object sender, RoutedEventArgs e)
        {
            if (_compared == null || _shown == null || _worker == null) return;

            SyncPlan plan = SyncPlan.Of(_compared);

            if (plan.Count == 0)
            {
                Told("Nothing is in the wrong folder.", "Nothing to do.");
                return;
            }

            if (!Agreed(plan))
            {
                StatusText.Text = "Cancelled.";
                return;
            }

            string plc = _shown.Map?.Plc;
            string unit = _shown.Map?.Unit;

            Working(true);
            SyncButton.IsEnabled = false;
            StatusText.Text = "Moving…";

            Problems(null);
            ProjectSays("Moving " + plan.Count + " objects into the folders the core names.");

            _worker.Post(
                session => session.Sync(plc, unit, plan, Say),
                report => { Working(false); Synced(report, plan); },
                exception =>
                {
                    Working(false);
                    Failed(exception);
                });
        }

        /// <summary>
        /// Says what would move, and says what a move is.
        ///
        /// **Every object is named, up to a point.** "12 objects will be moved" is not something
        /// anybody can check; a list of names against their destinations is. Past twenty the
        /// list stops being read, so it stops.
        /// </summary>
        private static bool Agreed(SyncPlan plan)
        {
            const int Most = 20;

            List<string> lines = new List<string>
            {
                plan.Count == 1
                    ? "One object is not in the folder its family names:"
                    : plan.Count + " objects are not in the folders their families name:",
                string.Empty
            };

            for (int i = 0; i < plan.Objects.Count && i < Most; i++)
            {
                MisplacedObject one = plan.Objects[i];

                lines.Add("    " + one.Name + "   " + one.From + "  ->  " + one.Family);
            }

            if (plan.Count > Most) lines.Add("    …and " + (plan.Count - Most) + " more");

            lines.Add(string.Empty);
            lines.Add(
                "Openness cannot move an object, so each one is exported, deleted and imported " +
                "into its new folder. Anything that will not go back in is left as a file and " +
                "named here afterwards.");

            return MessageBox.Show(
                string.Join(Environment.NewLine, lines),
                Product.Title + " - Sync folders with core",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) == MessageBoxResult.OK;
        }

        private void Synced(SyncReport report, SyncPlan plan)
        {
            if (report == null)
            {
                Told("Nothing came back from the move.", "Not moved.");
                return;
            }

            // The comparison described the project as it was a moment ago. Told empties both
            // trees, which is what says so.
            Told(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} of {1} moved, {2} did not. Map the project again to compare it.",
                    report.Moved, plan.Count, report.Failed),
                report.Stranded > 0
                    ? "Moved, and " + report.Stranded + " left as files."
                    : report.Failed > 0 ? "Moved, with refusals." : "Moved.");

            List<string> problems = new List<string>(report.Problems);

            foreach (MovedObject one in report.Results)
                if (!one.Done) problems.Add(one.Name + ": " + one.Problem);

            Problems(problems.Count == 0 ? null : Listed(problems));
        }

        /// <summary>One comparison, and why there is none when there is not.</summary>
        private sealed class Comparison
        {
            private Comparison(ProjectMap map, CoreRefreshResult core, CoreComparison result, string problem, bool names)
            {
                Map = map;
                Core = core;
                Result = result;
                Problem = problem;
                NamesCore = names;
            }

            /// <summary>The map as it was read back — which names the scope the panels are of.</summary>
            public ProjectMap Map { get; }

            public CoreRefreshResult Core { get; }

            public CoreComparison Result { get; }

            public string Problem { get; }

            public bool NamesCore { get; }

            public static Comparison Of(ProjectMap map, CoreRefreshResult core, CoreComparison result) =>
                new Comparison(map, core, result, null, true);

            public static Comparison Without(CoreRefreshResult core) =>
                new Comparison(null, core, null, core.Problem, core.NamesCore);

            public static Comparison Failed(string problem) =>
                new Comparison(null, null, null, problem, true);
        }

        /// <summary>
        /// What the map is asked for, or **everything when every box is ticked** - which is the
        /// absence of a decision rather than a list of the whole project, and is what keeps a
        /// map of the whole PLC from recording a filter that narrows nothing.
        /// </summary>
        private MapFilter Chosen()
        {
            List<KindFilter> wanted = new List<KindFilter>();
            bool narrows = false;

            foreach (KindRow row in _rows)
            {
                if (row.Kind.IsChecked != true)
                {
                    narrows = true;
                    continue;
                }

                List<string> languages = new List<string>();

                foreach (CheckBox one in row.Languages)
                    if (one.IsChecked == true) languages.Add(one.Tag as string);

                // A row with every language ticked says nothing about languages at all, which
                // is what an empty list means inside a kind - and keeps the recorded filter
                // about what was narrowed rather than about what was on screen.
                bool all = languages.Count == row.Languages.Count;

                if (!all) narrows = true;

                wanted.Add(KindFilter.Of(row.Kind.Tag as string, all ? null : languages));
            }

            return narrows ? MapFilter.Of(wanted) : MapFilter.Everything;
        }

        /// <summary>
        /// Whether the Map button may be pressed, and why not when it may not.
        ///
        /// **Nothing ticked is refused rather than read as everything.** The filter's own rule
        /// is that empty means the whole scope - the safe reading of a decision nobody made -
        /// and an operator who has just cleared a panel has very much made one. The two would
        /// contradict each other in the one place it matters, so the window does not let it
        /// through.
        ///
        /// **The same rule one level down**: a kind that is ticked with none of its languages
        /// would map nothing, and it is named rather than silently skipped - a row asking for
        /// FCs and producing none is exactly the hole this design refuses elsewhere.
        /// </summary>
        private void Ready()
        {
            string empty = Empty();
            bool enough = _rows.Count == 0 || (AnyKind() && empty == null);

            MapButton.IsEnabled = !_busy && PlcBox.SelectedItem != null && enough;

            if (_busy) return;

            if (!enough)
            {
                StatusText.Text = empty == null
                    ? "No object is ticked, so there is nothing to map."
                    : empty + " is ticked with no language, so it would map nothing.";

                _complaining = true;
                return;
            }

            if (!_complaining) return;

            StatusText.Text = "Ready.";
            _complaining = false;
        }

        private bool AnyKind()
        {
            foreach (KindRow row in _rows)
                if (row.Kind.IsChecked == true) return true;

            return false;
        }

        /// <summary>The first ticked kind that would match nothing, or null.</summary>
        private string Empty()
        {
            foreach (KindRow row in _rows)
            {
                if (row.Kind.IsChecked != true || row.Languages.Count == 0) continue;

                bool any = false;

                foreach (CheckBox one in row.Languages)
                    if (one.IsChecked == true) any = true;

                if (!any) return row.Kind.Tag as string;
            }

            return null;
        }

        /// <summary>One kind on screen: its own box, and the boxes for its languages.</summary>
        private sealed class KindRow
        {
            public CheckBox Kind;

            public readonly List<CheckBox> Languages = new List<CheckBox>();
        }

        /// <summary>
        /// Where the walk has got to. Called from the worker's thread, so it marshals itself -
        /// the one place in this window that has to, and it says so rather than leaving the
        /// next reader to work it out.
        /// </summary>
        private void Say(string text)
        {
            Dispatcher.BeginInvoke(new Action(() => StatusText.Text = text));
        }

        private void Mapped(ProjectMap map)
        {
            Working(false);

            if (map == null)
            {
                Told("Nothing came back from the walk.", "Not mapped.");
                return;
            }

            string problem = ProjectMapFile.Write(map, _projectDirectory);

            ProjectSays(Counted(map));
            StatusText.Text = problem == null ? "Mapped." : "Mapped, but not written.";

            // Whatever would not read while walking, and whatever kept the file from being
            // written. Never folded into the counts: a map with holes has to say where they
            // are, or it reads as a whole one.
            List<string> problems = new List<string>();

            if (problem != null) problems.Add(problem);
            if (map.Problems != null) problems.AddRange(map.Problems);

            Problems(problems.Count == 0 ? null : Listed(problems));
        }

        /// <summary>
        /// What was found, in the two numbers that matter: everything, and how much of it says
        /// it came from the core. The second is what a comparison has to work with.
        /// </summary>
        private static string Counted(ProjectMap map)
        {
            int all = map.Objects == null ? 0 : map.Objects.Count;
            int core = 0;

            if (map.Objects != null)
                foreach (ProjectObject found in map.Objects)
                    if (found.FromCore) core++;

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0} objects in {1}, {2} of them from the core.{3}",
                all,
                map.Plc + (Places.IsGeneralProgram(map.Unit) ? string.Empty : " / " + map.Unit),
                core,

                // **A filtered map is not a map of the project**, and the heading is where
                // somebody reads the number: "18 objects" under a filter would otherwise be
                // taken for what the PLC holds.
                map.Filter == null ? string.Empty : " Of what was ticked, not of the whole scope.");
        }

        private static string Listed(IReadOnlyList<string> problems)
        {
            const int Most = 12;

            List<string> lines = new List<string> { problems.Count == 1 ? "One thing would not read:" : problems.Count + " things would not read:" };

            for (int i = 0; i < problems.Count && i < Most; i++) lines.Add("    " + problems[i]);

            if (problems.Count > Most) lines.Add("    …and " + (problems.Count - Most) + " more");

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// **Filled in place, never cleared and refilled.** A ComboBox whose ItemsSource
        /// empties loses its selection and comes back blank - the trap the config editor
        /// already records.
        /// </summary>
        private void Fill(ComboBox box, IReadOnlyList<string> values, string wanted)
        {
            _filling = true;

            try
            {
                box.Items.Clear();

                foreach (string value in values) box.Items.Add(value);

                int index = wanted == null ? -1 : IndexOf(values, wanted);

                box.SelectedIndex = index >= 0 ? index : (values.Count > 0 ? 0 : -1);
            }
            finally
            {
                _filling = false;
            }
        }

        private static int IndexOf(IReadOnlyList<string> values, string wanted)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], wanted, StringComparison.OrdinalIgnoreCase)) return i;

            return -1;
        }

        private void Working(bool busy)
        {
            _busy = busy;

            PlcBox.IsEnabled = !busy;
            UnitBox.IsEnabled = !busy;
            FilterPanel.IsEnabled = !busy;

            // Compare needs a project folder and nothing else - not a PLC, not a tick box. It
            // reads the map that is already on disk.
            CompareButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_projectDirectory);
            DownloadButton.IsEnabled = !busy && Picked().Count > 0;

            // Sync acts on what the comparison found misplaced, so it needs one on screen —
            // and there is nothing to be found before the project has been compared.
            SyncButton.IsEnabled = !busy && _compared != null && _compared.Count(Finding.Misplaced) > 0;

            // Never `IsEnabled = !busy` on its own: what is ticked decides it too, and coming
            // back from a run must not re-enable a button the tick boxes have disabled.
            Ready();
        }

        private void Failed(Exception exception)
        {
            // Through Told, which empties both trees: a failure written under a comparison
            // nobody can tell the age of is worse than one standing on its own.
            Told(exception == null ? "Something went wrong." : exception.Message, "Failed.");
        }

        /// <summary>
        /// Nothing was attached — and, when there is something to attach to, this is where the
        /// operator picks it rather than where the window gives up.
        /// </summary>
        private void Refused(TiaAttachment attachment)
        {
            bool choose = attachment.CanChoose;

            ProblemPanel.Visibility = Visibility.Visible;
            ProblemText.Text = attachment.Problem;

            string considered = Considered(attachment.Considered, choose);

            ConsideredText.Text = considered;
            ConsideredText.Visibility = considered.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            OfferChoices(choose ? attachment.Considered : null);

            StatusText.Text = choose ? "Pick a TIA Portal." : "Not attached.";
        }

        /// <summary>
        /// The running instances, as a list to choose from.
        ///
        /// **Nothing is preselected, deliberately.** A highlighted first row turns Attach into
        /// one click on whatever happened to be at the top, which is the guess this panel
        /// exists to replace with a decision. The button stays off until a row is picked.
        ///
        /// **Not called `Offer`**, which the tick boxes already are: two overloads differing
        /// only by an interface argument made `Offer(null)` ambiguous, and disambiguating a
        /// null with a cast is a worse sentence than two names.
        /// </summary>
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
                attachment => Arrived(attachment, _plc, _unit),
                exception =>
                {
                    // The list is put back rather than left dead: the instance the operator
                    // picked may have closed, and the next one along is still worth a try.
                    ChoicesList.IsEnabled = true;
                    AttachButton.IsEnabled = true;
                    Failed(exception);
                });
        }

        /// <summary>
        /// What was running, so the operator can tell "none" from "several, and not that one"
        /// - two failures that read alike and want opposite answers.
        ///
        /// **Null means nothing ever looked**, and saying "no TIA Portal was running" there
        /// would be a claim this never checked - which is exactly what it said on the VM,
        /// under an error about a missing assembly, with TIA Portal open behind the window.
        /// </summary>
        /// <param name="choosing">
        /// Whether the list below is about to show the same instances. It is then a heading
        /// rather than a report, and printing both would say everything twice.
        /// </param>
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
