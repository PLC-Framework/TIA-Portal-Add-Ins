using System;
using System.Windows;

using Satellite.CoreUpdater.Startup;
using Satellite.CoreUpdater.Tia;

namespace Satellite.CoreUpdater
{
    /// <summary>
    /// The application, shared by the V20 and V21 executables.
    ///
    /// **No `App.xaml`**, and that is what makes one application serve two executables: an
    /// `ApplicationDefinition` generates a `Main`, which would have to live in one assembly.
    /// The dictionaries are merged here instead, by the same pack URIs - which carry the
    /// *assembly* name, `PLC-Framework.UI.Shared`, not the project name.
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

        private TiaWorker _worker;

        private CoreUpdaterApp(
            Func<ITiaSession> connect, Func<string> whereItLooked, Requested requested, string tiaVersion)
        {
            _connect = connect;
            _whereItLooked = whereItLooked;
            _requested = requested;
            _tiaVersion = tiaVersion;

            Resources.MergedDictionaries.Add(Dictionary("Controls.xaml"));
            Resources.MergedDictionaries.Add(Dictionary("BrandLogo.xaml"));
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

            MainWindow window = new MainWindow();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            base.MainWindow = window;

            window.Badge(_tiaVersion);

            // Asked on this thread before the window shows, so the window can say which TIA
            // Portal it is looking for rather than only that it is looking.
            int? parent = ParentProcess.Id();

            window.ShowWaiting(parent);
            window.Show();

            _worker = new TiaWorker(_connect, Dispatcher);
            window.Uses(_worker);

            _worker.Post(
                session => session.Attach(parent),
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

            base.OnExit(e);
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
