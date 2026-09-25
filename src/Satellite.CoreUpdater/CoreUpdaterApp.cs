using System;
using System.Collections.Generic;
using System.Windows;

using Core.Logging;

using Satellite.CoreUpdater.Startup;
using Satellite.CoreUpdater.Tia;

namespace Satellite.CoreUpdater
{
    /// <summary>
    /// The application, shared by the V20 and V21 executables.
    ///
    /// **No `App.xaml`**, and that is what makes one application serve two executables: an
    /// `ApplicationDefinition` generates a `Main`, which would have to live in one assembly.
    ///
    /// **The brand dictionaries are merged by the window, not here.** They were merged into
    /// `Application.Resources` in this constructor until 2026-09-18, which worked at run time
    /// and left the XAML designer with nothing at all to resolve against: Visual Studio builds
    /// its design-time `Application.Resources` out of `App.xaml`, so every `{StaticResource}`
    /// in `MainWindow.xaml` came back as XDG-0001 on a window that compiled and ran. A window
    /// that declares the dictionaries it needs is the pattern
    /// `UI.Shared/Controls/BrandHeader.xaml` already uses, for the same reason one level down -
    /// and it is the more honest place, since knowing which resources one particular window
    /// wants was never the application's business.
    ///
    /// **No single-instance guard yet.** The config editor takes one per TIA Portal because
    /// two windows over one document lose each other's changes, and this window will want the
    /// same rule the moment it can change the project. It cannot yet - it only reads and
    /// writes a map inside `repo\` - and a guard is a decision about what the second launch
    /// should do, worth making when there is something to protect.
    /// </summary>
    public sealed class CoreUpdaterApp : Application
    {
        private readonly Func<ITiaSession> _connect;
        private readonly Func<string> _whereItLooked;
        private readonly Requested _requested;
        private readonly string _tiaVersion;

        /// <summary>
        /// Opened before anything else happens, and that is the whole point of it being here
        /// rather than in the window: the assembly resolver, the attach and every way either
        /// can fail all come before there is a window worth looking at, and they are exactly
        /// what somebody sending a log needs it to contain.
        /// </summary>
        private readonly Log _log = Log.For(LogPaths.CoreUpdater);

        private TiaWorker _worker;

        private CoreUpdaterApp(
            Func<ITiaSession> connect, Func<string> whereItLooked, Requested requested, string tiaVersion)
        {
            _connect = connect;
            _whereItLooked = whereItLooked;
            _requested = requested;
            _tiaVersion = tiaVersion;
        }

        /// <summary>
        /// Runs the window. <paramref name="connect"/> is a factory rather than a session
        /// because **it is the first thing that touches Openness**: invoking it inside the
        /// worker's own thread is what turns "the Siemens assemblies are not on this machine"
        /// into a sentence in the window instead of a process that dies before it paints.
        /// </summary>
        /// <param name="whereItLooked">
        /// What the executable's assembly resolver searched, shown under a load failure.
        /// **The registry layout cannot be checked on a machine without TIA Portal**, so a
        /// resolver that only says "not found" turns every wrong guess into another round
        /// trip to the VM. Optional: a caller with nothing to say passes null.
        /// </param>
        /// <param name="tiaVersion">
        /// <c>V20</c> or <c>V21</c>, shown beside the title. The two executables are otherwise
        /// identical on screen, and an operator with both TIA versions installed has no other
        /// way to tell which one is in front of them.
        /// </param>
        public static int Run(
            Func<ITiaSession> connect,
            Func<string> whereItLooked = null,
            string[] arguments = null,
            string tiaVersion = null)
        {
            if (connect == null) throw new ArgumentNullException(nameof(connect));

            return new CoreUpdaterApp(connect, whereItLooked, Requested.From(arguments), tiaVersion).Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // What the Add-In asked for, before anything has been read. A window that attaches
            // to the wrong TIA Portal, or to none, is answered first of all by what it was told.
            _log.Info("started for TIA " + (_tiaVersion ?? "(no version)") +
                      " - plc " + Said(_requested.Plc) +
                      ", unit " + Said(_requested.Unit) +
                      ", project " + Said(_requested.Project));

            MainWindow window = new MainWindow();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            base.MainWindow = window;

            window.Records(_log);
            window.Badge(_tiaVersion);

            // The project is what the window says it is looking for, because it is what an
            // operator recognises - and, since the bug fix, what actually decides the attach.
            window.ShowWaiting(TiaWanted.Of(_requested.Project, null).ProjectName);
            window.Show();

            _worker = new TiaWorker(_connect, Dispatcher, _log);
            window.Uses(_worker);

            // **The ancestry is read on the worker's thread, not here.** It is one query over
            // every process on the machine, and this line runs between the window being built
            // and being painted - which is where a satellite looks like one that failed to
            // start. Nothing about it is thread-bound.
            _worker.Post(
                session =>
                {
                    IReadOnlyList<int> chain = ParentProcess.Chain(out string unknown);

                    if (unknown != null)
                        _log.Warn("the processes this descends from could not be read, so only the project " +
                                  "says which TIA Portal to attach to - " + unknown);

                    return session.Attach(TiaWanted.Of(_requested.Project, chain));
                },
                attachment => window.Arrived(attachment, _requested.Plc, _requested.Unit),
                exception => window.Arrived(
                    TiaAttachment.Failed(Describe(exception) + Looked()), _requested.Plc, _requested.Unit));
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Stops the pump so its thread can leave. It does **not** dispose the session:
            // disposing an attached TiaPortal closes TIA Portal, which was seen on the VM
            // with an engineer's project open. The connection goes when this process does.
            if (_worker != null) _worker.Dispose();

            // Last, so the closing line counts everything the run said.
            _log.Dispose();

            base.OnExit(e);
        }

        /// <summary>An argument as the log should carry it, blank and absent reading alike.</summary>
        private static string Said(string argument)
        {
            return string.IsNullOrWhiteSpace(argument) ? "(none)" : argument;
        }

        /// <summary>What the resolver searched, when it has anything to say.</summary>
        private string Looked()
        {
            string report = null;

            try
            {
                if (_whereItLooked != null) report = _whereItLooked();
            }
            catch (Exception)
            {
                // A diagnostic that throws must not replace the problem it was explaining.
            }

            return string.IsNullOrWhiteSpace(report) ? string.Empty : "\n\n" + report;
        }

        /// <summary>
        /// The two failures worth telling apart by name, because the answers differ: the
        /// assemblies are not there at all, or they are and the attach was refused.
        /// </summary>
        private static string Describe(Exception exception)
        {
            if (exception is System.IO.FileNotFoundException || exception is System.IO.FileLoadException)
                return "The TIA Openness assemblies could not be loaded, so this cannot talk to " +
                       "TIA Portal on this machine.\n\n" + exception.Message;

            if (exception is UnauthorizedAccessException)
                return "TIA Portal refused the connection. The Windows user has to belong to the " +
                       "local \"Siemens TIA Openness\" group, and the membership is read at logon.\n\n" +
                       exception.Message;

            return "TIA Portal could not be attached to.\n\n" + exception.Message;
        }

        private static ResourceDictionary Dictionary(string name) =>
            new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/PLC-Framework.UI.Shared;component/Resources/" + name,
                    UriKind.Absolute)
            };
    }
}
