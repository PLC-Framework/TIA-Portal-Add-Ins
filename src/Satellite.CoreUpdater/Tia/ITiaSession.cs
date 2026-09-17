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
        ///
        /// **It never picks between two candidates.** Each signal has to name exactly one
        /// instance, and when none does the answer is a refusal carrying the list - which the
        /// window turns into a choice rather than a dead end.
        /// </summary>
        /// <param name="wanted">
        /// How to recognise the right instance: the project the Add-In was in, and the
        /// processes this one descends from. <see cref="TiaWanted.Nothing"/> for a window
        /// started by hand.
        /// </param>
        TiaAttachment Attach(TiaWanted wanted);

        /// <summary>
        /// Attaches to one named instance, because the operator picked it out of the list.
        ///
        /// **It looks the machine up again rather than trusting the list**, which may be a
        /// minute old by the time somebody clicks: an instance that has closed in between is
        /// reported as gone, with whatever is running now, instead of throwing.
        /// </summary>
        TiaAttachment AttachTo(int processId);

        /// <summary>The PLCs the attached project holds, in the order the project lists them.</summary>
        IReadOnlyList<string> Plcs();

        /// <summary>
        /// One PLC's software units. Empty is an ordinary answer - only the S7-1500 family
        /// has them, and a project can simply not use them.
        /// </summary>
        IReadOnlyList<string> Units(string plc);

        /// <summary>
        /// Counts what a PLC holds, by kind and by programming language, without reading any
        /// of it.
        ///
        /// **The cheap half of the walk**: both are typed properties in every TIA version, so
        /// this costs one pass and no exports, where the map itself exports every object in
        /// V17-V20. It is what lets the operator narrow four hundred objects down to the
        /// thirty they want before paying for any of them.
        /// </summary>
        ProjectSurvey Survey(string plc, string unit);

        /// <summary>
        /// Walks one PLC, or one of its software units, and says what is there.
        ///
        /// **Everything the filter asks for, not only what looks like the core.** A block in
        /// the wrong folder and a folder the core never heard of are two of the three
        /// discrepancies this exists to find, and neither is visible from a list of core
        /// blocks alone.
        /// </summary>
        /// <param name="unit">A unit's name, or <see cref="Core.Places.GeneralProgram"/>.</param>
        /// <param name="filter">
        /// Which kinds and languages to keep. **Applied before the export**, which is the
        /// whole of its value: the kind and the language cost nothing to read, and the export
        /// is what a run spends its minutes on. Null or empty means everything.
        /// </param>
        /// <param name="progress">
        /// Where it has got to, in words a window can show. **Not optional politeness**: in
        /// V17-V20 this exports every block and type to read its title, and a PLC of several
        /// hundred takes minutes. A window that says nothing for minutes is one an operator
        /// concludes has died - which this project has already paid for once, and is why the
        /// coding-style report opens before its work starts.
        ///
        /// Called on the worker's thread, so whatever is passed must marshal for itself.
        /// </param>
        ProjectMap Map(string plc, string unit, MapFilter filter, Action<string> progress);

        /// <summary>
        /// Writes a plan into the project: each source into the folder its family names.
        ///
        /// **The first thing in this framework that changes somebody's project**, which is why
        /// the plan is made and shown first. By the time this is called the operator has seen
        /// what it would touch and said yes.
        ///
        /// **Nothing is compiled and nothing is rolled back.** TIA refuses an inconsistent block
        /// and compiling would change the project behind an operator who asked for an import;
        /// Openness has no transaction, so a refusal half way through leaves what already went
        /// in, named in the report rather than deleted.
        /// </summary>
        ImportReport Import(string plc, string unit, DownloadPlan plan, Action<string> progress);
    }
}
