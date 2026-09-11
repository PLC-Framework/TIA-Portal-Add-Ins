using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

using AddIn.Shared.Adapters;

using Core;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Opens the configuration editor on the project that is currently open.
    ///
    /// The Add-In reads nothing and writes nothing here: it knows which project is open,
    /// the editor knows the file format, and the two meet over a small JSON document
    /// written into the editor's standard input.
    ///
    /// **No check that config.json exists.** Its absence is a normal state the editor
    /// handles - it offers to create one from the template - so refusing to open would
    /// block the very case the editor is most useful for.
    /// </summary>
    public static class ConfigEditorAction
    {
        public const string Title = "Config. Editor";

        /// <summary>
        /// File name inside <see cref="InstallPaths.Root"/>. It is the AssemblyName of the
        /// Satellite.ConfigEditor project, and nothing binds the two at compile time - they
        /// sit in different layers on purpose - so renaming that project breaks this at run
        /// time instead.
        /// </summary>
        public const string ExecutableName = "PLC-Framework.Satellite.ConfigEditor.exe";

        public static void Execute(
            ITiaNotifier notifier,
            IProcessLauncher launcher,
            string projectDirectory,
            string projectName)
        {
            if (notifier == null || launcher == null) return;

            // Without a project there is no .plc-framework to edit. The editor can be
            // pointed at one by hand, but launching it from a menu that belongs to a
            // project and getting an empty window would just look broken.
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                notifier.Warning(Title, "\n\nNo project is open.");
                return;
            }

            string path = InstallPaths.Tool(ExecutableName);

            if (string.IsNullOrEmpty(path))
            {
                notifier.Error(Title,
                    "\n\nThe installation folder could not be resolved.\n\n" +
                    $"Set {InstallPaths.RootOverrideVariable} or reinstall the framework.");
                return;
            }

            // Checked here rather than left to the launcher: "not installed" is the
            // expected failure, and it deserves a message that names the missing path.
            if (!File.Exists(path))
            {
                notifier.Error(Title,
                    $"\n\n{ExecutableName} is not installed.\n\nExpected at:\n{path}");
                return;
            }

            string error = launcher.Start(path, Payload(projectDirectory, projectName));

            if (error != null)
                notifier.Error(Title, $"\n\n{ExecutableName} could not be started.\n\n{error}");
        }

        /// <summary>
        /// The handoff document.
        ///
        /// Built with DataContractJsonSerializer rather than by hand: a project directory
        /// is full of backslashes, which is exactly what a hand-rolled writer gets wrong.
        /// It also keeps the Add-In free of the JSON package the editor uses, which must
        /// never load inside TIA's process.
        ///
        /// Public so it can be exercised on its own, including inside a restricted
        /// AppDomain - TIA runs Add-Ins in partial trust, and that is not something to find
        /// out about from a crash report.
        /// </summary>
        public static string Payload(string projectDirectory, string projectName)
        {
            ConfigEditorPayload payload = new ConfigEditorPayload
            {
                ProjectDirectory = projectDirectory,
                ProjectName = projectName
            };

            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(ConfigEditorPayload));

            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, payload);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
