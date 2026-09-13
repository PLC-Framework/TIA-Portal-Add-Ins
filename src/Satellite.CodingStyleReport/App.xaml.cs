using System.Windows;

using Core.Checks;

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
            StyleReport report = HandoffReader.Read(e.Args, out bool handedOver, out string problem);
            new MainWindow(report, handedOver, problem).Show();
        }
    }
}
