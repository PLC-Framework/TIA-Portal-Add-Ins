using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Core;
using Core.Repo;

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
                MapText.Text = "This project has not been saved, so it has no folder - and the map is " +
                               "written inside it. Save the project and open this window again.";
                MapButton.IsEnabled = false;
                StatusText.Text = "Attached, with nowhere to write.";
                return;
            }

            MapText.Text = attachment.ProjectDirectory;
            StatusText.Text = "Reading the PLCs…";

            Plcs(plc, unit);
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
                },
                exception =>
                {
                    Offer(null);
                    Working(false);
                    Failed(exception);
                });
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

            Working(true);
            StatusText.Text = "Reading " + plc + "…";

            MapHeading.Visibility = Visibility.Collapsed;
            MapProblems.Visibility = Visibility.Collapsed;
            MapText.Text = filter.Narrows
                ? "Walking the project, keeping what is ticked."
                : "Walking the project. A PLC of a few thousand objects takes a moment.";

            _worker.Post(
                session => session.Map(plc, unit, filter, Say),
                Mapped,
                exception =>
                {
                    Working(false);
                    Failed(exception);
                });
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
                MapText.Text = "Nothing came back from the walk.";
                StatusText.Text = "Not mapped.";
                return;
            }

            string problem = ProjectMapFile.Write(map, _projectDirectory);

            MapHeading.Visibility = Visibility.Visible;
            MapHeading.Text = Counted(map);

            MapText.Text = problem ?? RepoPaths.ProjectFor(_projectDirectory);
            StatusText.Text = problem == null ? "Mapped." : "Mapped, but not written.";

            // Whatever would not read while walking. Kept on screen rather than folded into
            // the counts: a map with holes has to say where they are, or it reads as whole.
            if (map.Problems != null && map.Problems.Count > 0)
            {
                MapProblems.Visibility = Visibility.Visible;
                MapProblems.Text = Listed(map.Problems);
            }
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

            // Never `IsEnabled = !busy` on its own: what is ticked decides it too, and coming
            // back from a run must not re-enable a button the tick boxes have disabled.
            Ready();
        }

        private void Failed(Exception exception)
        {
            StatusText.Text = "Failed.";
            MapText.Text = exception == null ? "Something went wrong." : exception.Message;
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
