using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Siemens.Engineering;

namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// Attaches to a running TIA Portal through Openness.
    ///
    /// Identical in the V20 and V21 executables, and duplicated for the reason the Add-In
    /// adapters already are: `TiaPortal.GetProcesses`, `TiaPortalProcess.Attach` and
    /// `Project.Path` are the same surface in both - read off both assemblies - but they live
    /// in `Siemens.Engineering` (token `d29ec89bac048f84`) and `Siemens.Engineering.Base`
    /// (token `29bfe5fdf4ba5d3b`), so one binary cannot serve both. **The code is identical
    /// line for line**; across the three files of these two executables the only differences
    /// are the comments naming a version and one constant, `OpennessAssemblies.Version`.
    /// </summary>
    internal sealed class TiaSession : ITiaSession
    {
        public TiaAttachment Attach(int? preferredProcessId)
        {
            IList<TiaPortalProcess> running;

            try
            {
                running = TiaPortal.GetProcesses();
            }
            catch (Exception exception)
            {
                return TiaAttachment.Failed(
                    "The running TIA Portals could not be listed.\n\n" + exception.Message);
            }

            List<string> considered = new List<string>();
            TiaPortalProcess chosen = null;

            foreach (TiaPortalProcess process in running)
            {
                considered.Add(Describe(process));

                if (preferredProcessId.HasValue && process.Id == preferredProcessId.Value) chosen = process;
            }

            // Nothing said which one, and there is only one: that is not a guess.
            if (chosen == null && running.Count == 1) chosen = running[0];

            if (chosen == null) return TiaAttachment.Failed(Why(running.Count, preferredProcessId), considered);

            return Read(chosen, considered);
        }

        /// <summary>
        /// Attaches and reads what identifies the project.
        ///
        /// **Nothing is disposed, and that is the whole of the lesson.** The first version
        /// wrapped the portal in a `using` and wrapped the process handles in a `finally`,
        /// on the reasoning that disposing an *attached* portal detaches the client rather
        /// than closing the application. **It closes TIA Portal** - confirmed on the VM, where
        /// the V20 instance shut down the moment this window said "Ready". `TiaPortal.Dispose`
        /// is how an Openness client *shuts a portal down*, and attaching does not change what
        /// the method means.
        ///
        /// Which of the two disposals did it was not established and does not need to be:
        /// neither is needed. What is held here is a handful of handles in a process that ends
        /// when its window closes, and the operating system releases them then. Set against
        /// closing an engineer's TIA Portal with their project open, that is not a trade worth
        /// thinking about twice.
        /// </summary>
        private static TiaAttachment Read(TiaPortalProcess process, List<string> considered)
        {
            int id = process.Id;

            try
            {
                TiaPortal portal = process.Attach();
                Project project = portal.Projects.FirstOrDefault();

                return TiaAttachment.To(id, project?.Name, project?.Path?.DirectoryName, considered);
            }
            catch (Exception exception)
            {
                return TiaAttachment.Failed(
                    string.Format(CultureInfo.CurrentCulture,
                        "TIA Portal process {0} refused the connection.\n\n{1}", id, exception.Message),
                    considered);
            }
        }

        private static string Why(int running, int? preferred)
        {
            if (running == 0)
                return "No TIA Portal is running. Open the project in TIA Portal and start this again.";

            if (!preferred.HasValue)
                return "Several TIA Portals are running and this window cannot tell which one opened it, " +
                       "so it will not guess. Start it from the PLC in the project you mean.";

            return string.Format(CultureInfo.CurrentCulture,
                "The TIA Portal that opened this window (process {0}) is no longer running.", preferred.Value);
        }

        /// <summary>
        /// One running TIA Portal, in the words the window shows: the project is what an
        /// operator recognises, the process id is what tells two of the same project apart.
        /// </summary>
        private static string Describe(TiaPortalProcess process)
        {
            string project;

            try
            {
                project = process.ProjectPath == null ? "no project open" : process.ProjectPath.FullName;
            }
            catch (Exception)
            {
                // An instance that is starting up, or shutting down, answers nothing useful.
                project = "its project could not be read";
            }

            return string.Format(CultureInfo.CurrentCulture, "process {0} - {1}", process.Id, project);
        }
    }
}
