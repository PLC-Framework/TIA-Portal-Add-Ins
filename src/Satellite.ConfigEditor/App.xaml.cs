using System.Windows;

using Core.Logging;

using Satellite.ConfigEditor.Document;
using Satellite.ConfigEditor.Handoff;
using Satellite.ConfigEditor.Startup;

using UI.Shared;

namespace Satellite.ConfigEditor
{
    public partial class App : Application
    {
        /// <summary>
        /// Opened first, so the handoff and a window that gives way to another are recorded
        /// too - and closed last, so its closing line counts everything the run said.
        /// </summary>
        private readonly Log _log = Log.For(LogPaths.ConfigEditor);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // What escapes every handler, with its stack, before WPF reports it. Not handled
            // here: recording a crash is not a reason to pretend it did not happen.
            DispatcherUnhandledException += (sender, args) => _log.Failed("unhandled", args.Exception);

            EditorRequest request = HandoffReader.Read(e.Args);

            _log.Info("started - project " + Said(request.ProjectName) + ", folder " + Said(request.ProjectDirectory));

            if (request.Problem != null) _log.Warn(request.Problem);

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
                _log.Info("another editor already has this, so this one gave way");
                Shutdown();
                return;
            }

            // No StartupUri in App.xaml: leaving it there makes WPF create a second window
            // once this returns, which looks exactly like a broken guard and is not.
            new MainWindow(request, _log).Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _log.Dispose();

            base.OnExit(e);
        }

        /// <summary>An argument as the log should carry it, blank and absent reading alike.</summary>
        private static string Said(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }
    }
}
