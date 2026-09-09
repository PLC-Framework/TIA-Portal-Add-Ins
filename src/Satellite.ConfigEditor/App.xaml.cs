using System.Windows;

using Satellite.ConfigEditor.Document;
using Satellite.ConfigEditor.Handoff;
using Satellite.ConfigEditor.Startup;

using UI.Shared;

namespace Satellite.ConfigEditor
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            EditorRequest request = HandoffReader.Read(e.Args);

            // One editor per TIA Portal - and the tie is worked out here rather than taken
            // from the handoff, because the Add-In cannot read its own process id under
            // partial trust. Started by hand there is no TIA parent, and the guard falls
            // back to one editor per file, which is the half that actually prevents damage:
            // two windows over one config.json lose each other's changes in silence.
            // The same resolution the window uses - see ConfigLocation - so two windows
            // over one file cannot each conclude they are alone.
            string key = ParentProcess.InstanceKey(ConfigLocation.Resolve(request.ProjectDirectory));

            if (!SingleInstance.Claim("ConfigEditor." + key))
            {
                Shutdown();
                return;
            }

            // No StartupUri in App.xaml: leaving it there makes WPF create a second window
            // once this returns, which looks exactly like a broken guard and is not.
            new MainWindow(request).Show();
        }
    }
}
