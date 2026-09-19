using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
            Mutex mutex = new Mutex(false, "Local\\" + Product.Title + "." + appId, out createdNew);

            if (createdNew)
            {
                _mutex = mutex;
                return true;
            }

            // **A claim that lost lets go at once.** The guard is the *existence* of the named
            // mutex - nobody owns it, it is created unowned - and a name exists for as long as
            // any handle to it is open. Kept here, the other instance's mutex would outlive the
            // instance itself: found by a test, where a window refused while another process
            // held the project stopped the next window from starting after that process had
            // gone. A process that loses normally exits at once, which is why nothing showed.
            mutex.Dispose();
            ActivateRunningInstance();

            return false;
        }

        /// <summary>
        /// A mutex-safe identifier for a path: eight hex characters of its SHA-256, taken
        /// case-insensitively and without a trailing separator.
        ///
        /// **Hashed, and that is not decoration.** <see cref="Claim"/> builds
        /// <c>Local\&lt;Product&gt;.&lt;id&gt;</c>, and a backslash *separates the mutex
        /// namespace* - a raw path in there does not name what it seems to, and on some paths it
        /// fails outright. It lives here rather than in a satellite because this is the class
        /// that imposes the constraint: the config editor had it as a private helper, and the
        /// core updater arriving as a second consumer is when a copy would have started to drift.
        ///
        /// Eight characters is plenty to keep two open projects apart, and it keeps the mutex
        /// name readable in Process Explorer.
        /// </summary>
        public static string PathKey(string path)
        {
            string value = (path ?? string.Empty).Trim().TrimEnd('\\', '/').ToLowerInvariant();

            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value));

                return BitConverter.ToString(digest, 0, 4).Replace("-", string.Empty);
            }
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
