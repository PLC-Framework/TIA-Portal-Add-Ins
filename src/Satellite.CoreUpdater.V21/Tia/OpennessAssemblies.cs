using System;
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
    ///     Siemens.Engineering.Base = ...\PublicAPI\V21\Siemens.Engineering.Base.dll
    /// </code>
    ///
    /// **It is installed before anything Siemens is touched**, and the caller keeps that true
    /// by putting the first such use behind a method the JIT has not reached yet. Resolving
    /// runs once per assembly; the CLR caches what a handler returns.
    ///
    /// **Not verified on this machine** - the development PC has no TIA Portal and therefore
    /// no such key. The shape above is Siemens' documented layout, and the matching is
    /// deliberately loose because the value names differ between installations: some carry
    /// the simple name, some the full display name. **Confirm on the VM.**
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

        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;

            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string wanted = new AssemblyName(args.Name).Name;

            // Only ever Siemens': anything else missing is our own packaging problem, and
            // answering null leaves the CLR's own message intact.
            if (!wanted.StartsWith("Siemens.", StringComparison.OrdinalIgnoreCase)) return null;

            string path = Find(wanted);

            return path == null ? null : Assembly.LoadFrom(path);
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
                    if (root == null) return null;

                    // The version this was built against wins; anything else is a fallback
                    // that will probably fail to bind, but failing with a path named is
                    // better than failing with nothing found.
                    return InVersion(root, Version, assembly) ?? InAnyVersion(root, assembly);
                }
            }
            catch (Exception)
            {
                // A locked-down machine can refuse the read outright. There is nothing to do
                // about it here; the caller turns a failed load into a sentence on screen.
                return null;
            }
        }

        private static string InAnyVersion(RegistryKey root, string assembly)
        {
            foreach (string version in root.GetSubKeyNames())
            {
                if (string.Equals(version, Version, StringComparison.OrdinalIgnoreCase)) continue;

                string found = InVersion(root, version, assembly);
                if (found != null) return found;
            }

            return null;
        }

        private static string InVersion(RegistryKey root, string version, string assembly)
        {
            using (RegistryKey api = root.OpenSubKey(version + @"\PublicAPI"))
            {
                if (api == null) return null;

                foreach (string name in api.GetSubKeyNames())
                {
                    using (RegistryKey entries = api.OpenSubKey(name))
                    {
                        string found = InEntries(entries, assembly);
                        if (found != null) return found;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// **Matched loosely on purpose.** A value is named either by the simple name or by
        /// the full display name - `Siemens.Engineering.Base, Version=21.0.0.0, ...` - and
        /// which of the two an installation writes is not something to bet a launch on.
        /// </summary>
        private static string InEntries(RegistryKey entries, string assembly)
        {
            if (entries == null) return null;

            foreach (string value in entries.GetValueNames())
            {
                if (!string.Equals(value, assembly, StringComparison.OrdinalIgnoreCase) &&
                    !value.StartsWith(assembly + ",", StringComparison.OrdinalIgnoreCase)) continue;

                string path = entries.GetValue(value) as string;

                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
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
