using System;
using System.IO;
using System.Linq;
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

            StyleReport report = HandoffReader.Read(e.Args, out bool handedOver, out string problem);
            new MainWindow(report, handedOver, problem).Show();
        }
    }
}
