using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using Core.Repo;
using Core.Repo.PlcCore;

namespace Satellite.CoreUpdater.Download
{
    /// <summary>
    /// Asks what a download should write, **whenever it would meet anything the project already
    /// has** - the maintainer's design, and the answer to two things the VM found at once.
    ///
    /// **TIA overwrites without a word.** Generating a block that already exists writes over it,
    /// silently, so nothing downstream of this window will ever stop a download: what the
    /// operator sees here is the whole of the safety.
    ///
    /// **A second download of a block with dependencies brought the block and none of them.**
    /// The plan leaves a dependency alone when the project already has it at the core's version
    /// - the cheapest right answer the first time, and the wrong one when the point of
    /// downloading again is to put everything back. So every entry has a tick box, the plan's
    /// own proposal is only the starting position, and <c>All</c> is the second download.
    ///
    /// It replaces a message box that listed only the dependencies other blocks used: that one
    /// could say what would change behind somebody's back, and could not be told to do more.
    /// </summary>
    public partial class DownloadWindow : Window
    {
        private readonly List<DownloadRow> _rows = new List<DownloadRow>();

        public DownloadWindow(DownloadPlan plan)
        {
            InitializeComponent();

            foreach (PlannedNode one in plan.Nodes)
                _rows.Add(new DownloadRow(one, one.Action != DownloadAction.Skip, Ink, Changed));

            Rows.ItemsSource = _rows;

            Changed();
        }

        /// <summary>
        /// Shows the question over <paramref name="owner"/> and answers the plan to run: **the
        /// same plan with exactly what was left ticked taken**, or null when the operator backed
        /// out. Nothing is written by asking.
        /// </summary>
        public static DownloadPlan Ask(Window owner, DownloadPlan plan)
        {
            DownloadWindow window = new DownloadWindow(plan) { Owner = owner };

            return window.ShowDialog() == true ? plan.Taking(window.Taken()) : null;
        }

        /// <summary>The ids of the entries left ticked, in the plan's own order.</summary>
        public List<string> Taken()
        {
            List<string> ids = new List<string>();

            foreach (DownloadRow one in _rows)
                if (one.Take) ids.Add(one.Id);

            return ids;
        }

        private Brush Ink(string key) => TryFindResource(key) as Brush;

        /// <summary>
        /// What the ticks add up to, in the one line that is always on screen - and whether there
        /// is anything to do at all. **Nothing ticked turns the button off** rather than running a
        /// download that writes nothing and then reports it.
        /// </summary>
        private void Changed()
        {
            int taken = 0;
            int overwrites = 0;
            int moves = 0;
            int missing = 0;

            foreach (DownloadRow one in _rows)
            {
                if (one.Take)
                {
                    taken++;

                    if (one.Collides) overwrites++;
                    if (one.Collides && one.Planned.Moves) moves++;
                }
                else if (!one.Collides)
                {
                    missing++;
                }
            }

            string said = string.Format(
                CultureInfo.CurrentCulture, "{0} of {1} ticked - {2} overwrite what the project has",
                taken, _rows.Count, overwrites);

            if (moves > 0)
                said += string.Format(CultureInfo.CurrentCulture, ", {0} of them moved into its family's folder", moves);

            if (missing > 0)
                said += string.Format(CultureInfo.CurrentCulture, "; {0} the project does not have left out", missing);

            StatusText.Text = said + ".";

            if (GoButton != null) GoButton.IsEnabled = taken > 0;
        }

        private void AllClicked(object sender, RoutedEventArgs e) => TickAll(true);

        private void NoneClicked(object sender, RoutedEventArgs e) => TickAll(false);

        private void TickAll(bool take)
        {
            foreach (DownloadRow one in _rows) one.Take = take;
        }

        private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

        private void GoClicked(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
