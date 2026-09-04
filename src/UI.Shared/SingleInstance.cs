using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

using Core;

namespace UI.Shared
{
    /// <summary>
    /// Single-instance guard for the windowed apps (satellites and tools).
    /// </summary>
    public static class SingleInstance
    {
        private const int SW_RESTORE = 9;

        // Must outlive Claim(): if this were a local it could be collected, the handle
        // would be released and the guard would fail intermittently.
        private static Mutex _mutex;

        /// <summary>
        /// True when this is the first instance. False when another one is already running,
        /// in which case its window has been brought to the front.
        /// </summary>
        public static bool Claim(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("An application identifier is required.", nameof(appId));

            // Session-scoped on purpose: the install folder is per-machine, but two users
            // on the same station must each get their own window.
            bool createdNew;
            _mutex = new Mutex(false, "Local\\" + Product.Title + "." + appId, out createdNew);

            if (!createdNew)
                ActivateRunningInstance();

            return createdNew;
        }

        private static void ActivateRunningInstance()
        {
            using (Process current = Process.GetCurrentProcess())
            {
                foreach (Process other in Process.GetProcessesByName(current.ProcessName))
                {
                    using (other)
                    {
                        if (other.Id == current.Id) continue;

                        IntPtr window = SafeMainWindowHandle(other);
                        if (window == IntPtr.Zero) continue;

                        ShowWindow(window, SW_RESTORE);   // no-op unless minimised
                        SetForegroundWindow(window);
                        return;
                    }
                }
            }
        }

        private static IntPtr SafeMainWindowHandle(Process process)
        {
            // A process in another session, or running elevated, can deny access here.
            try { return process.MainWindowHandle; }
            catch (InvalidOperationException) { return IntPtr.Zero; }
            catch (Exception) { return IntPtr.Zero; }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
