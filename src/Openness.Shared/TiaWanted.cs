using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Openness.Shared
{
    /// <summary>
    /// How a satellite recognises the TIA Portal it belongs to, among the several that may be
    /// running - and, given what is running, which one that is.
    ///
    /// **The project is the answer and the ancestry is only a hint**, the correction the core
    /// updater's first version paid for. It matched the immediate parent process against
    /// `TiaPortal.GetProcesses()` and, with two instances open, matched nothing in either TIA
    /// version - the process that starts a satellite is not one of the processes Openness lists.
    /// The project path has no such gap: the Add-In is *in* a project,
    /// `TiaPortalProcess.ProjectPath` says which project each instance has open, and the two
    /// are the same string.
    ///
    /// **Nothing here guesses.** Each signal has to name exactly one instance; two answers is
    /// no answer, and the window then asks the operator rather than picking.
    ///
    /// **The choosing lives here, not in the Siemens half**, where the core updater keeps it:
    /// it only ever needed the list of what was running, so it is decided - and tested - with no
    /// TIA Portal anywhere near it.
    /// </summary>
    public sealed class TiaWanted
    {
        private TiaWanted(string projectPath, IReadOnlyList<int> ancestors)
        {
            ProjectPath = projectPath;
            Ancestors = ancestors ?? new List<int>();
        }

        /// <summary>
        /// The project file the Add-In was in - <c>…\LabSlave.ap21</c> - or null when nobody
        /// said, which is what a window started by hand looks like.
        /// </summary>
        public string ProjectPath { get; }

        /// <summary>Every process this one descends from, nearest first. Possibly empty.</summary>
        public IReadOnlyList<int> Ancestors { get; }

        public static TiaWanted Of(string projectPath, IReadOnlyList<int> ancestors) =>
            new TiaWanted(string.IsNullOrWhiteSpace(projectPath) ? null : projectPath.Trim(), ancestors);

        /// <summary>Nothing to go on: started by hand, with no ancestry readable either.</summary>
        public static TiaWanted Nothing => new TiaWanted(null, null);

        public bool NamesProject => ProjectPath != null;

        /// <summary>The project's own name, for a sentence a person reads.</summary>
        public string ProjectName
        {
            get
            {
                if (!NamesProject) return null;

                try
                {
                    return Path.GetFileNameWithoutExtension(ProjectPath);
                }
                catch (Exception)
                {
                    return ProjectPath;
                }
            }
        }

        /// <summary>
        /// Whether a running instance has this satellite's project open.
        ///
        /// **Compared as full paths, case-insensitively**, because Windows is: the Add-In and
        /// the satellite run in the same logon session, so a mapped drive spells the same on
        /// both sides, but nothing promises the same capitalisation.
        /// </summary>
        public bool IsProject(string path)
        {
            if (!NamesProject || string.IsNullOrWhiteSpace(path)) return false;

            return string.Equals(Full(path), Full(ProjectPath), StringComparison.OrdinalIgnoreCase);
        }

        public bool IsAncestor(int processId)
        {
            foreach (int one in Ancestors)
                if (one == processId) return true;

            return false;
        }

        /// <summary>
        /// The instance this satellite belongs to, or null when nothing says - in which case
        /// the list goes to the operator.
        ///
        /// The project first, the ancestry second, and a lone TIA Portal third, which is not a
        /// guess. **The third counts everything running**, not only what could be described:
        /// an instance that would not even say its id is still one more TIA Portal, and with it
        /// on the machine the one that did answer is no longer the only candidate.
        /// </summary>
        /// <param name="considered">What was running and could be described.</param>
        /// <param name="running">How many instances Openness listed, described or not.</param>
        public RunningPortal Pick(IReadOnlyList<RunningPortal> considered, int running)
        {
            if (considered == null || considered.Count == 0) return null;

            RunningPortal byProject = Only(considered, one => IsProject(one.Project));
            if (byProject != null) return byProject;

            RunningPortal byAncestry = Only(considered, one => IsAncestor(one.Id));
            if (byAncestry != null) return byAncestry;

            return running == 1 && considered.Count == 1 ? considered[0] : null;
        }

        /// <summary>
        /// Why <see cref="Pick"/> found nothing, in a sentence that says what to do.
        ///
        /// **The project open in two instances is its own sentence**, and the core updater's
        /// copy does not have it: there it reads "no running TIA Portal has 'X' open" about a
        /// project that two of them have open - which sends the operator to look for a closed
        /// project instead of choosing between two open ones.
        /// </summary>
        public string WhyNone(IReadOnlyList<RunningPortal> considered, int running)
        {
            if (running == 0)
                return "No TIA Portal is running. Open the project in TIA Portal and start this again.";

            if (NamesProject)
            {
                int holding = 0;

                if (considered != null)
                    foreach (RunningPortal one in considered)
                        if (one != null && IsProject(one.Project)) holding++;

                if (holding > 1)
                    return string.Format(CultureInfo.CurrentCulture,
                        "'{0}' is open in {1} TIA Portals, and nothing says which one this window belongs to.",
                        ProjectName, holding);

                return string.Format(CultureInfo.CurrentCulture,
                    "No running TIA Portal has '{0}' open - it may have been closed, or reopened, " +
                    "since this window was started.", ProjectName);
            }

            return "Several TIA Portals are running and nothing said which one this window belongs to.";
        }

        /// <summary>
        /// The one instance a signal names, or null. **Two answers is no answer**: the same
        /// project open twice, or a chain running through both, identifies nothing, and taking
        /// the first would be exactly the guess this exists to avoid.
        /// </summary>
        private static RunningPortal Only(IReadOnlyList<RunningPortal> considered, Func<RunningPortal, bool> wants)
        {
            RunningPortal found = null;

            foreach (RunningPortal one in considered)
            {
                if (one == null || !wants(one)) continue;
                if (found != null) return null;

                found = one;
            }

            return found;
        }

        /// <summary>
        /// A path as Windows would resolve it, or the path itself when it will not resolve.
        /// **A malformed argument must not end the attach** - it is one signal, and failing to
        /// normalise it only means this signal says nothing.
        /// </summary>
        private static string Full(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch (Exception)
            {
                return path.Trim();
            }
        }
    }
}
