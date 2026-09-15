using System;

using AddIn.Shared.Adapters;

using SiemensProcess = Siemens.Engineering.AddIn.Utilities.Process;
using SiemensProcessStartInfo = Siemens.Engineering.AddIn.Utilities.ProcessStartInfo;

namespace AddIn.Adapters
{
    /// <summary>
    /// IProcessLauncher over Siemens' own Process wrapper, which is what the
    /// ProcessStartPermission declared in Config.xml authorises.
    ///
    /// The alias is deliberate: this is not System.Diagnostics.Process. The file is
    /// identical in V20 and V21 and duplicated on purpose - the two
    /// Siemens.Engineering.AddIn.Utilities assemblies share a name but not a public key
    /// token, so a single compiled binary could not load in both hosts.
    /// </summary>
    internal sealed class ProcessLauncher : IProcessLauncher
    {
        public string Start(string fileName) => Start(fileName, null);

        public string Start(string fileName, string standardInput)
        {
            try
            {
                if (standardInput == null)
                {
                    // The wrapper delegates to System.Diagnostics.Process, so disposing it
                    // releases the handle without touching the process that was started.
                    using (SiemensProcess started = SiemensProcess.Start(fileName))
                        return started == null ? "No new process was started." : null;
                }

                using (SiemensProcess process = new SiemensProcess())
                {
                    process.StartInfo = new SiemensProcessStartInfo
                    {
                        FileName = fileName,

                        // Redirection is impossible while the shell is doing the starting;
                        // this has to be false before RedirectStandardInput means anything.
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true
                    };

                    if (!process.Start()) return "No new process was started.";

                    // Write, then close. The satellite reads to end of input, so it stays
                    // blocked on the pipe until this end is shut.
                    process.StandardInput.Write(standardInput);
                    process.StandardInput.Close();

                    return null;
                }
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        /// <summary>
        /// Starts it, lets the caller work while its window is already up, and writes the
        /// report into it afterwards.
        ///
        /// **The process stays a local variable for the whole of it, and that is the design.**
        /// An Add-In may not hold a Siemens engineering object in a field: the Publisher
        /// refuses to package one that does, because from V20 an Add-In is not reloaded
        /// between executions and such a member would still be pointing at the previous run.
        /// Handing the work inwards is what keeps this legal - and the work here is minutes
        /// long, which is the whole point of opening the window first.
        /// </summary>
        public string Start(string fileName, string arguments, Func<string> payload)
        {
            try
            {
                using (SiemensProcess process = new SiemensProcess())
                {
                    process.StartInfo = new SiemensProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments ?? string.Empty,

                        // Redirection is impossible while the shell is doing the starting;
                        // this has to be false before RedirectStandardInput means anything.
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true
                    };

                    if (!process.Start()) return "No new process was started.";

                    string standardInput = null;

                    try
                    {
                        standardInput = payload == null ? null : payload();
                    }
                    finally
                    {
                        // Whatever came back - a report, nothing, or an exception on its way
                        // out - the input is closed, so the window stops waiting rather than
                        // sitting on a pipe nobody is going to write to.
                        Hand(process, standardInput);
                    }

                    return null;
                }
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        private static void Hand(SiemensProcess process, string standardInput)
        {
            try
            {
                if (standardInput != null) process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }
            catch (Exception)
            {
                // The window was closed while the check ran: there is nothing to report to,
                // and the check itself already finished.
            }
        }
    }
}
