using System;
using System.Collections.Generic;

using Core.DependencyGraph;
using Core.Repo.PlcCore;

using Core.Repo.PlcProject;

namespace Core.Repo
{
    /// <summary>
    /// What a download would do, worked out before anything is written.
    ///
    /// **The plan exists so the operator can be asked.** Importing a block pulls its dependencies
    /// in with it, and TIA overwrites an object that is already there **without a word** -
    /// measured on the VM, where no refusal and no message came back. So whatever the plan would
    /// write over is something the operator has to see first, and choose: the window lists every
    /// entry with a tick box, says which of them the project already holds and where, and takes
    /// back exactly what was left ticked (<see cref="Taking"/>).
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

        private DownloadPlan(IReadOnlyList<PlannedNode> nodes)
        {
            Nodes = nodes;

            List<PlannedNode> collisions = new List<PlannedNode>();

            foreach (PlannedNode one in nodes)
                if (one.Found != null) collisions.Add(one);

            Collisions = collisions;
        }

        /// <summary>
        /// Everything the download would touch, **dependencies before what needs them**. A UDT has
        /// to exist before the block whose interface names it, so the order is the plan's answer
        /// rather than something the importer has to work out again.
        /// </summary>
        public IReadOnlyList<PlannedNode> Nodes { get; }

        /// <summary>
        /// The entries the project already holds something of that name for — what a download
        /// would write over, or leave alone, and in either case what the operator is shown first.
        /// </summary>
        public IReadOnlyList<PlannedNode> Collisions { get; }

        /// <summary>
        /// Whether the operator has to be asked before this runs: **whenever it meets anything
        /// the project already has**. TIA will not refuse the overwrite, so nothing else would.
        /// </summary>
        public bool Collides => Collisions.Count > 0;

        public static DownloadPlan Empty => new DownloadPlan(Nothing);

        public int Count(DownloadAction action)
        {
            int found = 0;

            foreach (PlannedNode one in Nodes)
                if (one.Action == action) found++;

            return found;
        }

        /// <param name="chosen">The node ids ticked in the repository panel.</param>
        public static DownloadPlan Of(PlcCoreCatalog core, PlcCoreComparison compared, IEnumerable<string> chosen)
        {
            if (core == null || compared == null || chosen == null) return Empty;

            Dictionary<string, ProjectObject> held = Held(compared);
            Dictionary<string, PlannedNode> planned = new Dictionary<string, PlannedNode>(StringComparer.Ordinal);
            List<PlannedNode> ordered = new List<PlannedNode>();

            foreach (string id in chosen)
            {
                Node node = core.ById(id);

                if (node != null) Walk(core, node, null, held, planned, ordered, 0);
            }

            Affected(ordered, compared);

            return new DownloadPlan(ordered);
        }

        /// <summary>
        /// The same plan with **exactly these entries taken, and every other one left as it is** —
        /// what the operator's ticks in the download window turn into.
        ///
        /// A ticked entry the project has nothing of is imported and one it holds is replaced,
        /// **whatever the plan first proposed**: a dependency already at the core's version is
        /// left alone by default, and ticking it is how it gets written again - which is what a
        /// second download of the same block used to be unable to do, bringing the block down and
        /// none of what it needs. An unticked entry stays in the plan as a skip rather than
        /// vanishing, so the report can still say it was left as it was.
        /// </summary>
        public DownloadPlan Taking(IEnumerable<string> ids)
        {
            HashSet<string> taken = new HashSet<string>(ids ?? new string[0], StringComparer.Ordinal);
            List<PlannedNode> nodes = new List<PlannedNode>();

            foreach (PlannedNode one in Nodes)
            {
                DownloadAction action = !taken.Contains(one.Node.Id)
                    ? DownloadAction.Skip
                    : one.Found == null ? DownloadAction.Import : DownloadAction.Replace;

                nodes.Add(one.With(action));
            }

            return new DownloadPlan(nodes);
        }

        /// <summary>
        /// One node and everything it needs, **depth first so a dependency is planned before the
        /// block that needs it**.
        ///
        /// A node reached twice — as a choice and as somebody's dependency — keeps the first
        /// answer, except that being *chosen* wins: a block the operator ticked is replaced even
        /// where it would have been skipped as an already-current dependency, because ticking it
        /// is what asking for it looks like.
        ///
        /// **Every node remembers who needs it**, however many paths reach it, so the download
        /// window can say why a dependency is on the list at all.
        /// </summary>
        /// <param name="by">The node this one was reached from, or null when it was ticked.</param>
        private static void Walk(
            PlcCoreCatalog core,
            Node node,
            PlannedNode by,
            Dictionary<string, ProjectObject> held,
            Dictionary<string, PlannedNode> planned,
            List<PlannedNode> ordered,
            int depth)
        {
            if (node == null || string.IsNullOrEmpty(node.Id) || depth >= MaxDepth) return;

            bool chosen = by == null;
            PlannedNode already;

            if (planned.TryGetValue(node.Id, out already))
            {
                if (chosen) already.Choose(Action(node, held, true));
                else already.NeededBy(by.Node.Base);

                return;
            }

            PlannedNode made = new PlannedNode(
                node, Folder(core, node), core.FileOf(node), chosen, Action(node, held, chosen), Found(node, held));

            if (!chosen) made.NeededBy(by.Node.Base);

            // Recorded before descending, so a graph that points back at this node finds it and
            // stops rather than going round again.
            planned.Add(node.Id, made);

            foreach (Node needed in Needs(core, node))
                Walk(core, needed, made, held, planned, ordered, depth + 1);

            ordered.Add(made);
        }

        /// <summary>
        /// What a node depends on, **only where the edge says it resolved and is not external**.
        /// 147 of the real core's 296 edges are external — Siemens system blocks and things
        /// tracked elsewhere — and none of them is ours to import.
        /// </summary>
        private static IEnumerable<Node> Needs(PlcCoreCatalog core, Node node)
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
        /// Who else in the project depends on each entry the project already holds — **every
        /// collision, not only what the plan would replace by default**, because the window lets
        /// the operator tick a skipped one too.
        ///
        /// **A block already in the plan does not count as somebody else.** It is going to be
        /// written by the same download, so the version it used to want is not an argument
        /// against the version it is about to get.
        ///
        /// **Only blocks that declare their dependencies can be counted, and that is a real
        /// limit.** The project's dependency lists come from each block's TITLE, so a block of
        /// the plant calling a core function contributes nothing here. An empty answer therefore
        /// means "no *core* block depends on it", never "nothing does" - and whatever shows this
        /// has to say so rather than let it read as an all-clear.
        /// </summary>
        private static void Affected(IReadOnlyList<PlannedNode> ordered, PlcCoreComparison compared)
        {
            HashSet<string> inside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PlannedNode one in ordered)
                if (one.Node.Base != null) inside.Add(one.Node.Base);

            foreach (PlannedNode one in ordered)
            {
                // Only something already there can have users. An import of a name the project
                // has never had cannot disturb anybody.
                if (one.Found == null) continue;

                List<string> users = Users(compared, one.Node.Base, inside);

                if (users.Count > 0) one.UsedBy(users);
            }
        }

        private static List<string> Users(PlcCoreComparison compared, string name, HashSet<string> inside)
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

        private static Dictionary<string, ProjectObject> Held(PlcCoreComparison compared)
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
        private static string Folder(PlcCoreCatalog core, Node node)
        {
            string inside = core.InsideCore(node);

            if (inside == null) return CoreFolderInTia;

            int slash = inside.LastIndexOf(Places.Separator, StringComparison.Ordinal);

            return slash < 0
                ? CoreFolderInTia
                : CoreFolderInTia + Places.Separator + inside.Substring(0, slash);
        }

        /// <summary>
        /// The folder inside the TIA project that every core object is placed under -
        /// <c>Program blocks/core/…</c>, <c>PLC data types/core/…</c> - which is the
        /// hierarchy's own <c>core</c> folder, the one <c>config.json</c> declares beside
        /// <c>custom</c>, <c>vendor</c> and <c>provisional</c>.
        ///
        /// **Its own constant, and it borrowed one until 2026-09-22.** It was spelled with
        /// <c>RepoPaths.CoreFolder</c>, the name of the folder the core used to be mirrored into
        /// under <c>repo\</c>: two unrelated folders that happened to share a word. Removing
        /// that mirror would have left this one naming nothing, or - worse - kept one constant
        /// meaning both, so that renaming either would put imported objects in the wrong folder
        /// with nothing failing.
        /// </summary>
        public const string CoreFolderInTia = "core";

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

        /// <summary>
        /// The project has it, and this would write over it - **and move it into the folder its
        /// family names** when it is anywhere else, which TIA would not do on its own.
        /// </summary>
        Replace,

        /// <summary>
        /// Left as it is: a dependency already at the version the core stands behind, by
        /// default, or anything the operator unticked when asked.
        /// </summary>
        Skip
    }

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
