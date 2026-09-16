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
    /// **Two arguments, and no handoff.** Every other satellite is told what to work on in a
    /// JSON document, because it cannot ask; this one attaches to TIA Portal itself and finds
    /// the project that way. All it needs telling is which part of the project the operator
    /// was pointing at - the PLC that was right-clicked, and the software unit, which from a
    /// PLC-level entry is always the general program. Two arguments are not a handoff: there
    /// is no document, no payload type, and no pair of ends that can disagree about what was
    /// selected. The window offers the PLC's units so the star is a starting point rather
    /// than a decision.
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
        /// <param name="plc">The PLC the entry was clicked on. Nothing is launched without one.</param>
        public static void Execute(ITiaNotifier notifier, IProcessLauncher launcher, string tiaVersion, string plc)
        {
            if (notifier == null || launcher == null) return;

            // The entry is registered on every DeviceItem, and most of them are not a CPU -
            // a rack, a power supply, an interface module. Saying so is better than opening a
            // window that then reports a PLC it cannot find.
            if (string.IsNullOrWhiteSpace(plc))
            {
                notifier.Info(Title,
                    "\n\nThis is not a PLC.\n\n" +
                    "The core belongs to a PLC's software, so run this on the controller itself.");
                return;
            }

            string path = InstallPaths.Tool(ExecutableFor(tiaVersion) + ".exe");

            // The expected failure, and it deserves a message naming the missing file rather
            // than whatever the process API happens to say.
            if (!File.Exists(path))
            {
                notifier.Error(Title,
                    "\n\nThe core updater is not installed.\n\nExpected at:\n" + path);
                return;
            }

            string error = launcher.Start(path, Arguments(plc), null);

            if (error != null)
                notifier.Error(Title, "\n\nThe core updater could not be started.\n\n" + error);
        }

        /// <summary>
        /// The command line: the PLC, and the general program.
        ///
        /// **Quoted, because a PLC's name may hold a space** - and a quoted argument must not
        /// end in a backslash, which is why the name is trimmed of one. That trap is measured
        /// and recorded in the Openness notes; a name ending in `\` would swallow the argument
        /// after it.
        /// </summary>
        public static string Arguments(string plc) =>
            "\"" + (plc ?? string.Empty).Trim().TrimEnd('\\') + "\" \"" + Places.GeneralProgram + "\"";
    }
}
