using System;
using System.IO;

namespace Core
{
    /// <summary>
    /// The framework's installation folder, shared by the Add-Ins and the satellite apps.
    ///
    /// It is **per machine**, not per user: one install serves every engineer who logs
    /// into the station, and both the V20 and V21 Add-Ins resolve the very same path.
    ///
    /// Deliberately NOT derived from the running assembly's location. TIA Portal loads an
    /// Add-In out of its .addin package, so Assembly.Location is not something to rely on;
    /// and it is not derived from UserAddIns either, since that folder is per TIA version
    /// and belongs to Siemens. A well-known path under CommonApplicationData needs no
    /// discovery, and reading it needs no elevation — only installing does.
    ///
    ///     %ProgramData%\PLC-Framework\
    ///     +-- .env
    ///     +-- satellites\      apps with a UI, launched for the user
    ///     +-- tools\           helper executables, run without a UI
    ///
    /// The split is by role, not by file type: what the user sees and interacts with
    /// goes in satellites, what only the framework invokes goes in tools. Keeping them
    /// apart is what stops either folder from becoming a dumping ground.
    ///
    /// Nothing here creates directories or touches disk: these are just the agreed
    /// locations. Installing is somebody else's job.
    /// </summary>
    public static class InstallPaths
    {
        /// <summary>
        /// Overrides <see cref="Root"/> when set. Useful to point a test run, or the VM,
        /// at a staging folder without reinstalling.
        /// </summary>
        public const string RootOverrideVariable = "PLC_FRAMEWORK_HOME";

        private const string SatellitesFolderName = "satellites";
        private const string ToolsFolderName = "tools";
        private const string EnvFileName = ".env";

        /// <summary>Root of the installation, or null when it cannot be determined.</summary>
        public static string Root
        {
            get
            {
                string overridden = SafeEnvironmentVariable(RootOverrideVariable);
                if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

                string programData = SafeFolder(Environment.SpecialFolder.CommonApplicationData);
                return string.IsNullOrEmpty(programData)
                    ? null
                    : Path.Combine(programData, Product.Title);
            }
        }

        /// <summary>Where the satellite apps live — the ones the user opens.</summary>
        public static string Satellites => Combine(Root, SatellitesFolderName);

        /// <summary>Where helper executables live — the ones only the framework invokes.</summary>
        public static string Tools => Combine(Root, ToolsFolderName);

        /// <summary>
        /// The .env holding secrets, kept out of the TIA project on purpose.
        ///
        /// Note it sits in a per-machine folder, so it is readable by every user of the
        /// station. Fine for a shared team credential; if a personal token ever goes in
        /// here, move it to a per-user location instead.
        /// </summary>
        public static string EnvFile => Combine(Root, EnvFileName);

        /// <summary>Full path of a satellite app, by file name.</summary>
        public static string Satellite(string fileName) => Combine(Satellites, fileName);

        /// <summary>Full path of a helper executable, by file name.</summary>
        public static string Tool(string fileName) => Combine(Tools, fileName);

        private static string Combine(string root, string child) =>
            string.IsNullOrEmpty(root) || string.IsNullOrEmpty(child) ? null : Path.Combine(root, child);

        // An Add-In runs inside TIA Portal under the permissions its Config.xml declares.
        // Neither call is expected to fail, but a missing EnvironmentPermission must
        // degrade to "not found" rather than take the host process down.
        private static string SafeEnvironmentVariable(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch { return null; }
        }

        private static string SafeFolder(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder); }
            catch { return null; }
        }
    }
}
