using System;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// Says where a long piece of work has got to, and answers whether the operator has asked
    /// it to stop.
    ///
    /// **One delegate for both, because they happen together**: the loop that reports progress
    /// is the loop that has to notice a cancellation, and two calls per object would be two
    /// chances to forget one of them.
    /// </summary>
    /// <param name="text">
    /// What to show, or null to ask without saying anything. Every update is a call into TIA,
    /// and a name that changes thousands of times a second is not a thing anybody reads -
    /// whereas asking whether to stop is a property read and belongs on every object.
    /// </param>
    /// <returns>True when the operator has asked to stop.</returns>
    public delegate bool Progress(string text);

    /// <summary>
    /// TIA Portal's own busy state, around work that takes long enough to need one.
    ///
    /// **Why it is a port**: `ExclusiveAccess` is a Siemens type, and the same one in both
    /// versions - measured, member for member - but it arrives from two assemblies that
    /// cannot both be referenced by one binary.
    ///
    /// **Why the work is passed inwards** rather than a handle passed out: an Add-In may not
    /// keep a Siemens engineering object in a field, and the Publisher refuses to package one
    /// that does. The object stays inside the adapter for as long as the work runs.
    /// </summary>
    public interface ITiaBusy
    {
        /// <summary>
        /// Runs <paramref name="work"/> with TIA showing it is busy, and returns whatever the
        /// work returns.
        ///
        /// **A busy state that cannot be opened is not a reason to refuse the work.** TIA may
        /// be holding exclusive access already, or refuse it in a session without a user
        /// interface; the work then runs with a progress that says nothing and never reports
        /// a cancellation, which is worse than a dialog and far better than no check.
        /// </summary>
        string While(string text, Func<Progress, string> work);
    }
}
