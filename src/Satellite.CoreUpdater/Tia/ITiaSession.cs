using System;
using System.Collections.Generic;

using Core.Repo;

namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// Everything this satellite asks of a running TIA Portal, from outside it.
    ///
    /// **The one thing Openness is needed for**, which is why it is a port: `Siemens.Engineering`
    /// in V17-V20 and `Siemens.Engineering.Base` in V21 are different assemblies with
    /// different public key tokens, so a single binary would bind to one and fail to load in
    /// the other. Everything on this side of the port is compiled once.
    ///
    /// **A satellite may hold an engineering object where an Add-In may not.** The Publisher
    /// refuses to package an Add-In whose field holds one, because TIA does not reload an
    /// Add-In between executions; nothing inspects an ordinary executable. That is what lets
    /// the attachment live for the window's lifetime instead of being taken and dropped.
    ///
    /// > **Every call happens on one thread**, the one <see cref="TiaWorker"/> owns. Openness
    /// > objects belong to the thread that obtained them, and this interface is written as
    /// > though that were not true - so nothing but the worker may call it.
    /// </summary>
    public interface ITiaSession : IDisposable
    {
        /// <summary>
        /// Attaches to a running TIA Portal and reads what identifies its project. Called
        /// once, first; everything else needs it to have succeeded.
        /// </summary>
        /// <param name="preferredProcessId">
        /// The TIA Portal to prefer when several are running - the process that launched this
        /// window. Null when it could not be read, or when the window was started by hand.
        /// </param>
        TiaAttachment Attach(int? preferredProcessId);

        /// <summary>The PLCs the attached project holds, in the order the project lists them.</summary>
        IReadOnlyList<string> Plcs();

        /// <summary>
        /// One PLC's software units. Empty is an ordinary answer - only the S7-1500 family
        /// has them, and a project can simply not use them.
        /// </summary>
        IReadOnlyList<string> Units(string plc);

        /// <summary>
        /// Walks one PLC, or one of its software units, and says what is there.
        ///
        /// **Everything, not only what looks like the core.** A block in the wrong folder and
        /// a folder the core never heard of are two of the three discrepancies this exists to
        /// find, and neither is visible from a list of core blocks alone.
        /// </summary>
        /// <param name="unit">A unit's name, or <see cref="Core.Places.GeneralProgram"/>.</param>
        /// <param name="progress">
        /// Where it has got to, in words a window can show. **Not optional politeness**: in
        /// V17-V20 this exports every block and type to read its title, and a PLC of several
        /// hundred takes minutes. A window that says nothing for minutes is one an operator
        /// concludes has died - which this project has already paid for once, and is why the
        /// coding-style report opens before its work starts.
        ///
        /// Called on the worker's thread, so whatever is passed must marshal for itself.
        /// </param>
        ProjectMap Map(string plc, string unit, Action<string> progress);
    }
}
