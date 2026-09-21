using System.IO;

using AddIn.Shared.Adapters;

using Core;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Opens the window that compares a PLC's program with the core it is built on.
    ///
    /// **On a PLC, and on the project root** (2026-09-21, the maintainer's decision, widening
    /// his own of 2026-09-16). The reason the entry was kept off the root was that a core
    /// belongs to one PLC's software, so an entry there would have to ask which PLC it meant -
    /// and the window had no good moment to ask, because it read the project as soon as it
    /// opened. It does not any more: **nothing is read until *Load* is pressed**, so a window
    /// opened from the root comes up with the project's PLCs in a drop-down and waits, which
    /// is the asking. An entry on a block is still refused - that would offer a whole-PLC
    /// operation from a single object.
    ///
    /// **Three arguments, and no handoff.** Every other satellite is told what to work on in a
    /// JSON document, because it cannot ask; this one attaches to TIA Portal itself and finds
    /// the project that way. All it needs telling is which part of the project the operator
    /// was pointing at - the PLC that was right-clicked, and the software unit, which from a
    /// PLC-level entry is always the general program - and **which TIA Portal it belongs to**.
    /// Three arguments are not a handoff: there is no document, no payload type, and no pair
    /// of ends that can disagree about what was selected. The window offers the PLC's units so
    /// the star is a starting point rather than a decision.
    ///
    /// **The project path was added to fix a bug** (2026-09-16). The satellite used to
    /// recognise its TIA Portal by the process that launched it, and with two instances open
    /// that matched nothing in either TIA version: the process a satellite is started from is
    /// not one of the processes Openness lists. The project is the one thing both ends name
    /// identically - the Add-In is *in* a project, and `TiaPortalProcess.ProjectPath` says
    /// what each running instance has open.
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
        public const string IconPath = "AddIn/core-updater.ico";

        /// <summary>The executable for one TIA version - <c>V20</c> or <c>V21</c>.</summary>
        public static string ExecutableFor(string tiaVersion) =>
            "PLC-Framework.Satellite.CoreUpdater." + tiaVersion;

        /// <param name="tiaVersion">
        /// <c>V20</c> or <c>V21</c>, supplied by the version project. It is the only thing the
        /// Add-In knows about its own TIA version - **no Siemens API exposes it**, which is
        /// why this is passed rather than asked.
        /// </param>
        /// <param name="plc">The PLC the entry was clicked on. Nothing is launched without one.</param>
        /// <param name="projectFile">
        /// The open project's own file - <c>…\LabSlave.ap21</c> - which is how the window picks
        /// the right TIA Portal out of several. Null is allowed and costs only that: the window
        /// falls back to its ancestry, then to asking the operator.
        /// </param>
        public static void Execute(
            ITiaNotifier notifier, IProcessLauncher launcher, string tiaVersion, string plc, string projectFile)
        {
            if (notifier == null || launcher == null) return;

            // The entry is registered on every DeviceItem, and most of them are not a CPU -
            // a rack, a power supply, an interface module. Saying so is better than opening a
            // window that then reports a PLC it cannot find.
            //
            // **The project root is the other way round**, and goes through ExecuteForProject:
            // there nobody pointed at a PLC, which is a different thing from pointing at
            // something that is not one.
            if (string.IsNullOrWhiteSpace(plc))
            {
                notifier.Info(Title,
                    "\n\nThis is not a PLC.\n\n" +
                    "The core belongs to a PLC's software, so run this on the controller itself, " +
                    "or on the project to pick one in the window.");
                return;
            }

            Start(notifier, launcher, tiaVersion, plc, projectFile);
        }

        /// <summary>
        /// The same window, opened from the project root with **no PLC named**.
        ///
        /// A core belongs to one PLC's software, so something has to choose one — and the
        /// window is where that is done now: it lists the project's PLCs, counts what the
        /// chosen one holds, and reads nothing until *Load* is pressed. Naming a PLC here
        /// would be the Add-In guessing at what the operator did not say.
        /// </summary>
        public static void ExecuteForProject(
            ITiaNotifier notifier, IProcessLauncher launcher, string tiaVersion, string projectFile)
        {
            if (notifier == null || launcher == null) return;

            Start(notifier, launcher, tiaVersion, null, projectFile);
        }

        private static void Start(
            ITiaNotifier notifier, IProcessLauncher launcher, string tiaVersion, string plc, string projectFile)
        {
            string path = InstallPaths.Tool(ExecutableFor(tiaVersion) + ".exe");

            // The expected failure, and it deserves a message naming the missing file rather
            // than whatever the process API happens to say.
            if (!File.Exists(path))
            {
                notifier.Error(Title,
                    "\n\nThe core updater is not installed.\n\nExpected at:\n" + path);
                return;
            }

            string error = launcher.Start(path, Arguments(plc, projectFile), null);

            if (error != null)
                notifier.Error(Title, "\n\nThe core updater could not be started.\n\n" + error);
        }

        /// <summary>
        /// The command line: the PLC, the general program, and the project file.
        ///
        /// **An empty first argument is what the project root sends**, and the window reads it
        /// as "nobody said": it offers the project's PLCs and takes the first as a starting
        /// point. The positions stay fixed whatever is known, which is the same rule the third
        /// argument already follows.
        ///
        /// **Quoted, because a PLC's name and a path both hold spaces** - and a quoted argument
        /// must not end in a backslash, which is why each is trimmed of one. That trap is
        /// measured and recorded in the Openness notes; a value ending in `\` would swallow the
        /// argument after it. A project file never ends in a separator, but the rule is applied
        /// to every argument rather than to the ones somebody remembered.
        ///
        /// **A project that was never saved sends an empty third argument**, not a missing one.
        /// The satellite reads blank and absent as the same thing, so the positions stay fixed
        /// whatever is known.
        /// </summary>
        public static string Arguments(string plc, string projectFile) =>
            Quoted(plc) + " " + Quoted(Places.GeneralProgram) + " " + Quoted(projectFile);

        private static string Quoted(string value) =>
            "\"" + (value ?? string.Empty).Trim().TrimEnd('\\') + "\"";
    }
}
