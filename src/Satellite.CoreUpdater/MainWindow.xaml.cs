using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

using Core;

using Satellite.CoreUpdater.Tia;

namespace Satellite.CoreUpdater
{
    /// <summary>
    /// Says which TIA Portal this window is attached to, and which project.
    ///
    /// **It opens before the attach finishes**, which is the lesson the coding-style report
    /// already paid for: work that takes a noticeable moment behind a window that is not
    /// there yet reads as an application that failed to start. Attaching is normally quick,
    /// but reading this process's parent costs ~170 ms on its own and TIA can take longer to
    /// answer when it is busy.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Title = Product.Title + " - Core updater";
            StatusText.Text = "Starting…";
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

        /// <summary>The answer, whichever of the three it is.</summary>
        public void Arrived(TiaAttachment attachment)
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

            AttachedPanel.Visibility = Visibility.Visible;

            ProjectNameText.Text = string.IsNullOrWhiteSpace(attachment.ProjectName)
                ? "(no project open)"
                : attachment.ProjectName;

            // A project that was never saved is attached and still unusable here: a core is
            // copied into the project's own folder, and there is no folder to copy into.
            ProjectFolderText.Text = attachment.HasProjectFolder
                ? attachment.ProjectDirectory
                : "This project has not been saved, so it has no folder yet.";

            ProcessText.Text = string.Format(
                CultureInfo.CurrentCulture, "TIA Portal process {0}", attachment.ProcessId);

            StatusText.Text = attachment.HasProjectFolder
                ? "Ready."
                : "Save the project and open this window again.";
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
