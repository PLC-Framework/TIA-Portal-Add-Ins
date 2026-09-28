using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Core.Imports;

namespace Satellite.ImportObjects.Overwrite
{
    /// <summary>
    /// Asks which files may overwrite what the PLC already has - **one list, with "Yes to all"
    /// and "No to all"** (the maintainer's decision, 2026-09-28), rather than one question per
    /// object: thirty questions in a row is how a prompt stops being read.
    ///
    /// **Everything starts unticked.** Overwriting is the one thing here TIA will not question,
    /// so it is what has to be asked for; "Yes to all" is one click away for the run where it is
    /// meant. The import itself still goes ahead with whatever is left unticked - a file said no
    /// to is left out, not the whole run.
    /// </summary>
    public partial class OverwriteWindow : Window
    {
        private readonly List<OverwriteRow> _rows = new List<OverwriteRow>();

        public OverwriteWindow(IEnumerable<KeyValuePair<ImportFile, IReadOnlyList<ExistingObject>>> meeting)
        {
            InitializeComponent();

            foreach (KeyValuePair<ImportFile, IReadOnlyList<ExistingObject>> one in meeting)
                _rows.Add(new OverwriteRow(one.Key, one.Value, Ink, Changed));

            Rows.ItemsSource = _rows;

            Changed();
        }

        /// <summary>
        /// Shows the question over <paramref name="owner"/> and answers the files that may
        /// overwrite, or null when the operator backed out of the whole import. Nothing is
        /// written by asking.
        /// </summary>
        public static ISet<ImportFile> Ask(
            Window owner, IEnumerable<KeyValuePair<ImportFile, IReadOnlyList<ExistingObject>>> meeting)
        {
            OverwriteWindow window = new OverwriteWindow(meeting) { Owner = owner };

            return window.ShowDialog() == true ? window.Taken() : null;
        }

        /// <summary>The files left ticked.</summary>
        public ISet<ImportFile> Taken() => new HashSet<ImportFile>(_rows.Where(r => r.Take).Select(r => r.File));

        private Brush Ink(string key) => TryFindResource(key) as Brush;

        /// <summary>What the ticks add up to, in the one line that is always on screen.</summary>
        private void Changed()
        {
            int taken = _rows.Count(r => r.Take);

            StatusText.Text = string.Format(CultureInfo.CurrentCulture,
                "{0} of {1} will overwrite what the PLC has, where it is; {2} left as it is.",
                taken, _rows.Count, _rows.Count - taken);
        }

        private void YesToAllClicked(object sender, RoutedEventArgs e) => TickAll(true);

        private void NoToAllClicked(object sender, RoutedEventArgs e) => TickAll(false);

        private void TickAll(bool take)
        {
            foreach (OverwriteRow one in _rows) one.Take = take;
        }

        private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

        private void GoClicked(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
