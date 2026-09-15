using System;

using Siemens.Engineering;

using AddIn.Shared.Adapters;

namespace AddIn.Adapters
{
    /// <summary>
    /// ITiaBusy over TIA Portal's own exclusive access, which is what puts the "busy" state
    /// with its text and its Cancel button on screen.
    ///
    /// Identical in V20 and V21 - `ExclusiveAccess` has the same surface in both, read off the
    /// assemblies - and duplicated for the usual reason: the two host assemblies share names
    /// but not public key tokens.
    ///
    /// **The exclusive access never leaves this method.** An Add-In may not keep a Siemens
    /// engineering object in a field, and the Publisher refuses to package one that does, so
    /// the work is handed in rather than a handle handed out.
    /// </summary>
    internal sealed class TiaBusy : ITiaBusy
    {
        private readonly TiaPortal _tiaPortal;

        public TiaBusy(TiaPortal tiaPortal) => _tiaPortal = tiaPortal;

        public string While(string text, Func<Progress, string> work)
        {
            if (work == null) return null;

            ExclusiveAccess access;
            try
            {
                access = _tiaPortal?.ExclusiveAccess(text);
            }
            catch (Exception)
            {
                // Already held, or a session with no user interface to show it in. The check
                // is what the operator asked for; the dialog is how it looks while it runs.
                access = null;
            }

            if (access == null) return work(ignored => false);

            try
            {
                return work(progress =>
                {
                    try
                    {
                        if (progress != null) access.Text = progress;
                        return access.IsCancellationRequested;
                    }
                    catch (Exception)
                    {
                        // The busy state went away under us: carry on rather than end a check
                        // over the thing that was only showing it.
                        return false;
                    }
                });
            }
            finally
            {
                try
                {
                    access.Dispose();
                }
                catch (Exception)
                {
                    // Nothing left to close.
                }
            }
        }
    }
}
