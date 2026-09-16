using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Threading;

namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// The one thread that talks to TIA Portal, and the queue the window posts work onto.
    ///
    /// **Openness objects belong to the thread that obtained them**, so a session cannot be
    /// created on one thread and used from another - and it cannot live on the UI thread
    /// either, because walking a PLC takes long enough to freeze a window. Stage two sidestepped
    /// the question by taking a snapshot and letting go; a map of several thousand objects
    /// cannot, so this is where it gets answered.
    ///
    /// **The session is created inside the thread, by the first item of work.** That is what
    /// keeps a machine with no Openness assemblies from killing the process before it paints:
    /// the failure arrives as an exception on a work item, which has somewhere to go.
    ///
    /// **Nothing here disposes a `TiaPortal`.** Doing so closes TIA Portal - seen on the VM,
    /// with an engineer's project open. `Dispose` on this stops the pump and lets the process
    /// end, which is what releases the connection.
    /// </summary>
    public sealed class TiaWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        private readonly Func<ITiaSession> _connect;
        private readonly Dispatcher _ui;
        private readonly Thread _thread;

        private ITiaSession _session;

        public TiaWorker(Func<ITiaSession> connect, Dispatcher ui)
        {
            _connect = connect;
            _ui = ui;

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
        /// thread it is on - the one distinction that matters here is already made, and
        /// making it twice is how a window ends up touching a control it does not own.
        /// </summary>
        public void Post<T>(Func<ITiaSession, T> work, Action<T> done, Action<Exception> failed)
        {
            if (work == null) return;

            _queue.Add(() =>
            {
                T answer;

                try
                {
                    answer = work(Session());
                }
                catch (Exception exception)
                {
                    Report(failed, exception);
                    return;
                }

                if (done != null) _ui.BeginInvoke(new Action(() => done(answer)));
            });
        }

        private ITiaSession Session() => _session ?? (_session = _connect());

        private void Report(Action<Exception> failed, Exception exception)
        {
            if (failed != null) _ui.BeginInvoke(new Action(() => failed(exception)));
        }

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
                catch (Exception)
                {
                    // Post already reports everything the work itself can throw. Reaching
                    // here would mean the reporting threw, and taking the pump down with it
                    // would leave the window waiting forever.
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
