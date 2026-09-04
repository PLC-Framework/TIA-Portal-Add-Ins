using System.Windows;

using Satellite.DataBlockSnapshot.Handoff;

namespace Satellite.DataBlockSnapshot
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // No single-instance guard here, unlike the other satellites. Each run is a
            // job with its own PLC, blocks and destination: a second launch carries a new
            // selection, and one instance would either refuse it or throw it away.
            //
            // The window is created here rather than through StartupUri, which would make
            // WPF open a second one as soon as this method returns.
            SnapshotRequest request = HandoffReader.Read(e.Args);
            new MainWindow(request).Show();
        }
    }
}
