using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace Openness.Shared
{
    /// <summary>
    /// The processes this one descends from, nearest first - one of the signals that say which
    /// TIA Portal a satellite belongs to.
    ///
    /// **The whole chain, not just the parent, and that was a bug fix** in the core updater
    /// (2026-09-16). Its first version read the immediate parent and matched it against
    /// `TiaPortal.GetProcesses()`. Measured on the VM with two instances open, in both versions:
    ///
    /// <code>
    /// V21   parent 12896   portals 12924, 5236
    /// V20   parent 14736   portals  7756, 2480
    /// </code>
    ///
    /// Neither parent is among the portals: TIA either hosts an Add-In in a process of its own
    /// or starts the child through an intermediary, and the chain is what makes the question
    /// stop mattering - whichever it is, the TIA Portal is an ancestor if it is anywhere at all.
    /// **It corroborates and does not decide**; see <see cref="TiaWanted"/>, where the project
    /// path comes first.
    ///
    /// > **Here rather than in `UI.Shared`** (2026-09-28), which is where the core updater's copy
    /// > said a third consumer would take it: what reads the chain is the attach, and this is the
    /// > library the attach lives in. `Satellite.ConfigEditor` keeps its own, which reads the
    /// > direct parent and nothing else.
    /// </summary>
    public static class ParentProcess
    {
        /// <summary>
        /// Far enough for any hosting arrangement, and a guarantee that a recycled process id
        /// pointing back into the chain cannot loop. The visited set closes that too; this
        /// closes it without depending on the set being right.
        /// </summary>
        private const int MaxDepth = 16;

        /// <summary>
        /// Every process this one descends from, nearest ancestor first. Empty when it cannot be
        /// worked out, which is not a failure: the project path decides, and a lone TIA Portal
        /// needs no deciding at all.
        ///
        /// **One WMI query for the whole machine, then walked in memory.** Asking per level costs
        /// about 170 ms each - so it belongs on the worker's thread, not before a window paints.
        /// </summary>
        /// <param name="problem">
        /// Why the ancestry could not be read, and null otherwise - for the log, since with it
        /// gone two TIA Portals on one project leave the operator choosing.
        /// </param>
        public static IReadOnlyList<int> Chain(out string problem)
        {
            List<int> found = new List<int>();
            problem = null;

            try
            {
                Dictionary<int, int> parents = Parents();

                int current;

                using (Process self = Process.GetCurrentProcess()) current = self.Id;

                HashSet<int> seen = new HashSet<int> { current };

                for (int step = 0; step < MaxDepth; step++)
                {
                    int parent;

                    if (!parents.TryGetValue(current, out parent) || parent == 0) break;

                    // A parent that has exited leaves its id behind, and Windows reuses ids - so a
                    // chain can point back at something already walked. Stop rather than circle.
                    if (!seen.Add(parent)) break;

                    found.Add(parent);
                    current = parent;
                }
            }
            catch (Exception exception)
            {
                // WMI can be disabled, or slow to the point of failing. Not knowing the ancestry
                // is not a failure here - it is one signal of three.
                problem = exception.Message;
            }

            return found;
        }

        private static Dictionary<int, int> Parents()
        {
            Dictionary<int, int> parents = new Dictionary<int, int>();

            using (ManagementObjectSearcher search = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId FROM Win32_Process"))
            using (ManagementObjectCollection results = search.Get())
            {
                foreach (ManagementBaseObject row in results)
                    using (row)
                    {
                        object id = row["ProcessId"];
                        object parent = row["ParentProcessId"];

                        if (id == null || parent == null) continue;

                        parents[Convert.ToInt32(id)] = Convert.ToInt32(parent);
                    }
            }

            return parents;
        }
    }
}
