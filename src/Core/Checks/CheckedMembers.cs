using System.Collections.Generic;

namespace Core.Checks
{
    /// <summary>
    /// What an object was found to contain, or why that could not be read.
    ///
    /// **The two are one answer, because a caller must not be able to take the first without
    /// the second.** An empty list and a list that could not be read look identical in a
    /// report, and only one of them is a clean result.
    ///
    /// It exists because reading an interface is expensive - the Add-In exports the block to
    /// disk and parses it - so a <see cref="CheckedObject"/> can hand over a delegate that
    /// produces this instead of the members themselves. The checker then asks only for the
    /// objects whose matched rules actually expect something inside, which is what keeps the
    /// cost of a check proportional to what the configuration asks for rather than to the
    /// size of the project.
    /// </summary>
    public sealed class CheckedMembers
    {
        private CheckedMembers(IReadOnlyList<CheckedMember> members, string problem)
        {
            Members = members ?? new List<CheckedMember>();
            Problem = problem;
        }

        public static CheckedMembers Found(IReadOnlyList<CheckedMember> members) =>
            new CheckedMembers(members, null);

        /// <summary>
        /// Nothing could be read, and this says why in a sentence the operator can act on -
        /// a know-how protected block, a block that will not export, a folder that could not
        /// be written to.
        /// </summary>
        public static CheckedMembers Unreadable(string problem) =>
            new CheckedMembers(null, problem ?? "The interface could not be read.");

        public IReadOnlyList<CheckedMember> Members { get; }

        /// <summary>Why the members are not there. Null when they are.</summary>
        public string Problem { get; }
    }
}
