using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

using Core.Checks;
using Core.Logging;

using Satellite.CodingStyleReport.Export;
using Satellite.CodingStyleReport.Handoff;

namespace Satellite.CodingStyleReport
{
    public partial class App : Application
    {
        /// <summary>
        /// Opened first and closed last. **What TIA Portal handed over is recorded here**, before
        /// any window reads it: a report that could not be read, and a check that broke off with
        /// nothing sent, are the two endings somebody will ask about afterwards, and both used to
        /// exist only as a sentence in a window that has since been closed.
        /// </summary>
        private readonly Log _log = Log.For(LogPaths.CodingStyleReport);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // What escapes every handler, with its stack, before WPF reports it. Not handled
            // here: recording a crash is not a reason to pretend it did not happen.
            DispatcherUnhandledException += (sender, args) => _log.Failed("unhandled", args.Exception);

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
                _log.Info("started to import " + workbook);

                MainWindow window = new MainWindow(null, false, null);
                window.Records(_log);
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
                _log.About(notice.Project, null);
                _log.Info("waiting for the check of " + (notice.Scope ?? "(no scope)"));

                MainWindow waiting = new MainWindow(notice);
                waiting.Records(_log);
                waiting.Show();

                Wait(waiting, _log);
                return;
            }

            StyleReport report = HandoffReader.Read(e.Args, out bool handedOver, out string problem);

            Received(_log, report, handedOver, problem);

            MainWindow shown = new MainWindow(report, handedOver, problem);
            shown.Records(_log);
            shown.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _log.Dispose();

            base.OnExit(e);
        }

        /// <summary>
        /// Reads the handoff off the UI thread and shows it when it lands.
        ///
        /// A plain background thread rather than a task: this one blocks on a pipe for as
        /// long as TIA takes, and there is nothing to await it.
        /// </summary>
        private static void Wait(MainWindow window, Log log)
        {
            Thread reader = new Thread(() =>
            {
                StyleReport report = HandoffReader.Await(out bool arrived, out string problem);

                // On this thread, as soon as the pipe answers: the log takes its own lock, and a
                // window that fails to show the report must not also lose the record of it.
                //
                // Nothing at all is its own sentence here, where on the direct path it is a
                // window started by hand: the Add-In promised a report, so an input closed
                // with nothing in it is the check having broken off.
                if (arrived)
                    Received(log, report, true, problem);
                else
                    log.Warn("the input closed with nothing in it - the check did not finish");

                window.Dispatcher.Invoke(new Action(() => window.Arrived(report, arrived, problem)));
            })
            {
                IsBackground = true,
                Name = "handoff"
            };

            reader.Start();
        }

        /// <summary>
        /// What a handoff came to, each in its own words: a report, something that was not one,
        /// and nothing - which, read at startup rather than waited for, is a window started by
        /// hand. <see cref="Wait"/> says the waiting path's own "nothing" itself.
        /// </summary>
        private static void Received(Log log, StyleReport report, bool handedOver, string problem)
        {
            if (report != null)
            {
                log.About(report.Project, report.ProjectDirectory);
                // Qualified: inside an Application, a bare MainWindow is the property, not the type.
                log.Info("report received - " + global::Satellite.CodingStyleReport.MainWindow.Summarised(report));
            }
            else if (handedOver)
            {
                log.Warn("handed something that is not a report - " + (problem ?? "no reason given"));
            }
            else
            {
                log.Info("started with no report");
            }
        }
    }
}
