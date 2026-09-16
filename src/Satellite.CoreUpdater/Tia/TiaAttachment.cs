using System.Collections.Generic;

namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// What came of attaching: which TIA Portal, which project, or why neither.
    ///
    /// **A failure carries what was on the machine**, because the two reasons this fails look
    /// identical from the window and need opposite answers: no TIA Portal is running at all,
    /// or several are and none of them is the one that launched this. Listing what was found
    /// is the difference between "start TIA Portal" and "that one has no project open".
    /// </summary>
    public sealed class TiaAttachment
    {
        private static readonly string[] Nothing = new string[0];

        private TiaAttachment(
            int processId,
            string projectName,
            string projectDirectory,
            string problem,
            IReadOnlyList<string> considered)
        {
            ProcessId = processId;
            ProjectName = projectName;
            ProjectDirectory = projectDirectory;
            Problem = problem;
            Considered = considered ?? Nothing;
        }

        /// <summary>The TIA Portal this attached to, or 0 when it did not.</summary>
        public int ProcessId { get; }

        public string ProjectName { get; }

        /// <summary>Where the project lives, which is where <c>.plc-framework\</c> is.</summary>
        public string ProjectDirectory { get; }

        /// <summary>Why nothing was attached, or null when something was.</summary>
        public string Problem { get; }

        /// <summary>
        /// Every TIA Portal that was running when this ran, one line each. Populated whether
        /// it succeeded or not - it is what the window shows when it has to explain itself.
        /// </summary>
        public IReadOnlyList<string> Considered { get; }

        public bool Attached => Problem == null;

        /// <summary>
        /// The project was found but has never been saved, so it has no folder - and a core
        /// lives inside a project folder. Attached, and still not something to work with.
        /// </summary>
        public bool HasProjectFolder => Attached && !string.IsNullOrWhiteSpace(ProjectDirectory);

        public static TiaAttachment To(
            int processId, string projectName, string projectDirectory, IReadOnlyList<string> considered) =>
            new TiaAttachment(processId, projectName, projectDirectory, null, considered);

        public static TiaAttachment Failed(string problem, IReadOnlyList<string> considered = null) =>
            new TiaAttachment(0, null, null, problem ?? "TIA Portal could not be reached.", considered);
    }
}
