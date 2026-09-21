using System;
using System.Collections.Generic;

using Core.DependencyGraph;
using Core.Repo.PlcCore;

using Core.Repo.PlcProject;

namespace Core.Repo
{
    /// <summary>
    /// What a TIA project holds, held against the core it is built on.
    ///
    /// **Metadata only**, which the maintainer settled in those words: a version, a status and
    /// a family, never a line of code. Comparing sources would mean deciding what a formatting
    /// difference means, and the question this answers - *is this block the one the core
    /// stands behind* - is answered by what the block says about itself.
    ///
    /// **`core.json` is the source of truth.** A block claiming a `core/` family that the graph
    /// does not list is not part of the core, whatever it looks like; it comes back as
    /// <see cref="Finding.UnknownVersion"/> with no versions beside it rather than being quietly
    /// believed.
    ///
    /// **Nothing here is written to disk.** `project.json` is the durable half - a snapshot of
    /// the project that took minutes to read - and this is recomputed from it and from the
    /// copied core in the time it takes to open a window.
    ///
    /// **Pure**: it is handed a catalogue and a map and opens nothing. That is what lets it be
    /// exercised against the real 264-node core with no TIA Portal anywhere near it.
    /// </summary>
    public sealed class CoreComparison
    {
        private static readonly ComparedObject[] NoObjects = new ComparedObject[0];
        private static readonly CoreNodeState[] NoStates = new CoreNodeState[0];

        private CoreComparison(
            IReadOnlyList<ComparedObject> objects,
            IReadOnlyList<CoreNodeState> repository,
            bool absentKnown)
        {
            Objects = objects;
            Repository = repository;
            AbsentKnown = absentKnown;

            List<Node> absent = new List<Node>();

            foreach (CoreNodeState one in repository)
                if (one.State == NodeState.Absent) absent.Add(one.Node);

            Absent = absent;
        }

        /// <summary>One entry per mapped object, in the order the map lists them.</summary>
        public IReadOnlyList<ComparedObject> Objects { get; }

        /// <summary>
        /// Every version the core still stands behind, and what the project has of it - in the
        /// core's own folder order.
        ///
        /// **The whole core, not only the gap**, because what a window offers to import is not
        /// only what is missing: a block held at a retired version, or one sitting where its
        /// family does not otherwise live, is downloaded again too. A list of the absent alone
        /// could not offer either.
        ///
        /// **Only what is current.** A retired version is not something to offer, and the one
        /// place it still shows is beside the object that holds it, saying so.
        /// </summary>
        public IReadOnlyList<CoreNodeState> Repository { get; }

        /// <summary>
        /// Every version the core still stands behind that the project does not have at all -
        /// <see cref="Repository"/> narrowed to <see cref="NodeState.Absent"/>.
        ///
        /// **A block you hold at an older version is not absent** - it is outdated, and it is in
        /// <see cref="Objects"/> saying so. This is the other question: what the core offers that
        /// the project has never taken.
        /// </summary>
        public IReadOnlyList<Node> Absent { get; }

        /// <summary>
        /// Whether <see cref="Absent"/> means anything.
        ///
        /// **False when the map was filtered, and then the list is empty rather than wrong.**
        /// A map of the FBs alone cannot tell "the project has no UDTs" from "this map did not
        /// look for them", and answering the first would report 91 data types as missing from a
        /// project that has every one of them. It is the same reason `project.json` records its
        /// filter at all.
        /// </summary>
        public bool AbsentKnown { get; }

        public static CoreComparison Of(PlcCoreCatalog core, ProjectMap map)
        {
            List<ComparedObject> objects = new List<ComparedObject>();

            if (core == null || map == null || map.Objects == null)
                return new CoreComparison(NoObjects, NoStates, false);

            foreach (ProjectObject found in map.Objects)
                if (found != null) objects.Add(Compare(core, found));

            MarkMisplaced(objects);

            bool narrowed = map.Filter != null && map.Filter.Narrows;

            return new CoreComparison(objects, Offered(core, objects, narrowed), !narrowed);
        }

        /// <summary>How many entries carry one finding.</summary>
        public int Count(Finding finding)
        {
            int found = 0;

            foreach (ComparedObject one in Objects)
                if (one.Is(finding)) found++;

            return found;
        }

        /// <summary>Entries with nothing to say against them.</summary>
        public int Clean
        {
            get
            {
                int found = 0;

                foreach (ComparedObject one in Objects)
                    if (one.Findings.Count == 0) found++;

                return found;
            }
        }

        // ---- One object --------------------------------------------------------------------

        private static ComparedObject Compare(PlcCoreCatalog core, ProjectObject found)
        {
            List<Finding> findings = new List<Finding>();

            // A block with no metadata of any kind is not being judged against the core - it is
            // the plant's own, which in a real project is most of what is there.
            if (!found.FromCore)
            {
                findings.Add(Finding.NotFromCore);

                return new ComparedObject(found, null, null, null, null, findings);
            }

            // **The TITLE decides and the native header is the fallback**, because the TITLE is
            // the contract the core's own generator writes - and because a PLC data type has no
            // header at all in either TIA version, so for 91 of the core's 249 sources there is
            // nothing else to read.
            string version = Version(found);

            // The two disagreeing is its own answer. Reporting such a block as up to date, or as
            // outdated, would pick one of two things it says about itself and hide that it says
            // both - and what it needs is an edit, not an import.
            if (Some(found.Version) && Some(found.HeaderVersion) && !Same(found.Version, found.HeaderVersion))
                findings.Add(Finding.Disagrees);

            IReadOnlyList<Node> versions = core.ByBase(found.Name);
            Node node = Match(versions, version);

            if (node == null)
            {
                // Either the core has never heard of the name - `versions` is empty - or it has
                // and not at this version. One finding, and the list beside it tells them apart
                // without a second name for the same fact.
                findings.Add(Finding.UnknownVersion);

                return new ComparedObject(found, null, null, version, versions, findings);
            }

            Node replacement = null;

            if (PlcCoreStatus.IsDeprecated(node.Status))
            {
                findings.Add(Finding.Outdated);
                replacement = core.ById(node.DeprecatedBy);
            }

            // A status that is neither current nor deprecated is the *core's* problem, and
            // `PlcCoreValidator` reports it with the node's id. Inventing a finding here would say
            // it twice, in a panel about the project, where nobody can act on it.

            return new ComparedObject(found, node, replacement, version, versions, findings);
        }

        // ---- Where a family sits ------------------------------------------------------------

        /// <summary>
        /// Families the project keeps in more than one folder, marking their members as it goes.
        ///
        /// **It does not say which copy is in the wrong place, and it cannot.** The core's
        /// `family` is a path in the *repository* - `core/adt/node` - while a project folder is
        /// whatever `config.json`'s hierarchy calls it, `Program blocks/03-ALL`. There is no
        /// mapping between the two, and inventing one would be a rule about somebody's folder
        /// names living inside a binary.
        ///
        /// What needs no mapping is that a family belongs together: two blocks of `core/adt/node`
        /// in two different folders is a discrepancy whatever the folders are called, and it is
        /// exactly the shape of "downloaded into the wrong place". **No majority is taken** -
        /// with two blocks in two folders there is no majority, and guessing which is the odd one
        /// out would be a claim this layer has nothing to base on.
        ///
        /// **A family is compared within one tree, and that was a bug for an hour.** `core/adt/node`
        /// holds an FB *and* the UDT it works on, and TIA files those in `Program blocks\…` and
        /// `PLC data types\…` - two folders, by construction, for every family that spans both.
        /// The first version reported every one of them as split. So the grouping carries the
        /// folder's first segment, which is TIA's own word for the tree and is only ever compared
        /// with itself.
        /// </summary>
        private static void MarkMisplaced(IReadOnlyList<ComparedObject> objects)
        {
            foreach (ComparedObject one in objects)
            {
                if (one.Found == null || !Some(one.Expected)) continue;

                if (!SamePath(Inside(one.Found.Folder), one.Expected)) one.Add(Finding.Misplaced);
            }
        }

        /// <summary>
        /// A project folder with its TIA tree taken off: <c>Program blocks/core/adt/queue</c>
        /// becomes <c>core/adt/queue</c>, which is the form a family is written in.
        ///
        /// **The tree is stripped, never compared.** `Program blocks` follows the interface
        /// language, so holding it against a literal would make this wrong in German; only what
        /// comes after it is held against a family the core wrote.
        /// </summary>
        internal static string Inside(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return string.Empty;

            int slash = folder.IndexOf(Places.Separator, StringComparison.Ordinal);

            return slash < 0 ? string.Empty : folder.Substring(slash + 1);
        }

        internal static bool SamePath(string left, string right) =>
            string.Equals(
                (left ?? string.Empty).Trim('/'), (right ?? string.Empty).Trim('/'),
                StringComparison.OrdinalIgnoreCase);

        // ---- What the project has not got ---------------------------------------------------

        /// <summary>
        /// What the core still stands behind, each node saying what the project has of it.
        ///
        /// **A filtered map cannot answer "absent"**, so under one a node nothing matched is
        /// <see cref="NodeState.Unknown"/> rather than missing. What the map *did* cover is still
        /// answered - if a block is in there, it is in the project whatever else was skipped.
        /// </summary>
        private static IReadOnlyList<CoreNodeState> Offered(
            PlcCoreCatalog core, IReadOnlyList<ComparedObject> objects, bool narrowed)
        {
            Dictionary<string, ProjectObject> held =
                new Dictionary<string, ProjectObject>(StringComparer.OrdinalIgnoreCase);

            foreach (ComparedObject one in objects)
            {
                if (one.Found == null || !Some(one.Found.Name)) continue;

                string name = one.Found.Name.Trim();

                // The first wins: a project holding one name twice is a discrepancy of its own,
                // and picking between them is not this list's question.
                if (!held.ContainsKey(name)) held.Add(name, one.Found);
            }

            List<CoreNodeState> offered = new List<CoreNodeState>();

            foreach (Node node in core.Nodes)
            {
                // Only what the core still stands behind. Offering a retired version would be
                // offering to import something the core has itself withdrawn.
                if (node == null || !PlcCoreStatus.IsCurrent(node.Status) || !Some(node.Base)) continue;

                ProjectObject found;
                NodeState state;

                if (!held.TryGetValue(node.Base, out found))
                {
                    found = null;
                    state = narrowed ? NodeState.Unknown : NodeState.Absent;
                }
                else
                {
                    state = Same(Version(found), node.Version) ? NodeState.Held : NodeState.AtAnotherVersion;
                }

                offered.Add(new CoreNodeState(node, Folder(core, node), state, found));
            }

            offered.Sort((left, right) =>
            {
                int byFolder = string.Compare(left.Folder, right.Folder, StringComparison.OrdinalIgnoreCase);

                if (byFolder != 0) return byFolder;

                int bySource = Rank(left.Node).CompareTo(Rank(right.Node));

                return bySource != 0
                    ? bySource
                    : string.Compare(left.Node.Base, right.Node.Base, StringComparison.OrdinalIgnoreCase);
            });

            return offered;
        }

        /// <summary>
        /// The order inside one folder: **the blocks, then the data types, then the enumerations**
        /// - `.scl`, `.udt`, `.xlsx`, which is the order the maintainer reads a family in. By name
        /// alone they came out interleaved, `_queue` between `EQueueMethod` and `queueItem`, so a
        /// folder read as a list of names rather than as a function and what it works on.
        ///
        /// **The extension is the core's own answer**, as it is for the import: the repository
        /// writes one kind per extension and `core.json` carries the file. Anything else follows,
        /// by name, rather than being dropped from a panel that has to show the whole core.
        /// </summary>
        private static int Rank(Node node)
        {
            string extension = (System.IO.Path.GetExtension(node.File ?? string.Empty) ?? string.Empty)
                .ToLowerInvariant();

            switch (extension)
            {
                case ".scl": return 0;
                case ".udt": return 1;
                case ".xlsx": return 2;
                default: return 3;
            }
        }

        /// <summary>
        /// The folder a node's source sits in, inside the core - <c>node</c>, <c>adt/queue</c>,
        /// or empty at the root.
        ///
        /// **Taken from the node's own file, not from a family written in a TITLE.** `core.json`
        /// is the source of truth and it carries the path; the family is what a *block* says
        /// about itself, and the two agreeing on all 264 nodes is a fact about today's core
        /// rather than something to depend on here.
        /// </summary>
        private static string Folder(PlcCoreCatalog core, Node node)
        {
            string inside = core.InsideCore(node);

            if (inside == null) return string.Empty;

            int slash = inside.LastIndexOf(Places.Separator, StringComparison.Ordinal);

            return slash < 0 ? string.Empty : inside.Substring(0, slash);
        }

        /// <summary>The version an object is judged by: its TITLE's, or TIA's header.</summary>
        private static string Version(ProjectObject found) =>
            Some(found.Version) ? found.Version.Trim() : Trimmed(found.HeaderVersion);

        // ---- Plumbing -------------------------------------------------------------------------

        private static Node Match(IReadOnlyList<Node> versions, string version)
        {
            if (!Some(version)) return null;

            foreach (Node node in versions)
                if (Same(node.Version, version)) return node;

            return null;
        }

        /// <summary>
        /// Whether two versions are the same one.
        ///
        /// **Compared as versions where both parse**, so `3.0` and `3.0.0` are one version
        /// rather than two. They arrive from different places - the TITLE writes what somebody
        /// typed, `PlcBlock.HeaderVersion` is a `System.Version` rendered back to a string - and
        /// a string compare would report every block in the project as disagreeing with itself.
        /// </summary>
        private static bool Same(string left, string right)
        {
            string one = Trimmed(left);
            string other = Trimmed(right);

            if (one == null || other == null) return one == other;

            System.Version parsedOne;
            System.Version parsedOther;

            if (System.Version.TryParse(one, out parsedOne) && System.Version.TryParse(other, out parsedOther))
                return Whole(parsedOne) == Whole(parsedOther);

            return string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A version with the components nobody wrote spelled as zero.
        ///
        /// **`System.Version` keeps "not given" as -1**, so `1.1` and `1.1.0` are *not* equal to
        /// it - and they are one version written by two hands, which is the only comparison this
        /// makes. Without this every block whose TIA header renders a build number disagreed with
        /// its own TITLE.
        /// </summary>
        private static System.Version Whole(System.Version version) =>
            new System.Version(
                version.Major,
                version.Minor,
                version.Build < 0 ? 0 : version.Build,
                version.Revision < 0 ? 0 : version.Revision);

        private static string Trimmed(string value) => Some(value) ? value.Trim() : null;

        private static bool Some(string value) => !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// What the comparison has to say about one object. **A list, not a verdict**: a block can
    /// be outdated *and* in the wrong folder at once, and collapsing that into one word would
    /// lose half of what has to be fixed.
    /// </summary>
    public enum Finding
    {
        /// <summary>
        /// The core marks this version <c>deprecated</c>. <see cref="ComparedObject.Replacement"/>
        /// is what its <c>deprecatedBy</c> names.
        ///
        /// **Nothing compares one version as greater than another.** The core says what it still
        /// stands behind, and a base with two live majors - which the real core had until it was
        /// tidied - is then two right answers rather than one outdated block.
        /// </summary>
        Outdated,

        /// <summary>
        /// The core does not define this version. <see cref="ComparedObject.Versions"/> holds the
        /// versions it does define, and is **empty when the core has never heard of the name at
        /// all** - which is what a block claiming a `core/` family it is not entitled to looks
        /// like.
        /// </summary>
        UnknownVersion,

        /// <summary>
        /// It is not in the folder its family names. <see cref="ComparedObject.Expected"/> says
        /// where it should be — <c>core/adt/queue</c>, under whichever TIA tree it belongs to.
        ///
        /// **This became answerable when the destination of a download was settled.** Until
        /// then the core's families and the project's folders had no correspondence, and the
        /// most that could be said was that a family sat in more than one place.
        /// </summary>
        Misplaced,

        /// <summary>
        /// The <c>TITLE</c> metadata and TIA's own <c>VERSION</c> header do not say the same
        /// thing. Neither is believed over the other here: the block says two things and what it
        /// needs is an edit.
        /// </summary>
        Disagrees,

        /// <summary>
        /// No metadata and no <c>core/</c> family - the plant's own block, which in a real
        /// project is most of them. It is not a problem; it is why everything is mapped.
        /// </summary>
        NotFromCore
    }

    /// <summary>One mapped object and what the core says about it.</summary>
    public sealed class ComparedObject
    {
        private static readonly Node[] NoNodes = new Node[0];

        private readonly List<Finding> _findings;

        internal ComparedObject(
            ProjectObject found,
            Node node,
            Node replacement,
            string version,
            IReadOnlyList<Node> versions,
            List<Finding> findings)
        {
            Found = found;
            Node = node;
            Replacement = replacement;
            Version = version;
            Versions = versions ?? NoNodes;
            _findings = findings ?? new List<Finding>();
        }

        /// <summary>What the map said, exactly as it said it.</summary>
        public ProjectObject Found { get; }

        /// <summary>The core node this matched, or null when none did.</summary>
        public Node Node { get; }

        /// <summary>What a deprecated node's <c>deprecatedBy</c> names, when the core holds it.</summary>
        public Node Replacement { get; }

        /// <summary>
        /// The version this was judged by - the <c>TITLE</c>'s, or TIA's header when there is no
        /// TITLE. Null for an object that is not from the core.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Every version of this name the core defines, in graph order. **Empty means the core
        /// does not define the name at all**, which is the other half of
        /// <see cref="Finding.UnknownVersion"/>.
        /// </summary>
        public IReadOnlyList<Node> Versions { get; }

        public IReadOnlyList<Finding> Findings => _findings;

        public string Name => Found?.Name;

        /// <summary>
        /// The folder its family names — <c>core/adt/queue</c> — which is where a download puts
        /// it, under whichever TIA tree it belongs to. Null for an object claiming no family,
        /// which is what a block of the plant looks like.
        ///
        /// **Read off the object, not off the matched node.** It therefore answers even for a
        /// version the core does not define, which is exactly a block somebody edited and left
        /// behind — one of the cases worth catching. The native `FAMILY` header stands in where
        /// there is no TITLE, which in V17–V20 is every block whose export refused.
        ///
        /// **Only for an object that comes from the core**, and a header only when it is written
        /// as a family. The first version read `FAMILY` off anything, so a block of the plant
        /// whose header said `cyanezf` was reported as misplaced for not being in a folder called
        /// `cyanezf` — a finding against every block somebody wrote. Found by a test.
        /// </summary>
        public string Expected
        {
            get
            {
                if (Found == null || !Found.FromCore) return null;

                if (!string.IsNullOrWhiteSpace(Found.Family)) return Found.Family.Trim();

                return Found.HeaderFamily != null &&
                       Found.HeaderFamily.StartsWith("core/", StringComparison.OrdinalIgnoreCase)
                    ? Found.HeaderFamily.Trim()
                    : null;
            }
        }

        public bool Is(Finding finding) => _findings.Contains(finding);

        /// <summary>Whether the core has nothing to say against this one.</summary>
        public bool Clean => _findings.Count == 0;

        internal void Add(Finding finding)
        {
            if (!_findings.Contains(finding)) _findings.Add(finding);
        }
    }

    /// <summary>What the project has of one version the core still stands behind.</summary>
    public enum NodeState
    {
        /// <summary>The project holds this exact version.</summary>
        Held,

        /// <summary>
        /// The project holds this name at some other version - which may be a retired one, or one
        /// the core does not define at all. **Not called "older"**: nothing here compares one
        /// version as greater than another, and a project can perfectly well be ahead.
        /// </summary>
        AtAnotherVersion,

        /// <summary>The project has nothing of this name.</summary>
        Absent,

        /// <summary>
        /// The map was filtered and did not cover this, so whether the project has it is not
        /// something this map can answer. **A state of its own rather than "absent"**, because
        /// reporting 91 data types as missing from a project holding every one is the silent hole
        /// the filter was recorded to prevent.
        /// </summary>
        Unknown
    }

    /// <summary>One current core node, and what the project has of it.</summary>
    public sealed class CoreNodeState
    {
        internal CoreNodeState(Node node, string folder, NodeState state, ProjectObject found)
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
