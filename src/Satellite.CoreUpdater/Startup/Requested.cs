using Core;

namespace Satellite.CoreUpdater.Startup
{
    /// <summary>
    /// What the command line asked for: a PLC, and a software unit or the general program.
    ///
    /// <code>
    /// PLC-Framework.Satellite.CoreUpdater.V21.exe  "PLC_1"  "*"
    /// </code>
    ///
    /// **Two arguments, not a handoff**, and the difference is not pedantry: there is no
    /// document, no payload type, and no pair of ends that can disagree about what was
    /// selected. The window still attaches to TIA Portal itself and reads everything else
    /// from there - this only says which part of it the operator was pointing at.
    ///
    /// **The Add-In sends the PLC and `*`**, because its menu entry is on a PLC and no unit
    /// is selected there. So the unit is a starting point rather than a decision: the window
    /// offers the PLC's units and the operator can change it. Started by hand, with no
    /// arguments, both are chosen in the window.
    /// </summary>
    public sealed class Requested
    {
        private Requested(string plc, string unit)
        {
            Plc = plc;
            Unit = unit;
        }

        /// <summary>The PLC the operator right-clicked, or null when nobody said.</summary>
        public string Plc { get; }

        /// <summary>
        /// The software unit, or <see cref="Places.GeneralProgram"/>. Never null once a PLC
        /// was given: an unstated unit is the general program, which is what the Add-In means
        /// by sending the star.
        /// </summary>
        public string Unit { get; }

        public bool Named => !string.IsNullOrWhiteSpace(Plc);

        public static Requested From(string[] arguments)
        {
            if (arguments == null || arguments.Length == 0 || string.IsNullOrWhiteSpace(arguments[0]))
                return new Requested(null, Places.GeneralProgram);

            string unit = arguments.Length > 1 ? arguments[1] : null;

            return new Requested(arguments[0].Trim(), Places.UnitOrGeneral(unit));
        }
    }
}
