using System.IO;

using AddIn.Shared.Adapters;

using Core;
using Core.Imports;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Opens the window that imports files into the folder that was right-clicked.
    ///
    /// **On object folders only** - blocks, PLC data types, tag tables and technology objects,
    /// their roots included (the maintainer's decision, 2026-09-28) - **and the folder clicked is
    /// where the files go.** Not on a PLC or a unit, where there is no one tree to import into,
    /// and not on the project root, where there is no folder at all.
    ///
    /// **The Add-In only launches it.** The satellite attaches to TIA Portal through Openness and
    /// imports there, so all this hands over is where: the place as `Core.Imports.ImportPlace`
    /// writes it on the command line, which the satellite reads back with the same type. No
    /// payload and no handoff - the core updater's shape.
    ///
    /// **Two executables, one per TIA version**, for the core updater's reason: Openness is a
    /// different assembly with a different public key token in V20 and V21. The version project
    /// names its own, which is the one place that knows.
    /// </summary>
    public static class ImportObjectsAction
    {
        public const string Title = "Import objects";

        /// <summary>
        /// Not in <c>assets\AddIn\</c> yet: <c>Icons.Get</c> answers null and the entry shows
        /// without one. The icons are made elsewhere and dropped in; this names the file.
        /// </summary>
        public const string IconPath = "AddIn/import-objects.ico";

        /// <summary>The executable for one TIA version - <c>V20</c> or <c>V21</c>.</summary>
        public static string ExecutableFor(string tiaVersion) =>
            "PLC-Framework.Satellite.ImportObjects." + tiaVersion;

        /// <param name="tiaVersion"><c>V20</c> or <c>V21</c>, supplied by the version project.</param>
        /// <param name="selected">
        /// How many folders the click was on. **One, or nothing is launched**: the destination is
        /// the folder clicked, and with several selected TIA does not say which one that was.
        /// </param>
        /// <param name="place">
        /// Where the files go, as the version project read it off that folder - or null, with
        /// <paramref name="problem"/> saying why.
        /// </param>
        public static void Execute(
            ITiaNotifier notifier, IProcessLauncher launcher, string tiaVersion,
            int selected, ImportPlace place, string problem)
        {
            if (notifier == null || launcher == null) return;

            if (selected != 1)
            {
                notifier.Info(Title,
                    selected == 0
                        ? "\n\nNothing is selected.\n\nRight-click the folder the files should go into."
                        : "\n\nObjects are imported into one folder, and " + selected + " are selected.\n\n" +
                          "Right-click the one folder the files should go into.");
                return;
            }

            if (place == null)
            {
                notifier.Info(Title, "\n\n" + (string.IsNullOrWhiteSpace(problem)
                    ? "This folder cannot be imported into."
                    : problem));
                return;
            }

            string path = InstallPaths.Tool(ExecutableFor(tiaVersion) + ".exe");

            if (string.IsNullOrEmpty(path))
            {
                notifier.Error(Title,
                    "\n\nThe installation folder could not be resolved.\n\n" +
                    "Set " + InstallPaths.RootOverrideVariable + " or reinstall the framework.");
                return;
            }

            // The expected failure, and it deserves a message naming the missing file rather
            // than whatever the process API happens to say.
            if (!File.Exists(path))
            {
                notifier.Error(Title, "\n\nThe import window is not installed.\n\nExpected at:\n" + path);
                return;
            }

            // Its input is redirected and closed with nothing in it, as the core updater's is:
            // the window reads nothing from it, and a pipe closed at once is one nobody waits on.
            string error = launcher.Start(path, place.ToCommandLine(), null);

            if (error != null)
                notifier.Error(Title, "\n\nThe import window could not be started.\n\n" + error);
        }
    }
}
