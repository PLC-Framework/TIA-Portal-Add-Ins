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
    /// allowed - and only the installation knows where they are.
    ///
    /// **The registry publishes V20 and not V21**, which is the whole shape of this class.
    /// Read off a real station, where both are installed:
    ///
    /// <code>
    /// HKLM\SOFTWARE\Siemens\Automation\Openness         has: 20.0, 21.0, AllowList
    ///     20.0\PublicAPI\20.0.0.0   PublicKeyToken, Siemens.Engineering, Siemens.Engineering.Hmi,
    ///                               AssemblyVersion, EngineeringVersion
    ///     20.0\PublicAPI\17.0.0.0   the same, for each older API it still serves
    ///     21.0\PublicAPI\21.0.0.0   EngineeringVersion            - and nothing else
    /// </code>
    ///
    /// So V20 resolves by name and **V21 has nothing to resolve from**: no path, no assembly,
    /// nothing to take a folder off. What V20 does publish is the *layout* -
    /// `…\Portal V20\PublicAPI\V20\Siemens.Engineering.dll` - and that transfers: the same
    /// installation root, with the version segments rewritten, is where V21's assemblies are.
    /// That is what <see cref="InstallationRoot"/> and <see cref="Folders"/> do between them,
    /// and it is why this beats hardcoding `C:\Program Files`: the root comes from the machine.
    ///
    /// **It is installed before anything Siemens is touched**, and the caller keeps that true
    /// by putting the first such use behind a method the JIT has not reached yet. Resolving
    /// runs once per assembly; the CLR caches what a handler returns.
    /// </summary>
    internal static class OpennessAssemblies
    {
        private const string Root = @"SOFTWARE\Siemens\Automation\Openness";

        /// <summary>
        /// The TIA version this executable is built against, as the registry spells it.
        /// Preferred when a station has several installed, because a V20 build must not bind
        /// to V21's assemblies - different public key tokens, so the load would fail anyway,
        /// just later and with a worse message.
        ///
        /// **This build covers V17 through V20**, which share one binary-compatible API, and
        /// the station above publishes all four under `20.0\PublicAPI\`. Which of them gets
        /// loaded is what <see cref="Ordered"/> decides, and getting it wrong is what made
        /// this executable load V17's assembly and fail on a type V17 never had.
        /// </summary>
        private const string Version = "20.0";

        /// <summary>The same version as a path segment: `…\Portal V20\PublicAPI\V20\`.</summary>
        private const string Segment = "V20";

        /// <summary>
        /// V21 ships its sixteen assemblies under a framework subfolder, which V20 does not
        /// have. Probed after the folder itself, so a layout without it still works.
        /// </summary>
        private static readonly string[] Subfolders = { "", "net48" };

        /// <summary>Enough of an account to act on. The window scrolls, so it can be generous.</summary>
        private const int MostLines = 60;

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
        /// **Because the alternative is another round trip to the VM**, and this is what
        /// turned "V21 does not work" into the layout above in one run.
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
                Note(wanted + ": NOT FOUND");
                return null;
            }

            Note(wanted + " -> " + path);

            return Assembly.LoadFrom(path);
        }

        private static string Find(string assembly)
        {
            // Beside this executable first: that is where a station with an unusual
            // installation can be helped by hand, and where a test can put a stand-in.
            string beside = Beside(assembly);
            if (beside != null) return beside;

            List<string> published = new List<string>();

            string named = InRegistry(assembly, published);
            if (named != null) return named;

            // Nothing published this assembly by name. Every folder the registry does mention
            // is a place its siblings live, and the layout those folders reveal says where
            // this version's own are.
            return Probe(Folders(published), assembly);
        }

        /// <summary>
        /// Walks the Openness key for an entry naming this assembly, collecting every path it
        /// publishes on the way - which is what the fallback needs whether this succeeds or not.
        /// </summary>
        private static string InRegistry(string assembly, List<string> published)
        {
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

                    // This version first; the others only so their paths reach `published`,
                    // since binding to them would fail on the public key token anyway.
                    string found = InVersion(root, Version, assembly, published);
                    if (found != null) return found;

                    foreach (string version in versions)
                    {
                        if (string.Equals(version, Version, StringComparison.OrdinalIgnoreCase)) continue;

                        InVersion(root, version, assembly, published);
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

        private static string InVersion(RegistryKey root, string version, string assembly, List<string> published)
        {
            using (RegistryKey api = root.OpenSubKey(version + @"\PublicAPI"))
            {
                if (api == null) return null;

                string found = null;

                foreach (string name in Ordered(api.GetSubKeyNames(), version))
                {
                    using (RegistryKey entries = api.OpenSubKey(name))
                    {
                        if (entries == null) continue;

                        string[] values = entries.GetValueNames();
                        Note(version + @"\PublicAPI\" + name + " publishes: " + string.Join(", ", values));

                        foreach (string value in values)
                        {
                            string path = entries.GetValue(value) as string;
                            if (string.IsNullOrWhiteSpace(path)) continue;

                            if (!published.Contains(path)) published.Add(path);

                            // Loosely matched: a value is named either by the simple name or
                            // by the full display name, and which one an installation writes
                            // is not something to bet a launch on.
                            bool names = string.Equals(value, assembly, StringComparison.OrdinalIgnoreCase) ||
                                         value.StartsWith(assembly + ",", StringComparison.OrdinalIgnoreCase);

                            if (found == null && names && File.Exists(path)) found = path;
                        }
                    }
                }

                return found;
            }
        }

        /// <summary>
        /// The API subkeys, this version's first and the rest newest-first.
        ///
        /// **Order is the whole of it, and taking them as they came was a real bug.** One TIA
        /// installation serves every older API it is compatible with, so `20.0\PublicAPI\`
        /// holds `17.0.0.0`, `18.0.0.0`, `19.0.0.0` *and* `20.0.0.0`, each publishing its own
        /// `Siemens.Engineering`. Enumerated as the registry returns them, the first match is
        /// **V17** - which loaded, and then failed on the first type V17 does not have:
        /// *"Could not load type 'Siemens.Engineering.SW.Units.PlcUnitBase' from assembly
        /// 'Siemens.Engineering, Version=17.0.0.0'"*. Software units did not exist yet.
        ///
        /// A station with only an older TIA still resolves, through the rest of the list -
        /// and a project using something that TIA has not got fails where it uses it, naming
        /// the type, which is the best answer available.
        /// </summary>
        private static IEnumerable<string> Ordered(string[] names, string version)
        {
            List<string> rest = new List<string>(names);
            List<string> ordered = new List<string>();

            foreach (string name in names)
            {
                if (!name.StartsWith(version + ".", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, version, StringComparison.OrdinalIgnoreCase)) continue;

                ordered.Add(name);
                rest.Remove(name);
            }

            // Newest first among the rest: an older API is a fallback, and the newest of them
            // is the one most likely to carry what this was compiled against.
            rest.Sort((left, right) => Compare(right).CompareTo(Compare(left)));
            ordered.AddRange(rest);

            return ordered;
        }

        /// <summary>
        /// A subkey name as a comparable version.
        ///
        /// **`System.Version` written out, because this class has a constant called
        /// `Version`** and a member name shadows the type - the same trap `AddIn.Core` and
        /// `Core.Repo.RepoPaths.CoreFolder` already record, met a third time.
        /// </summary>
        private static System.Version Compare(string name)
        {
            System.Version parsed;

            return System.Version.TryParse(name, out parsed) ? parsed : new System.Version(0, 0);
        }

        /// <summary>
        /// Every folder worth probing: the ones the registry names, the ones their layout
        /// implies for *this* version, and the ordinary installation path as a last resort.
        /// </summary>
        private static List<string> Folders(List<string> published)
        {
            List<string> folders = new List<string>();

            foreach (string path in published) Add(folders, Containing(path));

            foreach (string folder in new List<string>(folders))
            {
                string root = InstallationRoot(folder);
                if (root == null) continue;

                // `PublicAPI\V20\` is what V20's layout implies; `PublicAPI\` itself is there
                // because the segment is only known to repeat for V20, and a probe costs a
                // file-exists call.
                Add(folders, Path.Combine(root, "Portal " + Segment, "PublicAPI", Segment));
                Add(folders, Path.Combine(root, "Portal " + Segment, "PublicAPI"));
            }

            foreach (string programFiles in new[]
                     {
                         Environment.GetEnvironmentVariable("ProgramW6432"),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                     })
            {
                if (string.IsNullOrWhiteSpace(programFiles)) continue;

                string root = Path.Combine(programFiles, "Siemens", "Automation");

                Add(folders, Path.Combine(root, "Portal " + Segment, "PublicAPI", Segment));
                Add(folders, Path.Combine(root, "Portal " + Segment, "PublicAPI"));
            }

            return folders;
        }

        /// <summary>
        /// What sits above a <c>Portal Vxx</c> folder - the installation root every version
        /// shares, taken out of a path the registry published.
        ///
        /// **That root comes from the machine rather than from a guess**, which is what makes
        /// deriving better than a hardcoded `C:\Program Files`: a station that installed TIA
        /// on D:, or under a renamed folder, still resolves.
        /// </summary>
        private static string InstallationRoot(string folder)
        {
            if (folder == null) return null;

            int portal = folder.IndexOf(@"\Portal V", StringComparison.OrdinalIgnoreCase);

            return portal < 0 ? null : folder.Substring(0, portal);
        }

        private static string Containing(string path)
        {
            try
            {
                return Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            }
            catch (Exception)
            {
                // A value that is not a path at all.
                return null;
            }
        }

        private static void Add(List<string> folders, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;

            foreach (string known in folders)
                if (string.Equals(known, folder, StringComparison.OrdinalIgnoreCase)) return;

            folders.Add(folder);
        }

        private static string Probe(List<string> folders, string assembly)
        {
            foreach (string folder in folders)
            {
                foreach (string subfolder in Subfolders)
                {
                    string path = subfolder.Length == 0
                        ? Path.Combine(folder, assembly + ".dll")
                        : Path.Combine(folder, subfolder, assembly + ".dll");

                    bool there = File.Exists(path);
                    Note((there ? "found   " : "probed  ") + path);

                    if (there) return path;
                }
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
