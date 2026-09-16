using System.Globalization;

namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// One TIA Portal that was running when this window looked: which process, and which
    /// project it has open.
    ///
    /// **It carries the project path rather than only a sentence about it**, which is what
    /// turned the failure panel from a dead end into a choice. The window used to be handed
    /// lines of text and could only print them; matching on the project, and offering the list
    /// to pick from, both need the parts.
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
        /// back blank are the same fact, and the two spellings would disagree in both places
        /// that read this: the line would print with nothing after the dash, and a project
        /// filter would have an empty value to compare against. The same rule the map filter
        /// had to learn about a missing programming language.
        /// </summary>
        public string Project { get; }

        /// <summary>Why the project could not be read, or null. Shown instead of the path.</summary>
        public string Problem { get; }

        public static RunningPortal With(int id, string project) =>
            new RunningPortal(id, string.IsNullOrWhiteSpace(project) ? null : project.Trim(), null);

        /// <summary>
        /// An instance that would not say what it has open - starting up, or shutting down.
        /// **Listed anyway**: it is running, the operator can see it, and leaving it out of a
        /// list of what was found would make the list disagree with the machine.
        /// </summary>
        public static RunningPortal Unreadable(int id) =>
            new RunningPortal(id, null, "its project could not be read");

        /// <summary>
        /// What the window shows, and - because this is what a list box binds - what a screen
        /// reader and a test read too. The same trap `GroupNode` and `DataBlockItem` both hit.
        /// </summary>
        public override string ToString() =>
            string.Format(
                CultureInfo.CurrentCulture,
                "process {0} - {1}",
                Id,
                Problem ?? Project ?? "no project open");
    }
}
