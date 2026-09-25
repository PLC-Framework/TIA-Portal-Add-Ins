using System;
using System.Diagnostics;
using System.Management;

namespace Satellite.ConfigEditor.Startup
{
    /// <summary>
    /// Which TIA Portal launched this window, so one editor can be tied to one instance.
    ///
    /// **The Add-In cannot tell us.** It runs in partial trust, where
    /// <c>Process.GetCurrentProcess()</c> throws <c>SecurityException</c> - measured in a
    /// restricted AppDomain, for Id and ProcessName alike - and Siemens' own
    /// <c>AddIn.Utilities</c> holds only <c>Process</c> and <c>ProcessStartInfo</c>, neither
    /// of which offers it.
    ///
    /// So the satellite works it out from its own side, where trust is full: its parent
    /// process **is** TIA, because the launcher starts it with UseShellExecute=false. That
    /// is better than being told - it cannot be forged by editing the handoff - and it
    /// degrades correctly when the window is started by hand, where the parent is a shell
    /// and no tie exists.
    /// </summary>
    public static class ParentProcess
    {
        /// <summary>
        /// The process id that started this one, or null when it cannot be determined.
        /// Costs about 170 ms, so it is asked once at startup and never again.
        /// </summary>
        /// <param name="problem">
        /// Why it could not be read - WMI disabled, or slow to the point of failing - and null
        /// otherwise, including when this one simply has no parent left to name.
        /// </param>
        public static int? Id(out string problem)
        {
            problem = null;

            try
            {
                using (Process self = Process.GetCurrentProcess())
                using (ManagementObjectSearcher search = new ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + self.Id))
                using (ManagementObjectCollection results = search.Get())
                {
                    foreach (ManagementBaseObject row in results)
                        using (row)
                            return Convert.ToInt32(row["ParentProcessId"]);
                }
            }
            catch (Exception exception)
            {
                // A missing tie means the guard falls back to one editor per file, which is the
                // safe half of what it was for anyway - but a second editor on the same TIA,
                // over another project, is then allowed where it would not be, and the caller
                // is told why so that is not a mystery.
                problem = exception.Message;
            }

            return null;
        }

        /// <summary>
        /// A mutex-safe identifier for this editor: the TIA instance when there is one,
        /// otherwise the file being edited — hashed by <see cref="UI.Shared.SingleInstance.PathKey"/>,
        /// which is where the reason for hashing it at all is written down.
        /// </summary>
        /// <param name="problem">Why the TIA instance could not be read, when that is why the key is the file.</param>
        public static string InstanceKey(string configPath, out string problem)
        {
            int? parent = Id(out problem);
            if (parent.HasValue) return "tia-" + parent.Value;

            return "file-" + UI.Shared.SingleInstance.PathKey(configPath);
        }
    }
}
