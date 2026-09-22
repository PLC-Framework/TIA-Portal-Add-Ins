using Core.Repo.PlcCore.Graph;
using Core.Repo.PlcProject;

namespace Core.Repo
{
    /// <summary>One current core node, and what the project has of it.</summary>
    public sealed class PlcCoreNodeState
    {
        internal PlcCoreNodeState(Node node, string folder, NodeState state, ProjectObject found)
        {
            Node = node;
            Folder = folder ?? string.Empty;
            State = state;
            Found = found;
        }

        public Node Node { get; }

        /// <summary>
        /// Where its source sits inside the core - <c>node</c>, <c>adt/queue</c>, empty at the
        /// root. The repository's own folders, which is what the panel is a tree of.
        /// </summary>
        public string Folder { get; }

        public NodeState State { get; }

        /// <summary>What the project holds of this name, or null when it holds nothing.</summary>
        public ProjectObject Found { get; }

        /// <summary>The version the project holds, when it holds another one.</summary>
        public string HeldVersion =>
            Found == null ? null : (string.IsNullOrWhiteSpace(Found.Version) ? Found.HeaderVersion : Found.Version);

        public override string ToString() => Node.Base + " v" + Node.Version;
    }
}
