using System;
using System.Collections.Concurrent;
using System.Threading;

using Core.Logging;

namespace Openness.Shared
{
    /// <summary>
    /// The one thread that talks to TIA Portal, and the queue a window posts work onto.
    ///
    /// **Openness objects belong to the thread that obtained them**, so a session cannot be
    /// created on one thread and used from another - and it cannot live on the UI thread
    /// either, because a walk or an import takes long enough to freeze a window.
    ///
    /// **The session is created inside the thread, by the first item of work.** That is what
    /// keeps a machine with no Openness assemblies from killing the process before it paints:
    /// the failure arrives as an exception on a work item, which has somewhere to go.
    ///
    /// **Generic over the session**, because each satellite asks its own questions of TIA - the
    /// core updater a map, the importer a folder - and every one of them needs this same thread.
    /// **And free of WPF**: the answer is handed back through whatever the caller says reaches
    /// its UI thread, so this library needs no window assembly to exist.
    ///
    /// **Nothing here disposes a session.** Disposing an attached `TiaPortal` closes TIA Portal.
    /// <see cref="Dispose"/> stops the pump and lets the process end, which is what releases the
    /// connection.
    /// </summary>
    public sealed class TiaWorker<TSession> : IDisposable where TSession : class
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Func<TSession> _connect;
        private readonly Action<Action> _onUi;
        private readonly Thread _thread;
        private readonly Log _log;

        private TSession _session;

        /// <param name="connect">
        /// Makes the session, **on this thread**, the first time work arrives. The first thing
        /// that touches a Siemens assembly, which is why it is a factory and not a session.
        /// </param>
        /// <param name="onUi">
        /// Runs an action on the caller's UI thread - for WPF,
        /// <c>action =&gt; Dispatcher.BeginInvoke(action)</c>. Never waited on.
        /// </param>
        /// <param name="log">Where a failure the pump cannot hand to anybody goes.</param>
        public TiaWorker(Func<TSession> connect, Action<Action> onUi, Log log = null)
        {
            _connect = connect ?? throw new ArgumentNullException(nameof(connect));
            _onUi = onUi ?? throw new ArgumentNullException(nameof(onUi));
            _log = log ?? Log.Nothing();

            _thread = new Thread(Pump);
            _thread.IsBackground = true;
            _thread.Name = "TIA Portal";

            // STA because an Openness client talks to TIA through COM, and a multi-threaded
            // apartment would marshal every call through a proxy it does not need.
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        /// <summary>
        /// Queues work against the session and hands the answer back on the UI thread.
        ///
        /// **Both callbacks run on the UI thread**, so a caller never has to remember which
        /// thread it is on - the one distinction that matters here is already made, and making
        /// it twice is how a window ends up touching a control it does not own.
        /// </summary>
        public void Post<T>(Func<TSession, T> work, Action<T> done, Action<Exception> failed)
        {
            if (work == null) return;

            try
            {
                _queue.Add(() =>
                {
                    T answer;

                    try
                    {
                        answer = work(Session());
                    }
                    catch (Exception exception)
                    {
                        if (failed != null) _onUi(() => failed(exception));
                        return;
                    }

                    if (done != null) _onUi(() => done(answer));
                });
            }
            catch (InvalidOperationException)
            {
                // Posted after Dispose: the window is closing, and there is nobody to answer.
            }
        }

        private TSession Session() => _session ?? (_session = _connect());

        private void Pump()
        {
            // GetConsumingEnumerable ends when the queue is marked complete, which is what
            // Dispose does - so the thread leaves on its own rather than being aborted.
            foreach (Action work in _queue.GetConsumingEnumerable())
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    // Post already reports everything the work itself can throw. Reaching here
                    // means handing the answer back threw, and taking the pump down with it would
                    // leave the window waiting forever - so it is not rethrown, only written down:
                    // the window never heard the answer, and this is the only place that knows why.
                    _log.Failed("handing an answer back to the window", exception);
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _queue.CompleteAdding();
            }
            catch (Exception)
            {
                // Already disposed. Nothing to do and nothing worth saying.
            }
        }
    }
}
