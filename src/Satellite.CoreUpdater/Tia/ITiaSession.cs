namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// Attaching to a running TIA Portal, from outside it.
    ///
    /// **This is the one thing in this satellite that Openness is needed for**, which is why
    /// it is a port: `Siemens.Engineering` in V17-V20 and `Siemens.Engineering.Base` in V21
    /// are different assemblies with different public key tokens, so a single binary would
    /// bind to one and fail to load in the other. Everything on this side of the port is
    /// compiled once.
    ///
    /// **A satellite may hold an engineering object where an Add-In may not.** The Publisher
    /// refuses to package an Add-In whose field holds one, because TIA does not reload an
    /// Add-In between executions; nothing inspects an ordinary executable, and an attached
    /// client is a process of its own that ends when its window closes. That is one of the
    /// constraints this design buys its way out of - along with partial trust.
    /// </summary>
    public interface ITiaSession
    {
        /// <summary>
        /// Attaches, reads what identifies the open project, and lets go again.
        ///
        /// **It does not keep the attachment**, and for stage two that is deliberate rather
        /// than lazy: Openness objects are not thread-safe and must be used from the thread
        /// that obtained them, so *holding* one means deciding where that thread lives and
        /// how the window talks to it. Nothing here needs a live session yet, and a snapshot
        /// has no thread to be wrong about. **Stage three decides the threading model**, and
        /// it is the first thing it has to decide.
        /// </summary>
        /// <param name="preferredProcessId">
        /// The TIA Portal to prefer when several are running - the process that launched this
        /// window. Null when it could not be read, or when the window was started by hand.
        /// </param>
        TiaAttachment Attach(int? preferredProcessId);
    }
}
