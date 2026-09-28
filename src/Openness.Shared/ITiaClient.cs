namespace Openness.Shared
{
    /// <summary>
    /// What every satellite asks of a running TIA Portal first: to attach to the right one.
    /// A satellite's own port extends this with what it then does - read a PLC, import into a
    /// folder - and one class per TIA version implements it, in <c>Openness.V20</c> and
    /// <c>Openness.V21</c>, because Openness is two assemblies with different public key tokens.
    ///
    /// **Every call happens on one thread**, the one <see cref="TiaWorker{TSession}"/> owns.
    /// Openness objects belong to the thread that obtained them, and this interface is written
    /// as though that were not true - so nothing but the worker may call it.
    ///
    /// **Not <c>IDisposable</c>, and that is deliberate.** Disposing an attached `TiaPortal`
    /// closes TIA Portal - seen on the VM, with an engineer's project open - so nothing here
    /// invites a <c>using</c>. The connection goes when the satellite's process does.
    /// </summary>
    public interface ITiaClient
    {
        /// <summary>
        /// Attaches to the TIA Portal <paramref name="wanted"/> names and reads what identifies
        /// its project. Called once, first. **It never picks between two candidates**: when
        /// nothing names exactly one, the answer is a refusal carrying the list, which the
        /// window turns into a choice.
        /// </summary>
        TiaAttachment Attach(TiaWanted wanted);

        /// <summary>
        /// Attaches to one named instance, because the operator picked it. **It looks the
        /// machine up again rather than trusting the list**, which may be a minute old: an
        /// instance that has closed in between is reported as gone, with what is running now.
        /// </summary>
        TiaAttachment AttachTo(int processId);
    }
}
