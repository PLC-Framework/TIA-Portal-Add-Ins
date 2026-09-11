using System.IO;

using AddIn.Shared.Adapters;

using Core;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Opens the About window.
    ///
    /// That window is a satellite: a separate executable in the framework's install folder,
    /// because a .addin package cannot carry an .exe and TIA loads its parts as assemblies
    /// rather than as files on disk. So this action resolves a path and launches it.
    /// </summary>
    public static class AboutAction
    {
        public const string Title = "About us";

        /// <summary>
        /// Path into the assets\ folder, as "Feature/file.ico". The action names the icon;
        /// the version-specific adapter is what turns it into a real image.
        /// </summary>
        public const string IconPath = "Brand/favicon.ico";

        /// <summary>
        /// File name inside <see cref="InstallPaths.Root"/>. It is the AssemblyName of the
        /// Satellite.About project, so renaming that project breaks this at runtime rather
        /// than at compile time - the two live in different solutions' worth of layers.
        /// </summary>
        public const string ExecutableName = "PLC-Framework.Satellite.About.exe";

        public static void Execute(ITiaNotifier notifier, IProcessLauncher launcher)
        {
            if (notifier == null || launcher == null) return;

            string path = InstallPaths.Tool(ExecutableName);

            if (string.IsNullOrEmpty(path))
            {
                notifier.Error(Title,
                    "\n\nThe installation folder could not be resolved.\n\n" +
                    $"Set {InstallPaths.RootOverrideVariable} or reinstall the framework.");
                return;
            }

            // Checked here rather than left to the launcher: "not installed" is the
            // expected failure, and it is worth a message that names the missing path
            // instead of whatever the process API happens to say.
            if (!File.Exists(path))
            {
                notifier.Error(Title,
                    $"\n\n{ExecutableName} is not installed.\n\nExpected at:\n{path}");
                return;
            }

            string error = launcher.Start(path);

            if (error != null)
                notifier.Error(Title, $"\n\n{ExecutableName} could not be started.\n\n{error}");
        }
    }
}
