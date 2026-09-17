using System;
using System.Collections.Generic;

using Core.DependencyGraph;

namespace Core.Repo
{
    /// <summary>
    /// What a download would do, worked out before anything is written.
    ///
    /// **The plan exists so the operator can be asked.** Importing a block pulls its dependencies
    /// in with it, and a dependency the project already holds at another version is not a private
    /// matter: every block that calls it changes behaviour, including blocks nobody selected. So
    /// the plan says what it would touch and who else depends on it, and the window asks before
    /// any of it happens.
    ///
    /// **Pure.** It reads a catalogue and a comparison and opens nothing — which is what lets the
    /// whole of it be exercised against the real 264-node core with no TIA Portal anywhere near.
    /// </summary>
    public sealed class DownloadPlan
    {
        /// <summary>
        /// Deep enough for any dependency chain the core has, and a guarantee that a graph which
        /// somehow points back at itself cannot run forever.
        /// </summary>
        private const int MaxDepth = 64;

        private static readonly PlannedNode[] Nothing = new PlannedNode[0];

        private DownloadPlan(IReadOnlyList<PlannedNode> nodes, IReadOnlyList<PlannedNode> impact)
        {
            Nodes = nodes;
            Impact = impact;
        }

        /// <summary>
        /// Everything the download would touch, **dependencies before what needs them**. A UDT has
        /// to exist before the block whose interface names it, so the order is the plan's answer
        /// rather than something the importer has to work out again.
        /// </summary>
        public IReadOnlyList<PlannedNode> Nodes { get; }

        /// <summary>
        /// The entries that would change something the project already holds **and that other
        /// blocks depend on** — the ones worth stopping for.
        /// </summary>
        public IReadOnlyList<PlannedNode> Impact { get; }

        /// <summary>Whether the operator has to be asked before this runs.</summary>
        public bool NeedsConfirming => Impact.Count > 0;

        public static DownloadPlan Empty => new DownloadPlan(Nothing, Nothing);

        public int Count(DownloadAction action)
        {
            int found = 0;

            foreach (PlannedNode one in Nodes)
                if (one.Action == action) found++;

            return found;
        }

        /// <param name="chosen">The node ids ticked in the repository panel.</param>
        public static DownloadPlan Of(CoreCatalog core, CoreComparison compared, IEnumerable<string> chosen)
        {
            if (core == null || compared == null || chosen == null) return Empty;

            Dictionary<string, ProjectObject> held = Held(compared);
            Dictionary<string, PlannedNode> planned = new Dictionary<string, PlannedNode>(StringComparer.Ordinal);
            List<PlannedNode> ordered = new List<PlannedNode>();

            foreach (string id in chosen)
            {
                Node node = core.ById(id);

                if (node != null) Walk(core, node, true, held, planned, ordered, 0);
            }

            return new DownloadPlan(ordered, Affected(ordered, planned, compared));
        }

        /// <summary>
        /// One node and everything it needs, **depth first so a dependency is planned before the
        /// block that needs it**.
        ///
        /// A node reached twice — as a choice and as somebody's dependency — keeps the first
        /// answer, except that being *chosen* wins: a block the operator ticked is replaced even
        /// where it would have been skipped as an already-current dependency, because ticking it
        /// is what asking for it looks like.
        /// </summary>
        private static void Walk(
            CoreCatalog core,
            Node node,
            bool chosen,
            Dictionary<string, ProjectObject> held,
            Dictionary<string, PlannedNode> planned,
            List<PlannedNode> ordered,
            int depth)
        {
            if (node == null || string.IsNullOrEmpty(node.Id) || depth >= MaxDepth) return;

            PlannedNode already;

            if (planned.TryGetValue(node.Id, out already))
            {
                if (chosen) already.Choose(Action(node, held, true));

                return;
            }

            PlannedNode made = new PlannedNode(
                node, Folder(core, node), core.FileOf(node), chosen, Action(node, held, chosen), Found(node, held));

            // Recorded before descending, so a graph that points back at this node finds it and
            // stops rather than going round again.
            planned.Add(node.Id, made);

            foreach (Node needed in Needs(core, node))
                Walk(core, needed, false, held, planned, ordered, depth + 1);

            ordered.Add(made);
        }

        /// <summary>
        /// What a node depends on, **only where the edge says it resolved and is not external**.
        /// 147 of the real core's 296 edges are external — Siemens system blocks and things
        /// tracked elsewhere — and none of them is ours to import.
        /// </summary>
        private static IEnumerable<Node> Needs(CoreCatalog core, Node node)
        {
            List<Node> needed = new List<Node>();

            foreach (Edge edge in core.Edges)
            {
                if (edge == null || !edge.Resolved || edge.External) continue;
                if (!string.Equals(edge.From, node.Id, StringComparison.Ordinal)) continue;

                Node to = core.ById(edge.To);

                if (to != null) needed.Add(to);
            }

            return needed;
        }

        private static DownloadAction Action(Node node, Dictionary<string, ProjectObject> held, bool chosen)
        {
            ProjectObject found = Found(node, held);

            // Nothing of this name in the project: there is nothing to weigh up.
            if (found == null) return DownloadAction.Import;

            // Ticked is ticked. Somebody asking for a block they already have is asking for it
            // to be put back, which is how a misplaced or edited one gets repaired.
            if (chosen) return DownloadAction.Replace;

            // A dependency already at the version the core stands behind is left alone - the
            // cheapest right answer, and the one that keeps a download from touching what it has
            // no reason to.
            return SameVersion(found, node) ? DownloadAction.Skip : DownloadAction.Replace;
        }

        private static ProjectObject Found(Node node, Dictionary<string, ProjectObject> held)
        {
            ProjectObject found;

            return node.Base != null && held.TryGetValue(node.Base, out found) ? found : null;
        }

        /// <summary>
        /// Which planned entries would change something other blocks depend on.
        ///
        /// **A block already in the plan does not count as somebody else.** It is going to be
        /// replaced by the same download, so the version it used to want is not an argument
        /// against the version it is about to get.
        ///
        /// **Only blocks that declare their dependencies can be counted, and that is a real
        /// limit.** The project's dependency lists come from each block's TITLE, so a block of
        /// the plant calling a core function contributes nothing here. An empty answer therefore
        /// means "no *core* block depends on it", never "nothing does" - and whatever shows this
        /// has to say so rather than let it read as an all-clear.
        /// </summary>
        private static IReadOnlyList<PlannedNode> Affected(
            IReadOnlyList<PlannedNode> ordered,
            Dictionary<string, PlannedNode> planned,
            CoreComparison compared)
        {
            HashSet<string> inside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PlannedNode one in ordered)
                if (one.Node.Base != null) inside.Add(one.Node.Base);

            List<PlannedNode> affected = new List<PlannedNode>();

            foreach (PlannedNode one in ordered)
            {
                // Only a change to something already there can disturb anybody. An import of a
                // name the project has never had cannot.
                if (one.Action != DownloadAction.Replace || one.Found == null) continue;

                List<string> users = Users(compared, one.Node.Base, inside);

                if (users.Count == 0) continue;

                one.UsedBy(users);
                affected.Add(one);
            }

            return affected.Count == 0 ? Nothing : affected;
        }

        private static List<string> Users(CoreComparison compared, string name, HashSet<string> inside)
        {
            List<string> users = new List<string>();

            foreach (ComparedObject one in compared.Objects)
            {
                ProjectObject found = one.Found;

                if (found?.Dependencies == null || found.Name == null) continue;
                if (inside.Contains(found.Name)) continue;

                foreach (string needed in found.Dependencies)
                    if (string.Equals(needed, name, StringComparison.OrdinalIgnoreCase))
                    {
                        users.Add(found.Name);
                        break;
                    }
            }

            users.Sort(StringComparer.OrdinalIgnoreCase);

            return users;
        }

        private static Dictionary<string, ProjectObject> Held(CoreComparison compared)
        {
            Dictionary<string, ProjectObject> held =
                new Dictionary<string, ProjectObject>(StringComparer.OrdinalIgnoreCase);

            foreach (ComparedObject one in compared.Objects)
            {
                if (one.Found == null || string.IsNullOrWhiteSpace(one.Found.Name)) continue;

                string name = one.Found.Name.Trim();

                if (!held.ContainsKey(name)) held.Add(name, one.Found);
            }

            return held;
        }

        /// <summary>
        /// Where a node goes in the project: the folder its family names, <c>core/adt/queue</c>,
        /// under whichever TIA tree its kind belongs to.
        ///
        /// **Taken from the node's own file rather than from a family somebody typed**, because
        /// `core.json` is the source of truth and it carries the path. The two agree on all 264
        /// nodes of the real core, which is a fact about today's core rather than a contract.
        /// </summary>
        private static string Folder(CoreCatalog core, Node node)
        {
            string inside = core.InsideCore(node);

            if (inside == null) return RepoPaths.CoreFolder;

            int slash = inside.LastIndexOf(Places.Separator, StringComparison.Ordinal);

            return slash < 0
                ? RepoPaths.CoreFolder
                : RepoPaths.CoreFolder + Places.Separator + inside.Substring(0, slash);
        }

        private static bool SameVersion(ProjectObject found, Node node)
        {
            string held = string.IsNullOrWhiteSpace(found.Version) ? found.HeaderVersion : found.Version;

            if (string.IsNullOrWhiteSpace(held) || string.IsNullOrWhiteSpace(node.Version)) return false;

            System.Version one;
            System.Version other;

            if (System.Version.TryParse(held.Trim(), out one) &&
                System.Version.TryParse(node.Version.Trim(), out other))
                return Whole(one) == Whole(other);

            return string.Equals(held.Trim(), node.Version.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static System.Version Whole(System.Version version) =>
            new System.Version(
                version.Major, version.Minor,
                version.Build < 0 ? 0 : version.Build,
                version.Revision < 0 ? 0 : version.Revision);
    }

    /// <summary>What a download would do with one node.</summary>
    public enum DownloadAction
    {
        /// <summary>The project has nothing of this name.</summary>
        Import,

        /// <summary>The project has it, and this would write over it.</summary>
        Replace,

        /// <summary>A dependency already at the version the core stands behind: left alone.</summary>
        Skip
    }

    /// <summary>One node a download would touch.</summary>
    public sealed class PlannedNode
    {
        private static readonly string[] Nobody = new string[0];

        private IReadOnlyList<string> _users = Nobody;

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
        /// `CoreValidator` reports rather than this resolving to somewhere plausible.
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

        /// <summary>The version the project holds, when it holds one.</summary>
        public string HeldVersion =>
            Found == null ? null : (string.IsNullOrWhiteSpace(Found.Version) ? Found.HeaderVersion : Found.Version);

        internal void Choose(DownloadAction action)
        {
            Chosen = true;
            Action = action;
        }

        internal void UsedBy(IReadOnlyList<string> users) => _users = users;

        public override string ToString() => Node.Base + " v" + Node.Version + " — " + Action;
    }
}
