using System;
using System.Collections.Generic;
using System.IO;

using Core.DependencyGraph;

using CoreGraph = Core.DependencyGraph.DependencyGraph;

namespace Core.Repo
{
    /// <summary>
    /// A core that has been read: the graph, where it was read from, and the two lookups
    /// everything downstream needs.
    ///
    /// **`core.json` is the source of truth, and the decision is the maintainer's**: a file
    /// under the core folder that the graph does not list is not part of the core, whatever
    /// it looks like. So nothing here reads the folder to find nodes - the graph is the list,
    /// and the folder only answers where a node's source sits.
    ///
    /// **A block is found by its base name, not by its id.** A project block calls itself
    /// `_queue`; the core holds `_queue-v3.0`, and may hold three versions of it at once.
    /// Cross-checked against the real repository: **`base` equals `name` on all 264 nodes**,
    /// so the TIA symbol of a block *is* its base and the join needs no heuristic.
    ///
    /// > The graph type is aliased to `CoreGraph` because `Core.DependencyGraph` is both a
    /// > namespace and the type inside it, and an unqualified use resolves to the namespace.
    /// > Same trap as `AddIn.Core`, one layer down.
    /// </summary>
    public sealed class CoreCatalog
    {
        private static readonly Node[] NoNodes = new Node[0];
        private static readonly Edge[] NoEdges = new Edge[0];
        private static readonly Report[] NoReports = new Report[0];

        private readonly Dictionary<string, Node> _byId;
        private readonly Dictionary<string, List<Node>> _byBase;

        private CoreCatalog(CoreGraph graph, string coreFolder, string folderInRepository)
        {
            Graph = graph;
            CoreFolder = coreFolder;
            FolderInRepository = folderInRepository ?? string.Empty;

            _byId = new Dictionary<string, Node>(StringComparer.Ordinal);
            _byBase = new Dictionary<string, List<Node>>(StringComparer.OrdinalIgnoreCase);

            foreach (Node node in Nodes)
            {
                if (node == null) continue;

                // A duplicate id is a broken file, and CoreValidator reports it. Keeping the
                // first rather than throwing means a catalogue still loads and the report
                // says why, which is the whole reason loading and validating are separate.
                if (!string.IsNullOrEmpty(node.Id) && !_byId.ContainsKey(node.Id)) _byId.Add(node.Id, node);

                if (string.IsNullOrEmpty(node.Base)) continue;

                List<Node> versions;
                if (!_byBase.TryGetValue(node.Base, out versions))
                {
                    versions = new List<Node>();
                    _byBase.Add(node.Base, versions);
                }

                versions.Add(node);
            }
        }

        public CoreGraph Graph { get; }

        /// <summary>The folder <c>core.json</c> was read from.</summary>
        public string CoreFolder { get; }

        /// <summary>
        /// The core's path inside its repository, forward slashes and all - because a node's
        /// <c>file</c> is relative to the repository root, not to the core folder, and this
        /// is the prefix that turns one into the other.
        /// </summary>
        public string FolderInRepository { get; }

        public IReadOnlyList<Node> Nodes => Graph.Nodes ?? (IReadOnlyList<Node>)NoNodes;

        public IReadOnlyList<Edge> Edges => Graph.Edges ?? (IReadOnlyList<Edge>)NoEdges;

        public IReadOnlyList<Report> Reports => Graph.Reports ?? (IReadOnlyList<Report>)NoReports;

        /// <summary>When the repository generated this graph, as it wrote it.</summary>
        public string GeneratedAt => Graph.GeneratedAt;

        public static CoreCatalog Of(CoreGraph graph, string coreFolder, string folderInRepository) =>
            graph == null ? null : new CoreCatalog(graph, coreFolder, folderInRepository);

        /// <summary>One node by its id - <c>_queue-v3.0</c> - or null.</summary>
        public Node ById(string id)
        {
            Node node;
            return id != null && _byId.TryGetValue(id, out node) ? node : null;
        }

        /// <summary>
        /// Every version of one block, in the order the graph lists them. Empty when the core
        /// has never heard of it, which is what "not in core.json" means.
        /// </summary>
        public IReadOnlyList<Node> ByBase(string baseName)
        {
            List<Node> versions;
            return baseName != null && _byBase.TryGetValue(baseName, out versions)
                ? (IReadOnlyList<Node>)versions
                : NoNodes;
        }

        /// <summary>
        /// Every version of a block the core still stands behind, in graph order.
        ///
        /// **There can be more than one, and that is not a defect.** `_mc_positioning1Axis`
        /// carries v1.0, v1.1 and v2.0 all current in the real core, because plants run
        /// different majors and none of them is retired. So this is the honest answer and
        /// <see cref="Current"/> is the convenience; whatever offers a block to import shows
        /// the choice rather than making it.
        /// </summary>
        public IReadOnlyList<Node> Currents(string baseName)
        {
            List<Node> found = new List<Node>();

            foreach (Node node in ByBase(baseName))
                if (CoreStatus.IsCurrent(node.Status)) found.Add(node);

            return found;
        }

        /// <summary>
        /// The one current version of a block, or null when the core has none **or several**.
        /// Null is "do not decide this here", not "there is nothing" - ask
        /// <see cref="Currents"/> to tell the two apart.
        /// </summary>
        public Node Current(string baseName)
        {
            IReadOnlyList<Node> currents = Currents(baseName);

            return currents.Count == 1 ? currents[0] : null;
        }

        /// <summary>
        /// Where a node's source sits inside <see cref="CoreFolder"/>, or null when the node's
        /// <c>file</c> does not sit under the core at all - which <see cref="CoreValidator"/>
        /// reports rather than this silently resolving to somewhere plausible.
        /// </summary>
        public string FileOf(Node node)
        {
            string inside = InsideCore(node);

            if (inside == null || CoreFolder == null) return null;

            try
            {
                return Path.Combine(CoreFolder, inside.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A node's <c>file</c> relative to the core folder, or null when it is not under it.
        /// Separate from <see cref="FileOf"/> because the validator needs the question
        /// answered without a path being built.
        /// </summary>
        internal string InsideCore(Node node)
        {
            if (node == null || string.IsNullOrEmpty(node.File)) return null;

            string file = node.File.Replace('\\', '/').TrimStart('/');

            if (FolderInRepository.Length == 0) return file;

            string prefix = FolderInRepository.Replace('\\', '/').Trim('/') + "/";

            return file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? file.Substring(prefix.Length)
                : null;
        }
    }

    /// <summary>
    /// The closed set a node's <c>status</c> comes from, spelled once.
    ///
    /// Taken verbatim from a hand-written TITLE, so anything outside the two is **unknown**
    /// rather than assumed current - a block whose status nobody can read is not a block to
    /// report as up to date.
    /// </summary>
    public static class CoreStatus
    {
        public const string Current = "current";
        public const string Deprecated = "deprecated";

        public static bool IsCurrent(string status) => string.Equals(status, Current, StringComparison.Ordinal);

        public static bool IsDeprecated(string status) => string.Equals(status, Deprecated, StringComparison.Ordinal);

        public static bool IsKnown(string status) => IsCurrent(status) || IsDeprecated(status);
    }
}
