using System;
using System.IO;

namespace Core
{
    /// <summary>
    /// The framework's installation folder, shared by the Add-Ins and everything they run.
    ///
    /// It is **per machine**, not per user: one install serves every engineer who logs
    /// into the station, and both the V20 and V21 Add-Ins resolve the very same path.
    ///
    /// Deliberately NOT derived from the running assembly's location. TIA Portal loads an
    /// Add-In out of its .addin package, so Assembly.Location is not something to rely on;
    /// and it is not derived from UserAddIns either, since that folder is per TIA version
    /// and belongs to Siemens. A well-known path needs no discovery, and reading it needs
    /// no elevation — only installing does.
    ///
    ///     C:\Program Files\PLC-Framework\
    ///     +-- *.exe, *.dll     every executable shipped with the framework, and its DLLs
    ///
    ///     %LOCALAPPDATA%\PLC-Framework\
    ///     +-- .env             secrets, per user
    ///     +-- ...              whatever an application writes for itself
    ///
    /// One flat folder for the executables, on purpose. "Satellite" stays the name of a
    /// role — a WPF app the Add-In launches from the menu — not of a location: satellites
    /// sit alongside any command-line helper. Splitting by role only invited arguments
    /// about which half a new executable belonged in, and a tools\ level under a folder
    /// that holds nothing but binaries said nothing the folder did not already say.
    ///
    /// **Program Files, not %ProgramData%, and that was a correction** (2026-09-11). The
    /// original choice rested on "%ProgramData% grants ordinary users read and execute but
    /// not write", which is simply false: its Users ACE carries Write with ContainerInherit,
    /// so it is inherited by every subfolder — measured, and a standard user can drop a file
    /// next to an .exe there without elevation. A directory anyone can write to and everyone
    /// executes from is how a planted DLL gets loaded, and it is why binaries belong in
    /// Program Files, which grants Users read and execute and nothing more. Application
    /// whitelisting agrees: the default AppLocker rules permit execution from Program Files
    /// and Windows and deny it elsewhere for standard users.
    ///
    /// **The rule between the two roots survives the correction, restated:
    /// Program Files is what the installer puts there, %LOCALAPPDATA% is what the
    /// applications write.** Installing needs elevation; nothing the applications do to
    /// their own data ever should.
    ///
    /// Nothing here creates directories or touches disk: these are just the agreed
    /// locations. Installing is somebody else's job.
    /// </summary>
    public static class InstallPaths
    {
        /// <summary>
        /// Overrides <see cref="Root"/> when set. Useful to point a test run, or the VM,
        /// at a staging folder without reinstalling.
        ///
        /// It deliberately does not move <see cref="UserRoot"/>. The two answer different
        /// questions — where the product is installed, and where this user's data lives —
        /// and the reason this variable exists at all is that installing needs elevation,
        /// which the per-user folder never does.
        /// </summary>
        public const string RootOverrideVariable = "PLC_FRAMEWORK_HOME";

        private const string EnvFileName = ".env";

        // Always the 64-bit Program Files, whatever the bitness of the process asking.
        // Core is loaded into TIA Portal, which is x64, and into the satellites, which are
        // AnyCPU - so on a 32-bit host SpecialFolder.ProgramFiles would answer
        // "Program Files (x86)" and the two would resolve DIFFERENT installations without
        // anything failing visibly. ProgramW6432 is set by 64-bit Windows for processes of
        // either bitness and is absent on 32-bit Windows, where the fallback is correct.
        private const string Wow64ProgramFilesVariable = "ProgramW6432";

        /// <summary>
        /// Root of the installation - where every executable shipped with the framework
        /// lives, the satellites the Add-In launches and any command-line helper alike.
        /// Null when it cannot be determined.
        /// </summary>
        public static string Root
        {
            get
            {
                string overridden = SafeEnvironmentVariable(RootOverrideVariable);
                if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

                string programFiles = SafeEnvironmentVariable(Wow64ProgramFilesVariable);
                if (string.IsNullOrWhiteSpace(programFiles))
                {
                    programFiles = SafeFolder(Environment.SpecialFolder.ProgramFiles);
                }

                return string.IsNullOrEmpty(programFiles)
                    ? null
                    : Path.Combine(programFiles, Product.Title);
            }
        }

        /// <summary>
        /// Where the framework's applications keep this user's own data, or null when it
        /// cannot be determined.
        ///
        /// Per user, and writable without elevation — which is the whole point. Three
        /// tenants so far: the .env, the remembered PLC credentials, and the config
        /// template. None of them belongs to the machine, and none of them belongs in a
        /// TIA project either, since those are under version control.
        /// </summary>
        public static string UserRoot
        {
            get
            {
                string localAppData = SafeFolder(Environment.SpecialFolder.LocalApplicationData);
                return string.IsNullOrEmpty(localAppData)
                    ? null
                    : Path.Combine(localAppData, Product.Title);
            }
        }

        /// <summary>
        /// The .env holding secrets, kept out of the TIA project on purpose.
        ///
        /// **Per user**, not per machine. It moved out of the install folder on 2026-09-08
        /// for two reasons: the only thing in it is a personal GitHub token rather than a
        /// shared team credential, and the install folder is not writable by ordinary users,
        /// so an editor saving there would have failed on any station where the engineer is
        /// not an administrator.
        /// </summary>
        public static string EnvFile => Combine(UserRoot, EnvFileName);

        /// <summary>Full path of a shipped executable, by file name.</summary>
        public static string Tool(string fileName) => Combine(Root, fileName);

        /// <summary>
        /// Full path of a per-user file, by file name.
        ///
        /// The file *names* stay with whoever owns them — credentials.json belongs to the
        /// snapshot satellite, config.template.json to the editor — because only one
        /// application reads each. What has to be agreed, and therefore lives here, is the
        /// folder. That is the same line ConfigPaths draws.
        /// </summary>
        public static string UserFile(string fileName) => Combine(UserRoot, fileName);

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
