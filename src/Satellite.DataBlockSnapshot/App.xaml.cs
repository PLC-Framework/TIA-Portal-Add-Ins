using System;
using System.IO;
using System.Windows;

using Core.Logging;

using Satellite.DataBlockSnapshot.Handoff;

namespace Satellite.DataBlockSnapshot
{
    public partial class App : Application
    {
        /// <summary>
        /// Opened first and closed last, so the handoff is on record before the window reads it.
        /// **This is the one satellite that holds a password**, and nothing hands it to the log:
        /// the window and the runner name a CPU by its address and its user, and the log's own
        /// masking is there for what nobody meant to write rather than as the plan.
        /// </summary>
        private readonly Log _log = Log.For(LogPaths.DataBlockSnapshot);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // What escapes every handler, with its stack, before WPF reports it. Not handled
            // here: recording a crash is not a reason to pretend it did not happen.
            DispatcherUnhandledException += (sender, args) => _log.Failed("unhandled", args.Exception);

            // No single-instance guard here, unlike the other satellites. Each run is a
            // job with its own PLC, blocks and destination: a second launch carries a new
            // selection, and one instance would either refuse it or throw it away.
            //
            // The window is created here rather than through StartupUri, which would make
            // WPF open a second one as soon as this method returns.
            SnapshotRequest request = HandoffReader.Read(e.Args);

            if (!string.IsNullOrWhiteSpace(request.ProjectDirectory))
                _log.About(NameOf(request.ProjectDirectory), request.ProjectDirectory);

            _log.Info("started - plc " + Said(request.PlcName) +
                      ", addresses " + (request.Addresses.Count == 0 ? "(none)" : string.Join(", ", request.Addresses)) +
                      ", " + request.DataBlocks.Count + " data blocks");

            new MainWindow(request, _log).Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _log.Dispose();

            base.OnExit(e);
        }

        /// <summary>
        /// The project's name for the log's column, which the handoff does not carry: a TIA
        /// project's folder is named after it, and the folder is what arrives.
        /// </summary>
        private static string NameOf(string directory)
        {
            try
            {
                return Path.GetFileName(directory.TrimEnd('\\', '/'));
            }
            catch (ArgumentException)
            {
                return directory;
            }
        }

        /// <summary>An argument as the log should carry it, blank and absent reading alike.</summary>
        private static string Said(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
        }
    }
}
