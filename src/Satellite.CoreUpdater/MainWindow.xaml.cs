using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

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
        private TiaWorker _worker;
        private string _projectDirectory;
        private bool _filling;

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

        /// <summary>What the window shows while the attach is still running.</summary>
        public void ShowWaiting(int? parentProcessId)
        {
            WaitingText.Text = parentProcessId.HasValue
                ? string.Format(CultureInfo.CurrentCulture,
                    "Attaching to the TIA Portal that opened this window (process {0})…", parentProcessId.Value)
                : "Looking for a running TIA Portal…";

            StatusText.Text = string.Empty;
        }

        /// <summary>The answer to the attach, whichever of the three it is.</summary>
        public void Arrived(TiaAttachment attachment, string plc, string unit)
        {
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

                    MapButton.IsEnabled = found.Count > 0;

                    // The unit list belongs to whichever PLC ended up selected, which
                    // PlcChanged is already the one place that knows.
                    Units(unit);
                },
                Failed);
        }

        private void PlcChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_filling) return;

            Units(Places.GeneralProgram);
        }

        private void Units(string unit)
        {
            string plc = PlcBox.SelectedItem as string;

            if (plc == null)
            {
                Fill(UnitBox, new string[0], null);
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
                },
                Failed);
        }

        private void MapClicked(object sender, RoutedEventArgs e)
        {
            string plc = PlcBox.SelectedItem as string;
            string unit = UnitBox.SelectedItem as string;

            if (plc == null) return;

            Working(true);
            StatusText.Text = "Reading " + plc + "…";

            MapHeading.Visibility = Visibility.Collapsed;
            MapProblems.Visibility = Visibility.Collapsed;
            MapText.Text = "Walking the project. A PLC of a few thousand objects takes a moment.";

            _worker.Post(
                session => session.Map(plc, unit, Say),
                Mapped,
                exception =>
                {
                    Working(false);
                    Failed(exception);
                });
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
                "{0} objects in {1}, {2} of them from the core.",
                all, map.Plc + (Places.IsGeneralProgram(map.Unit) ? string.Empty : " / " + map.Unit), core);
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
        private void Fill(System.Windows.Controls.ComboBox box, IReadOnlyList<string> values, string wanted)
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
            MapButton.IsEnabled = !busy;
            PlcBox.IsEnabled = !busy;
            UnitBox.IsEnabled = !busy;
        }

        private void Failed(Exception exception)
        {
            StatusText.Text = "Failed.";
            MapText.Text = exception == null ? "Something went wrong." : exception.Message;
        }

        private void Refused(TiaAttachment attachment)
        {
            ProblemPanel.Visibility = Visibility.Visible;
            ProblemText.Text = attachment.Problem;

            string considered = Considered(attachment.Considered);

            ConsideredText.Text = considered;
            ConsideredText.Visibility = considered.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            StatusText.Text = "Not attached.";
        }

        /// <summary>
        /// What was running, so the operator can tell "none" from "several, and not that one"
        /// - two failures that read alike and want opposite answers.
        ///
        /// **Null means nothing ever looked**, and saying "no TIA Portal was running" there
        /// would be a claim this never checked - which is exactly what it said on the VM,
        /// under an error about a missing assembly, with TIA Portal open behind the window.
        /// </summary>
        private static string Considered(IReadOnlyList<string> considered)
        {
            if (considered == null) return string.Empty;

            if (considered.Count == 0)
                return "No TIA Portal was running when this window opened.";

            string heading = considered.Count == 1
                ? "One TIA Portal was running:"
                : string.Format(CultureInfo.CurrentCulture, "{0} TIA Portals were running:", considered.Count);

            return heading + Environment.NewLine + "    " +
                   string.Join(Environment.NewLine + "    ", considered);
        }
    }
}
