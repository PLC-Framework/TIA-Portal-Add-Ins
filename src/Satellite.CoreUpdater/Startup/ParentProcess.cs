using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace Satellite.CoreUpdater.Startup
{
    /// <summary>
    /// The processes this one descends from, nearest first.
    ///
    /// **The whole chain, not just the parent, and that was a bug fix** (2026-09-16). The first
    /// version read the immediate parent and matched it against `TiaPortal.GetProcesses()`, on
    /// the reasoning that an Add-In runs inside TIA Portal so the process that started this one
    /// *is* the TIA Portal to attach to. Measured on the VM with two instances open, in both
    /// versions, it is not:
    ///
    /// <code>
    /// V21   parent 12896   portals 12924, 5236
    /// V20   parent 14736   portals  7756, 2480
    /// </code>
    ///
    /// Neither parent is among the portals. So TIA either hosts the Add-In in a process of its
    /// own or starts the child through an intermediary - which one is not settled, and the
    /// chain is what makes the question stop mattering: whichever it is, the TIA Portal is an
    /// ancestor if it is anywhere at all. **It is a corroborating signal and no longer the
    /// deciding one** - see <see cref="Tia.TiaWanted"/>, where the project path comes first.
    ///
    /// > **This is the second copy of these lines**, the first being
    /// > `Satellite.ConfigEditor.Startup.ParentProcess`. It is duplicated rather than
    /// > extracted because the one home both could share is `UI.Shared`, whose whole naming
    /// > principle is that it holds what a *window* needs, and a process looking up its
    /// > parent is not that. `Core` cannot take it either: it is loaded inside TIA Portal and
    /// > its references are the intersection of its consumers' needs, not the union.
    /// > **A third consumer settles it** - extract it then, beside `SingleInstance`, and
    /// > accept the name no longer fitting. Note the two have now diverged: the editor only
    /// > needs the parent, to key a mutex.
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
        /// Every process this one descends from, nearest ancestor first. Empty when it cannot
        /// be worked out, which is not a failure: the project path decides, and a lone TIA
        /// Portal needs no deciding at all.
        ///
        /// **One WMI query for the whole machine, then walked in memory.** Asking per level
        /// costs about 170 ms each, and this runs before the window is on screen - four levels
        /// would be most of a second of nothing, which is the failure this project has already
        /// paid for once.
        /// </summary>
        /// <param name="problem">
        /// Why the ancestry could not be read, and null otherwise. Not a failure of the attach -
        /// the project decides - but with it gone, two TIA Portals on one project leave the
        /// operator choosing where this would otherwise have told them apart, and the log should
        /// say why.
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

                    // A parent that has exited leaves its id behind, and Windows reuses ids -
                    // so a chain can point back at something already walked. Stop rather than
                    // circle.
                    if (!seen.Add(parent)) break;

                    found.Add(parent);
                    current = parent;
                }
            }
            catch (Exception exception)
            {
                // WMI can be disabled, or slow to the point of failing. Not knowing the
                // ancestry is not a failure here - it is one signal of three.
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
