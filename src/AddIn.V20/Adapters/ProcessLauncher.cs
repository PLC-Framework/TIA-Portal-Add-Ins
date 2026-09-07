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
    }
}
