using System.IO;

using AddIn.Shared.Adapters;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Opens the folder the TIA project lives in.
    ///
    /// **The folder, not `.plc-framework`.** From the project folder the configuration, the
    /// exports and `.version-control\` are all one step away; from inside `.plc-framework`
    /// the project itself is one step back, and that is the direction nobody wants to walk.
    ///
    /// It is on the project root and nowhere else: the same folder would open from a block,
    /// from a PLC and from a tag table, so an entry there would only suggest otherwise.
    /// </summary>
    public static class OpenProjectFolderAction
    {
        public const string Title = "Open project folder";

        /// <summary>
        /// Not embedded yet, and that is not a bug: <c>Icons.Get</c> answers null and the menu
        /// falls back to an entry without one. Drop the file into <c>assets\AddIn\</c> and it
        /// appears - assets are embedded by a glob.
        /// </summary>
        public const string IconPath = "AddIn/open-folder.ico";

        public static void Execute(ITiaNotifier notifier, IProcessLauncher launcher, string projectDirectory)
        {
            if (notifier == null || launcher == null) return;

            // A project that has never been saved has no folder at all. TIA allows that, and
            // it is the one case where there is nothing wrong and nothing to open.
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                notifier.Info(Title,
                    "\n\nThis project has no folder yet.\n\n" +
                    "It has not been saved, so there is nothing on disk to open. Save it and try again.");
                return;
            }

            // Moved, renamed, or on a drive that is not mapped today: naming the path is what
            // lets the operator tell those apart.
            if (!Exists(projectDirectory))
            {
                notifier.Error(Title,
                    $"\n\nThe project folder could not be found.\n\nExpected at:\n{projectDirectory}");
                return;
            }

            string error = launcher.Browse(projectDirectory);

            if (error != null)
                notifier.Error(Title, $"\n\nThe folder could not be opened.\n\n{projectDirectory}\n\n{error}");
        }

        /// <summary>
        /// A path that cannot even be looked at - a network share that is gone, a name the
        /// file system refuses - answers the same as one that is not there: there is nothing
        /// to open either way, and the message names the path.
        /// </summary>
        private static bool Exists(string folder)
        {
            try
            {
                return Directory.Exists(folder);
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
