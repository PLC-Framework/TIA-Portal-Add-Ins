using System.Windows;
using System.Windows.Controls;

namespace UI.Shared.Controls
{
    /// <summary>
    /// The line along the bottom of a window that says what just happened — the same height,
    /// the same ink and the same place in every satellite that has a form.
    ///
    /// **Extracted because the four of them had drifted into three margins and two inks**
    /// (2026-09-18, the maintainer asking for one look across every window): 12 pixels above it
    /// in one, 14 in another, 16 in a third, and that third one painting it `InkMuted` where
    /// the rest used `Ink`. It is the same drift `BrandHeader` was pulled out of two days
    /// earlier, at the other end of the same windows.
    ///
    /// **It is there whether there is a status or not.** A bar that appears with its first
    /// message moves everything above it, and a window that resizes itself as it works reads as
    /// one that cannot make up its mind. `MinHeight` is what reserves it.
    ///
    /// **Trimmed with the whole of it on hover, never wrapped**, for the same reason: two lines
    /// of status is a window that changes shape. Long sentences do arrive here — a comparison
    /// that could not run says why — and this framework already answers "longer than the space
    /// it has" with a tooltip.
    /// </summary>
    public partial class StatusBar : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBar),
                new PropertyMetadata(string.Empty, Said));

        public StatusBar()
        {
            InitializeComponent();
        }

        /// <summary>What the window has to say, or an empty string.</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        private static void Said(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            StatusBar bar = sender as StatusBar;

            if (bar == null) return;

            string said = bar.Text ?? string.Empty;

            bar.Line.Text = said;

            // An empty tooltip would still open a small blank box under the pointer, which the
            // coding-style report already learned about its own cells.
            bar.Line.ToolTip = said.Length == 0 ? null : said;
        }
    }
}
