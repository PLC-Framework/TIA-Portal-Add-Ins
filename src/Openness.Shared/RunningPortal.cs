using System.Globalization;

namespace Openness.Shared
{
    /// <summary>
    /// One TIA Portal that was running when a satellite looked: which process, and which
    /// project it has open.
    ///
    /// **It carries the project path rather than only a sentence about it**, which is what
    /// turned the core updater's failure panel from a dead end into a choice: matching on the
    /// project, and offering the list to pick from, both need the parts.
    /// </summary>
    public sealed class RunningPortal
    {
        private RunningPortal(int id, string project, string problem)
        {
            Id = id;
            Project = project;
            Problem = problem;
        }

        public int Id { get; }

        /// <summary>
        /// The project file it has open, or null for none and for one it would not say.
        ///
        /// **Never the empty string.** An instance with nothing open and one whose path came
        /// back blank are the same fact, and two spellings of it would disagree wherever this
        /// is printed or compared.
        /// </summary>
        public string Project { get; }

        /// <summary>Why the project could not be read, or null. Shown instead of the path.</summary>
        public string Problem { get; }

        public static RunningPortal With(int id, string project) =>
            new RunningPortal(id, string.IsNullOrWhiteSpace(project) ? null : project.Trim(), null);

        /// <summary>
        /// An instance that would not say what it has open - starting up, or shutting down.
        /// **Listed anyway**: it is running, the operator can see it, and a list of what was
        /// found that disagrees with the machine is worse than one with a gap.
        /// </summary>
        public static RunningPortal Unreadable(int id) =>
            new RunningPortal(id, null, "its project could not be read");

        /// <summary>
        /// What a window shows, and - because this is what a list box binds - what a screen
        /// reader and a test read too.
        /// </summary>
        public override string ToString() =>
            string.Format(
                CultureInfo.CurrentCulture,
                "process {0} - {1}",
                Id,
                Problem ?? Project ?? "no project open");
    }
}
