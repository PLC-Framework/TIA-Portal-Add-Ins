using System.IO;

using AddIn.Shared.Adapters;

using Core;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Opens the window that compares a PLC's program with the core it is built on.
    ///
    /// **On a PLC and nowhere else** (the maintainer's decision, 2026-09-16): a core belongs
    /// to one PLC's software, so an entry on the project root would have to ask which PLC it
    /// meant, and one on a block would offer a whole-PLC operation from a single object.
    ///
    /// **Nothing is handed over.** Every other satellite is told what to work on, because it
    /// cannot ask; this one attaches to TIA Portal itself and finds the project that way.
    /// That removes the handoff, the payload type, and the class of bug where the two ends
    /// disagree about what was selected - and it is only possible because a satellite is an
    /// ordinary process that may reference Openness, where an Add-In is not.
    ///
    /// **Two executables, one per TIA version**, since Openness is a different assembly with
    /// a different public key token in V20 and V21. The version project names its own, which
    /// is the one place that knows.
    /// </summary>
    public static class CoreUpdaterAction
    {
        public const string Title = "Core updater";

        /// <summary>
        /// Not embedded yet: <c>Icons.Get</c> answers null and the menu falls back to an entry
        /// without one. Drop the file into <c>assets\AddIn\</c> and it appears.
        /// </summary>
        public const string IconPath = "AddIn/core-update.ico";

        /// <summary>The executable for one TIA version - <c>V20</c> or <c>V21</c>.</summary>
        public static string ExecutableFor(string tiaVersion) =>
            "PLC-Framework.Satellite.CoreUpdater." + tiaVersion;

        /// <param name="tiaVersion">
        /// <c>V20</c> or <c>V21</c>, supplied by the version project. It is the only thing the
        /// Add-In knows about its own TIA version - **no Siemens API exposes it**, which is
        /// why this is passed rather than asked.
        /// </param>
        public static void Execute(ITiaNotifier notifier, IProcessLauncher launcher, string tiaVersion)
        {
            if (notifier == null || launcher == null) return;

            string path = InstallPaths.Tool(ExecutableFor(tiaVersion) + ".exe");

            // The expected failure, and it deserves a message naming the missing file rather
            // than whatever the process API happens to say.
            if (!File.Exists(path))
            {
                notifier.Error(Title,
                    "\n\nThe core updater is not installed.\n\nExpected at:\n" + path);
                return;
            }

            string error = launcher.Start(path);

            if (error != null)
                notifier.Error(Title, "\n\nThe core updater could not be started.\n\n" + error);
        }
    }
}
