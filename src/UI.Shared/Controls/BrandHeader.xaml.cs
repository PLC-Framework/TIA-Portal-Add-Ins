using System.Windows;
using System.Windows.Controls;

namespace UI.Shared.Controls
{
    /// <summary>
    /// The logo, the window's name and a line under it - the same size and shape in every
    /// satellite that has a form.
    ///
    /// **Extracted because the four of them had drifted into three shapes** (2026-09-16), and
    /// the maintainer asked for one: a 38-pixel logo with an 18-point title in two windows, 34
    /// and 17 in a third, and 28 with a plain-weight title in the fourth. Two of them also
    /// painted the line under the title with a hex literal rather than the named brush - which
    /// is exactly how one "muted grey" becomes two, the trap `Theme.xaml` exists to close.
    ///
    /// **`Satellite.About` is deliberately not a consumer.** It has no form: the logo *is* its
    /// content, at 300 pixels, and squeezing it into a header would be the rule applied
    /// against its own purpose.
    /// </summary>
    public partial class BrandHeader : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(BrandHeader),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(BrandHeader),
                new PropertyMetadata(string.Empty, Shown));

        public static readonly DependencyProperty BadgeProperty =
            DependencyProperty.Register(nameof(Badge), typeof(string), typeof(BrandHeader),
                new PropertyMetadata(string.Empty, Shown));

        public BrandHeader()
        {
            InitializeComponent();

            // Collapsed until something sets them. The property-changed callback does not
            // fire for a value that was never assigned, so a window that sets only the title
            // would otherwise carry an empty badge and an empty line beneath it.
            SubtitleLine.Visibility = Visibility.Collapsed;
            BadgeChrome.Visibility = Visibility.Collapsed;
        }

        /// <summary>What this window is, in two or three words.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>
        /// What it is working on - the project, the file it was given. Hidden while empty, so
        /// a window that has not been told anything yet leaves no gap.
        /// </summary>
        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        /// <summary>
        /// A short mark beside the title - the TIA version, for a window that is built once
        /// per version and otherwise looks identical in both.
        /// </summary>
        public string Badge
        {
            get => (string)GetValue(BadgeProperty);
            set => SetValue(BadgeProperty, value);
        }

        private static void Shown(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            BrandHeader header = sender as BrandHeader;
            if (header == null) return;

            header.SubtitleLine.Visibility = Visible(header.Subtitle);
            header.BadgeChrome.Visibility = Visible(header.Badge);
        }

        private static Visibility Visible(string value) =>
            string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
    }
}
