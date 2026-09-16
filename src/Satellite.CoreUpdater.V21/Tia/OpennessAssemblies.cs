using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Microsoft.Win32;

namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// Finds the TIA Openness assemblies at run time, so this executable can load them.
    ///
    /// **An Openness client has to do this and an Add-In never does**, which is the one real
    /// cost of moving the work out of the Add-In. Inside TIA the assemblies are already in
    /// the process; out here they are not in the GAC, not beside this executable - the
    /// project references them with `Private=False` because redistributing them is not
    /// allowed - and only the installation knows where they are. Siemens publishes that in
    /// the registry, which is what this reads.
    ///
    /// <code>
    /// HKLM\SOFTWARE\Siemens\Automation\Openness\&lt;version&gt;\PublicAPI\&lt;version&gt;
    ///     Siemens.Engineering = ...\PublicAPI\V21\Siemens.Engineering.dll
    /// </code>
    ///
    /// **A named entry is looked for first, and its folder second**, which is what V21 needs:
    /// the object model is split across sixteen assemblies there, and the registry does not
    /// publish an entry for each. `Siemens.Engineering.Base` sits *beside* whatever is
    /// published, so the directory of any sibling entry is where to look next. Found the hard
    /// way - the first version resolved V20 and failed V21 with
    /// *"Could not load file or assembly 'Siemens.Engineering.Base'"*.
    ///
    /// **It is installed before anything Siemens is touched**, and the caller keeps that true
    /// by putting the first such use behind a method the JIT has not reached yet. Resolving
    /// runs once per assembly; the CLR caches what a handler returns.
    /// </summary>
    internal static class OpennessAssemblies
    {
        private const string Root = @"SOFTWARE\Siemens\Automation\Openness";

        /// <summary>
        /// The TIA version this executable is built against. Preferred when a station has
        /// several installed, because a V21 build must not bind to V20's assemblies - they
        /// carry different public key tokens and the load would fail anyway, just later and
        /// with a worse message.
        /// </summary>
        private const string Version = "21.0";

        /// <summary>Enough of an account to act on, short enough to fit in a window.</summary>
        private const int MostLines = 40;

        private static readonly List<string> Looked = new List<string>();

        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;

            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        /// <summary>
        /// Where this looked, for the window to show when a load failed.
        ///
        /// **Because the alternative is another round trip to the VM.** The registry layout
        /// cannot be checked on a machine without TIA Portal, so a resolver that only says
        /// "not found" turns every wrong guess into a day. This says which keys existed, what
        /// they published and which folders were probed.
        /// </summary>
        public static string Report()
        {
            lock (Looked)
            {
                if (Looked.Count == 0) return null;

                return "Where the Openness assemblies were looked for:" + Environment.NewLine +
                       "    " + string.Join(Environment.NewLine + "    ", Looked.ToArray());
            }
        }

        private static void Note(string line)
        {
            lock (Looked)
            {
                if (Looked.Count >= MostLines) return;

                Looked.Add(Looked.Count == MostLines - 1 ? "…and more" : line);
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string wanted = new AssemblyName(args.Name).Name;

            // Only ever Siemens': anything else missing is our own packaging problem, and
            // answering null leaves the CLR's own message intact.
            if (!wanted.StartsWith("Siemens.", StringComparison.OrdinalIgnoreCase)) return null;

            string path = Find(wanted);

            if (path == null)
            {
                Note(wanted + ": not found");
                return null;
            }

            Note(wanted + " -> " + path);

            return Assembly.LoadFrom(path);
        }

        private static string Find(string assembly)
        {
            // Beside this executable first: that is where a station with an unusual
            // installation can be helped by hand, and where a test can put a stand-in.
            string local = Beside(assembly);
            if (local != null) return local;

            try
            {
                using (RegistryKey root = RegistryKey
                           .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                           .OpenSubKey(Root))
                {
                    if (root == null)
                    {
                        Note(@"HKLM\" + Root + " does not exist");
                        return null;
                    }

                    string[] versions = root.GetSubKeyNames();
                    Note(@"HKLM\" + Root + " has: " + string.Join(", ", versions));

                    // The version this was built against wins; anything else is a fallback
                    // that will probably fail to bind, but failing with a path named is
                    // better than failing with nothing found.
                    string found = InVersion(root, Version, assembly);
                    if (found != null) return found;

                    foreach (string version in versions)
                    {
                        if (string.Equals(version, Version, StringComparison.OrdinalIgnoreCase)) continue;

                        found = InVersion(root, version, assembly);
                        if (found != null) return found;
                    }

                    return null;
                }
            }
            catch (Exception exception)
            {
                // A locked-down machine can refuse the read outright. There is nothing to do
                // about it here; the caller turns a failed load into a sentence on screen.
                Note("the registry could not be read: " + exception.Message);
                return null;
            }
        }

        private static string InVersion(RegistryKey root, string version, string assembly)
        {
            using (RegistryKey api = root.OpenSubKey(version + @"\PublicAPI"))
            {
                if (api == null) return null;

                List<string> folders = new List<string>();

                foreach (string name in api.GetSubKeyNames())
                {
                    using (RegistryKey entries = api.OpenSubKey(name))
                    {
                        if (entries == null) continue;

                        string[] values = entries.GetValueNames();
                        Note(version + @"\PublicAPI\" + name + " publishes: " + string.Join(", ", values));

                        string named = Named(entries, values, assembly);
                        if (named != null) return named;

                        Folders(entries, values, folders);
                    }
                }

                // **The V21 case.** Sixteen assemblies, and the registry names only some of
                // them; the rest are in the same folder as the ones it does name.
                return Beside(folders, assembly);
            }
        }

        /// <summary>
        /// **Matched loosely on purpose.** A value is named either by the simple name or by
        /// the full display name - `Siemens.Engineering, Version=21.0.0.0, ...` - and which of
        /// the two an installation writes is not something to bet a launch on.
        /// </summary>
        private static string Named(RegistryKey entries, string[] values, string assembly)
        {
            foreach (string value in values)
            {
                if (!string.Equals(value, assembly, StringComparison.OrdinalIgnoreCase) &&
                    !value.StartsWith(assembly + ",", StringComparison.OrdinalIgnoreCase)) continue;

                string path = entries.GetValue(value) as string;

                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
            }

            return null;
        }

        /// <summary>
        /// The folders every published entry lives in. A value can be a file or a directory,
        /// since which of the two an installation writes is another thing not to bet on.
        /// </summary>
        private static void Folders(RegistryKey entries, string[] values, List<string> folders)
        {
            foreach (string value in values)
            {
                string path = entries.GetValue(value) as string;
                if (string.IsNullOrWhiteSpace(path)) continue;

                try
                {
                    string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

                    if (!string.IsNullOrEmpty(folder) && !folders.Contains(folder)) folders.Add(folder);
                }
                catch (Exception)
                {
                    // A value that is not a path at all. Nothing to add.
                }
            }
        }

        private static string Beside(List<string> folders, string assembly)
        {
            foreach (string folder in folders)
            {
                string path = Path.Combine(folder, assembly + ".dll");

                Note("probed " + path);

                if (File.Exists(path)) return path;
            }

            return null;
        }

        private static string Beside(string assembly)
        {
            try
            {
                string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                if (string.IsNullOrEmpty(folder)) return null;

                string path = Path.Combine(folder, assembly + ".dll");

                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
