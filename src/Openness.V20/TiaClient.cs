using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Openness.Shared;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace Openness
{
    /// <summary>
    /// Attaches to a running TIA Portal and finds a PLC in its project - what every satellite's
    /// session starts from. A satellite's own session derives from this and adds what it then
    /// does.
    ///
    /// **One source for both TIA versions.** The surface it touches is the same in V17-V20 and
    /// V21, read off both assemblies, but it lives in `Siemens.Engineering` (token
    /// `d29ec89bac048f84`) and `Siemens.Engineering.Base` (token `29bfe5fdf4ba5d3b`), so it is
    /// compiled twice: `Openness.V21` links this project's files rather than keeping a copy -
    /// the mechanism the decision record names for identical code with Siemens dependencies,
    /// and the first place it is used.
    ///
    /// **Choosing the instance is not decided here**: <see cref="TiaWanted.Pick"/> does it, over
    /// the list this reads, so the rule is tested with no TIA Portal anywhere near it.
    ///
    /// **Nothing is disposed, and nothing here ever should be.** Disposing an attached
    /// `TiaPortal` closes TIA Portal - seen on the VM, in the core updater, with an engineer's
    /// project open. The references go when the satellite's process does.
    /// </summary>
    public abstract class TiaClient : ITiaClient
    {
        private TiaPortal _portal;
        private Project _project;

        /// <summary>The project of the TIA Portal attached to, or null before an attach and when none is open.</summary>
        protected Project AttachedProject => _project;

        public TiaAttachment Attach(TiaWanted wanted)
        {
            TiaWanted asked = wanted ?? TiaWanted.Nothing;

            IList<TiaPortalProcess> running;

            try
            {
                running = TiaPortal.GetProcesses();
            }
            catch (Exception exception)
            {
                return TiaAttachment.Failed("The running TIA Portals could not be listed.\n\n" + exception.Message);
            }

            List<RunningPortal> considered = Describe(running);
            RunningPortal chosen = asked.Pick(considered, running.Count);

            if (chosen == null) return TiaAttachment.Failed(asked.WhyNone(considered, running.Count), considered);

            TiaPortalProcess process = Process(running, chosen.Id);

            if (process == null) return Gone(chosen.Id, considered);

            return Read(process, considered);
        }

        public TiaAttachment AttachTo(int processId)
        {
            IList<TiaPortalProcess> running;

            try
            {
                running = TiaPortal.GetProcesses();
            }
            catch (Exception exception)
            {
                return TiaAttachment.Failed("The running TIA Portals could not be listed.\n\n" + exception.Message);
            }

            List<RunningPortal> considered = Describe(running);
            TiaPortalProcess chosen = Process(running, processId);

            if (chosen == null) return Gone(processId, considered);

            return Read(chosen, considered);
        }

        /// <summary>
        /// Attaches and reads what identifies the project, **keeping the attachment**: every call
        /// after this one needs it, and it belongs to the thread that obtained it.
        /// </summary>
        private TiaAttachment Read(TiaPortalProcess process, List<RunningPortal> considered)
        {
            int id = process.Id;

            try
            {
                _portal = process.Attach();
                _project = _portal.Projects.FirstOrDefault();

                return TiaAttachment.To(id, _project?.Name, _project?.Path?.DirectoryName, considered);
            }
            catch (Exception exception)
            {
                return TiaAttachment.Failed(
                    string.Format(CultureInfo.CurrentCulture,
                        "TIA Portal process {0} refused the connection.\n\n{1}", id, exception.Message),
                    considered);
            }
        }

        private static TiaAttachment Gone(int processId, List<RunningPortal> considered) =>
            TiaAttachment.Failed(
                string.Format(CultureInfo.CurrentCulture, "TIA Portal process {0} is no longer running.", processId),
                considered);

        private static TiaPortalProcess Process(IEnumerable<TiaPortalProcess> running, int id)
        {
            foreach (TiaPortalProcess process in running)
            {
                try
                {
                    if (process.Id == id) return process;
                }
                catch (Exception)
                {
                    // An instance that will not say its id is not one anything can attach to.
                }
            }

            return null;
        }

        /// <summary>
        /// Every running TIA Portal, as parts rather than a sentence. **One that will not say its
        /// id is left out**, there being nothing to attach to; one that will not say its project
        /// is listed anyway, since a list that disagrees with the machine is worse than one with a
        /// gap.
        /// </summary>
        private static List<RunningPortal> Describe(IEnumerable<TiaPortalProcess> running)
        {
            List<RunningPortal> found = new List<RunningPortal>();

            foreach (TiaPortalProcess process in running)
            {
                int id;

                try
                {
                    id = process.Id;
                }
                catch (Exception)
                {
                    continue;
                }

                try
                {
                    found.Add(RunningPortal.With(id, process.ProjectPath == null ? null : process.ProjectPath.FullName));
                }
                catch (Exception)
                {
                    // Starting up, or shutting down: it answers nothing useful.
                    found.Add(RunningPortal.Unreadable(id));
                }
            }

            return found;
        }

        // ---- The project's PLCs ---------------------------------------------------------------

        /// <summary>Every PLC in the attached project, wherever its device sits in the tree.</summary>
        protected IReadOnlyList<PlcSoftware> Plcs()
        {
            List<PlcSoftware> found = new List<PlcSoftware>();
            HashSet<PlcSoftware> seen = new HashSet<PlcSoftware>();

            if (_project == null) return found;

            Devices(_project.Devices, found, seen);
            Devices(_project.UngroupedDevicesGroup?.Devices, found, seen);

            foreach (DeviceUserGroup group in _project.DeviceGroups) Group(group, found, seen);

            return found;
        }

        /// <summary>The PLC of that name, or null. Case does not decide, as it does not in TIA's tree.</summary>
        protected PlcSoftware Plc(string name) =>
            Plcs().FirstOrDefault(one => string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));

        private static void Group(DeviceUserGroup group, List<PlcSoftware> found, HashSet<PlcSoftware> seen)
        {
            if (group == null) return;

            Devices(group.Devices, found, seen);

            foreach (DeviceUserGroup child in group.Groups) Group(child, found, seen);
        }

        private static void Devices(DeviceComposition devices, List<PlcSoftware> found, HashSet<PlcSoftware> seen)
        {
            if (devices == null) return;

            foreach (Device device in devices)
            {
                if (device == null) continue;

                foreach (DeviceItem item in device.DeviceItems) Items(item, found, seen);
            }
        }

        private static void Items(DeviceItem item, List<PlcSoftware> found, HashSet<PlcSoftware> seen)
        {
            if (item == null) return;

            PlcSoftware plc = SoftwareOf(item);
            if (plc != null && seen.Add(plc)) found.Add(plc);

            foreach (DeviceItem child in item.DeviceItems) Items(child, found, seen);
        }

        /// <summary>
        /// The PLC a device item carries, or null. Asked of every module in a device, and most of
        /// them are not a CPU - a refusal there is an answer, not a failure.
        /// </summary>
        private static PlcSoftware SoftwareOf(DeviceItem item)
        {
            try
            {
                return item.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
