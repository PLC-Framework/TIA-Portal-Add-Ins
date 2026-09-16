using Core;

namespace Satellite.CoreUpdater.Startup
{
    /// <summary>
    /// What the command line asked for: a PLC, and a software unit or the general program.
    ///
    /// <code>
    /// PLC-Framework.Satellite.CoreUpdater.V21.exe  "PLC_1"  "*"  "E:\proyectos\LabSlave\LabSlave.ap21"
    /// </code>
    ///
    /// **Three arguments, not a handoff**, and the difference is not pedantry: there is no
    /// document, no payload type, and no pair of ends that can disagree about what was
    /// selected. The window still attaches to TIA Portal itself and reads everything else
    /// from there - this only says which part of it the operator was pointing at, and which
    /// of several running TIA Portals to point at in the first place.
    ///
    /// **The Add-In sends the PLC and `*`**, because its menu entry is on a PLC and no unit
    /// is selected there. So the unit is a starting point rather than a decision: the window
    /// offers the PLC's units and the operator can change it. Started by hand, with no
    /// arguments, both are chosen in the window.
    ///
    /// **The third argument was added to fix a real bug** (2026-09-16): with two TIA Portals
    /// open the window matched neither and refused, because the process that launches it is
    /// not one of the processes Openness lists. The project is the one thing both ends can
    /// name identically. An older Add-In sends two arguments and still works - it simply has
    /// nothing to say about which instance, which is where it was before.
    /// </summary>
    public sealed class Requested
    {
        private Requested(string plc, string unit, string project)
        {
            Plc = plc;
            Unit = unit;
            Project = project;
        }

        /// <summary>The PLC the operator right-clicked, or null when nobody said.</summary>
        public string Plc { get; }

        /// <summary>
        /// The software unit, or <see cref="Places.GeneralProgram"/>. Never null once a PLC
        /// was given: an unstated unit is the general program, which is what the Add-In means
        /// by sending the star.
        /// </summary>
        public string Unit { get; }

        /// <summary>
        /// The project file the Add-In was in, or null when nobody said. **Which TIA Portal to
        /// attach to, when several are running** - see <see cref="Tia.TiaWanted"/>.
        /// </summary>
        public string Project { get; }

        public bool Named => !string.IsNullOrWhiteSpace(Plc);

        public static Requested From(string[] arguments)
        {
            if (arguments == null || arguments.Length == 0 || string.IsNullOrWhiteSpace(arguments[0]))
                return new Requested(null, Places.GeneralProgram, Argument(arguments, 2));

            return new Requested(
                arguments[0].Trim(), Places.UnitOrGeneral(Argument(arguments, 1)), Argument(arguments, 2));
        }

        /// <summary>
        /// One argument, or null when it is not there or is blank. **An argument nobody sent
        /// and an argument sent empty mean the same thing here**, and reading them apart would
        /// only invent a difference between an older Add-In and a project with no path.
        /// </summary>
        private static string Argument(string[] arguments, int index)
        {
            if (arguments == null || arguments.Length <= index) return null;

            string value = arguments[index];

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
