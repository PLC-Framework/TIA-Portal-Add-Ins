using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

using Core.Checks;

using Satellite.CodingStyleReport.Export;
using Satellite.CodingStyleReport.Handoff;

namespace Satellite.CodingStyleReport
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // No single-instance guard, like the snapshot and unlike the editor. Each run is
            // a report of its own selection at its own moment: a second launch carries a new
            // one, and a single window would either refuse it or throw the first away. Two
            // reports side by side is also exactly how a before-and-after gets compared.
            //
            // The window is created here rather than through StartupUri, which would make WPF
            // open a second one as soon as this method returns.

            // A workbook named on the command line - "Open with", or a shortcut - is imported as
            // if picked with the button. It is not a handoff: nobody in TIA Portal sent it, so the
            // window keeps its Import button, and standard input is not read at all.
            string workbook = e.Args?.FirstOrDefault(argument =>
                !string.IsNullOrWhiteSpace(argument) &&
                argument.EndsWith(ReportFile.Extension, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(argument));

            if (workbook != null)
            {
                MainWindow window = new MainWindow(null, false, null);
                window.Show();
                window.Import(workbook);
                return;
            }

            // Launched by TIA Portal, the window goes up **before** the check runs and says
            // what is being worked on; the report arrives on standard input minutes later, on
            // a thread of its own so this one paints. Nothing on screen for the length of a
            // project-wide check is what made operators think the Add-In had died.
            //
            // **Both halves of the condition are needed.** The notice is the Add-In stating
            // that a report is coming, so a window started any other way never sits waiting
            // for one - including from a script whose own input happens to be redirected, and
            // including an Add-In older than this window, which sends no notice and is served
            // by the path below exactly as it was. The redirection is what makes the promise
            // keepable: without a pipe there is nothing to wait on.
            CheckingNotice notice = CheckingNotice.From(e.Args);

            if (notice != null && HandoffReader.Expected())
            {
                MainWindow waiting = new MainWindow(notice);
                waiting.Show();

                Wait(waiting);
                return;
            }

            StyleReport report = HandoffReader.Read(e.Args, out bool handedOver, out string problem);
            new MainWindow(report, handedOver, problem).Show();
        }

        /// <summary>
        /// Reads the handoff off the UI thread and shows it when it lands.
        ///
        /// A plain background thread rather than a task: this one blocks on a pipe for as
        /// long as TIA takes, and there is nothing to await it.
        /// </summary>
        private static void Wait(MainWindow window)
        {
            Thread reader = new Thread(() =>
            {
                StyleReport report = HandoffReader.Await(out bool arrived, out string problem);

                window.Dispatcher.Invoke(new Action(() => window.Arrived(report, arrived, problem)));
            })
            {
                IsBackground = true,
                Name = "handoff"
            };

            reader.Start();
        }
    }
}
