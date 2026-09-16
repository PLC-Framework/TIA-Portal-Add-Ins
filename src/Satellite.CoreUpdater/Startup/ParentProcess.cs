using System;
using System.Diagnostics;
using System.Management;

namespace Satellite.CoreUpdater.Startup
{
    /// <summary>
    /// Which TIA Portal launched this window.
    ///
    /// **Here it decides what to attach to**, which is a stronger use than the config editor's
    /// - there it only keys a mutex. `TiaPortal.GetProcesses()` lists every TIA Portal on the
    /// machine, and on a station with two open the right one is the one that started this
    /// process.
    ///
    /// **The Add-In cannot supply it.** Under partial trust `Process.GetCurrentProcess()`
    /// throws `SecurityException` - measured in a restricted AppDomain, for `Id` and
    /// `ProcessName` alike - and Siemens' `AddIn.Utilities` offers only `Process` and
    /// `ProcessStartInfo`. Reading it from this side is also better than being told: it
    /// cannot be forged by editing a handoff, and it degrades correctly when the window is
    /// started by hand, where the parent is a shell and there is no tie.
    ///
    /// > **This is the second copy of these thirty lines**, the first being
    /// > `Satellite.ConfigEditor.Startup.ParentProcess`. It is duplicated rather than
    /// > extracted because the one home both could share is `UI.Shared`, whose whole naming
    /// > principle is that it holds what a *window* needs, and a process looking up its
    /// > parent is not that. `Core` cannot take it either: it is loaded inside TIA Portal and
    /// > its references are the intersection of its consumers' needs, not the union.
    /// > **A third consumer settles it** - extract it then, beside `SingleInstance`, and
    /// > accept the name no longer fitting.
    /// </summary>
    public static class ParentProcess
    {
        /// <summary>
        /// The process id that started this one, or null when it cannot be determined.
        /// Costs about 170 ms, so it is asked once at startup and never again.
        /// </summary>
        public static int? Id()
        {
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
            catch (Exception)
            {
                // WMI can be disabled, or slow to the point of failing. Not knowing the parent
                // is not a failure here: with one TIA Portal open there is nothing to choose
                // between, and with several the window says so and names them.
            }

            return null;
        }
    }
}
