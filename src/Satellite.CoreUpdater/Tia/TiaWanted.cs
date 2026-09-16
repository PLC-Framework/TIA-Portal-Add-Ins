using System;
using System.Collections.Generic;
using System.IO;

namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// How this window recognises the TIA Portal it belongs to, among the several that may be
    /// running.
    ///
    /// **The project is the answer and the ancestry is only a hint**, which is the correction
    /// this type exists to carry. The first version matched the immediate parent process
    /// against `TiaPortal.GetProcesses()` and, with two instances open, matched nothing at all
    /// in either TIA version - the process that starts the satellite is not one of the
    /// processes Openness lists. The project path has no such gap: the Add-In is *in* a
    /// project, `TiaPortalProcess.ProjectPath` says which project each instance has open, and
    /// the two are the same string.
    ///
    /// **Nothing here guesses.** Each signal has to name exactly one instance; two answers is
    /// no answer, and the window then asks the operator rather than picking.
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
            new TiaWanted(
                string.IsNullOrWhiteSpace(projectPath) ? null : projectPath.Trim(), ancestors);

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
        /// Whether a running instance has this window's project open.
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
