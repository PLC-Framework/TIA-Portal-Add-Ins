using System;
using System.Collections.Generic;

using Core.Repo.PlcCore.Graph;
using Core.Repo.PlcProject;

namespace Core.Repo
{
    /// <summary>One node a download would touch.</summary>
    public sealed class PlannedNode
    {
        private static readonly string[] Nobody = new string[0];

        private IReadOnlyList<string> _users = Nobody;
        private readonly List<string> _neededBy = new List<string>();

        internal PlannedNode(
            Node node, string folder, string source, bool chosen, DownloadAction action, ProjectObject found)
        {
            Node = node;
            Folder = folder;
            Source = source;
            Chosen = chosen;
            Action = action;
            Found = found;
        }

        public Node Node { get; }

        /// <summary>Where it goes: <c>core/adt/queue</c>, under its TIA tree.</summary>
        public string Folder { get; }

        /// <summary>
        /// The source file **inside the project's own copy of the core**, never in the repository
        /// it came from. Null when the node's file does not sit under the core at all, which
        /// `PlcCoreValidator` reports rather than this resolving to somewhere plausible.
        /// </summary>
        public string Source { get; }

        /// <summary>Whether the operator ticked it, as against it coming along as a dependency.</summary>
        public bool Chosen { get; private set; }

        public DownloadAction Action { get; private set; }

        /// <summary>What the project holds of this name, or null.</summary>
        public ProjectObject Found { get; }

        /// <summary>
        /// Blocks in the project that depend on this one and are **not** themselves in the plan —
        /// what a replacement would change behind somebody's back. Empty is the ordinary answer.
        /// </summary>
        public IReadOnlyList<string> Users => _users;

        /// <summary>
        /// The planned entries that need this one — why a dependency is on the list at all.
        /// Empty for what the operator ticked and nothing else reaches.
        /// </summary>
        public IReadOnlyList<string> Needers => _neededBy;

        /// <summary>The version the project holds, when it holds one.</summary>
        public string HeldVersion =>
            Found == null ? null : (string.IsNullOrWhiteSpace(Found.Version) ? Found.HeaderVersion : Found.Version);

        /// <summary>Where the project keeps what it has of this name, as the map recorded it.</summary>
        public string From => Found?.Folder;

        /// <summary>
        /// Whether what the project holds sits somewhere other than the folder its family names.
        ///
        /// **TIA overwrites in place, so this is what a download has to do something about**:
        /// generating a block that already exists writes the new one wherever the old one was -
        /// measured on the VM - and a download meant to put the core's block where the core says
        /// would otherwise leave it exactly where it was found. The tree is stripped and never
        /// compared, the same rule the comparison's own "in the wrong folder" follows.
        /// </summary>
        public bool Moves =>
            Found != null && !PlcCoreComparison.SamePath(PlcCoreComparison.Inside(Found.Folder), Folder);

        internal void Choose(DownloadAction action)
        {
            Chosen = true;
            Action = action;
        }

        internal void UsedBy(IReadOnlyList<string> users) => _users = users;

        internal void NeededBy(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            foreach (string one in _neededBy)
                if (string.Equals(one, name, StringComparison.OrdinalIgnoreCase)) return;

            _neededBy.Add(name);
        }

        /// <summary>This entry with another action and everything else as it was.</summary>
        internal PlannedNode With(DownloadAction action)
        {
            PlannedNode copy = new PlannedNode(Node, Folder, Source, Chosen, action, Found) { _users = _users };

            copy._neededBy.AddRange(_neededBy);

            return copy;
        }

        public override string ToString() => Node.Base + " v" + Node.Version + " — " + Action;
    }
}
