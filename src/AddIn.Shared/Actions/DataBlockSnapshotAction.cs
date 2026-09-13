using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

using AddIn.Shared.Adapters;
using AddIn.Shared.Handoff;
using Core;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Hands the selected data blocks to the snapshot satellite.
    ///
    /// The Add-In does no reading of its own: it knows the project and the selection,
    /// the satellite knows how to talk to the CPU, and the two meet over a small JSON
    /// document written into the satellite's standard input.
    /// </summary>
    public static class DataBlockSnapshotAction
    {
        public const string Title = "Snapshot data blocks";
        public const string IconPath = "AddIn/datablock-snapshot.ico";

        /// <summary>
        /// File name inside <see cref="InstallPaths.Root"/>. It is the AssemblyName of
        /// the Satellite.DataBlockSnapshot project.
        /// </summary>
        public const string ExecutableName = "PLC-Framework.Satellite.DataBlockSnapshot.exe";

        public static void Execute(
            ITiaNotifier notifier,
            IProcessLauncher launcher,
            string projectDirectory,
            PlcSelection selection)
        {
            if (notifier == null || launcher == null) return;

            if (selection == null || selection.DataBlocks.Count == 0)
            {
                notifier.Warning(Title, "\n\nNo data block was selected.");
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

            string error = launcher.Start(path, Payload(projectDirectory, selection));

            if (error != null)
                notifier.Error(Title, $"\n\n{ExecutableName} could not be started.\n\n{error}");
        }

        /// <summary>
        /// The handoff document.
        ///
        /// Built with DataContractJsonSerializer rather than by hand: the payload carries
        /// Windows paths, and a project directory is full of backslashes - exactly what a
        /// hand-rolled writer gets wrong. This also keeps the Add-In free of the JSON
        /// package the satellite uses, which must never load inside TIA's process.
        ///
        /// Public so it can be exercised on its own, including inside a restricted
        /// AppDomain: TIA runs Add-Ins in partial trust, and that is not something to
        /// find out about from a crash report.
        /// </summary>
        public static string Payload(string projectDirectory, PlcSelection selection)
        {
            HandoffPayload payload = new HandoffPayload
            {
                ProjectDirectory = projectDirectory,
                Plc = new HandoffPlc
                {
                    Name = selection.PlcName,
                    Addresses = Copy(selection.Addresses)
                },
                DataBlocks = Copy(selection.DataBlocks)
            };

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(HandoffPayload));

            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, payload);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string[] Copy(System.Collections.Generic.IReadOnlyList<string> values)
        {
            string[] copy = new string[values.Count];
            for (int i = 0; i < values.Count; i++) copy[i] = values[i];
            return copy;
        }
    }
}
