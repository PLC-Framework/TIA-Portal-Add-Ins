using System;
using System.Collections.Generic;
using System.Windows;

using Core.Imports;
using Core.Logging;

using Openness.Shared;

namespace Satellite.ImportObjects
{
    /// <summary>
    /// The application, shared by the V20 and V21 executables - the core updater's shape.
    ///
    /// **No `App.xaml`**: an `ApplicationDefinition` generates a `Main`, which would have to live
    /// in one assembly. The window merges the brand dictionaries itself.
    /// </summary>
    public sealed class ImportObjectsApp : Application
    {
        private readonly Func<Log, IImportSession> _connect;
        private readonly Func<string> _whereItLooked;
        private readonly ImportPlace _place;
        private readonly string _placeProblem;
        private readonly bool _anyArguments;
        private readonly string _tiaVersion;

        /// <summary>
        /// Opened before anything else happens: the assembly resolver, the attach and every way
        /// either can fail come before there is a window worth looking at, and they are exactly
        /// what somebody sending a log needs it to contain.
        /// </summary>
        private readonly Log _log = Log.For(LogPaths.ImportObjects);

        private TiaWorker<IImportSession> _worker;

        private ImportObjectsApp(
            Func<Log, IImportSession> connect, Func<string> whereItLooked, string[] arguments, string tiaVersion)
        {
            _connect = connect;
            _whereItLooked = whereItLooked;
            _tiaVersion = tiaVersion;
            _anyArguments = arguments != null && arguments.Length > 0;
            _place = ImportPlace.Parse(arguments, out _placeProblem);
        }

        /// <summary>
        /// Runs the window. <paramref name="connect"/> is a factory rather than a session because
        /// **it is the first thing that touches Openness**: invoked inside the worker's own
        /// thread, "the Siemens assemblies are not on this machine" becomes a sentence in the
        /// window instead of a process that dies before it paints.
        /// </summary>
        /// <param name="whereItLooked">What the assembly resolver searched, shown under a load failure.</param>
        /// <param name="arguments">
        /// What the Add-In wrote: <c>&lt;project&gt; &lt;plc&gt; &lt;unit|*&gt; &lt;tree&gt; [folder ...]</c>,
        /// read by <see cref="ImportPlace.Parse"/> - the same type that wrote it.
        /// </param>
        /// <param name="tiaVersion"><c>V20</c> or <c>V21</c>, shown beside the title.</param>
        public static int Run(
            Func<Log, IImportSession> connect,
            Func<string> whereItLooked = null,
            string[] arguments = null,
            string tiaVersion = null)
        {
            if (connect == null) throw new ArgumentNullException(nameof(connect));

            return new ImportObjectsApp(connect, whereItLooked, arguments, tiaVersion).Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            MainWindow window = new MainWindow();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            base.MainWindow = window;

            window.Records(_log);
            window.Badge(_tiaVersion);

            // Nowhere to import into: started by hand, or arguments that do not say a place. There
            // is nothing to attach for, so nothing is attached.
            if (_place == null)
            {
                _log.Info("started for TIA " + (_tiaVersion ?? "(no version)") + " - " +
                          (_anyArguments ? "with arguments that name no folder: " + _placeProblem : "by hand, with no folder to import into"));

                window.NotLaunched(_placeProblem);
                window.Show();
                return;
            }

            _log.Info("started for TIA " + (_tiaVersion ?? "(no version)") + " - into " + _place +
                      ", project " + (_place.Project ?? "(never saved)"));

            window.ShowWaiting(_place);
            window.Show();

            _worker = new TiaWorker<IImportSession>(
                () => _connect(_log),
                action => Dispatcher.BeginInvoke(action),
                _log);

            window.Uses(_worker);

            // The ancestry is read on the worker's thread: one WMI query over every process, and
            // this line runs before the window has painted.
            _worker.Post(
                session =>
                {
                    IReadOnlyList<int> chain = ParentProcess.Chain(out string unknown);

                    if (unknown != null)
                        _log.Warn("the processes this descends from could not be read, so only the project " +
                                  "says which TIA Portal to attach to - " + unknown);

                    return session.Attach(TiaWanted.Of(_place.Project, chain));
                },
                window.Arrived,
                exception => window.Arrived(TiaAttachment.Failed(exception, Looked())));
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Stops the pump. It does **not** dispose the session: disposing an attached TiaPortal
            // closes TIA Portal. The connection goes when this process does.
            if (_worker != null) _worker.Dispose();

            _log.Dispose();

            base.OnExit(e);
        }

        /// <summary>What the resolver searched, when it has anything to say.</summary>
        private string Looked()
        {
            try
            {
                return _whereItLooked == null ? null : _whereItLooked();
            }
            catch (Exception)
            {
                // A diagnostic that throws must not replace the problem it was explaining.
                return null;
            }
        }
    }
}
