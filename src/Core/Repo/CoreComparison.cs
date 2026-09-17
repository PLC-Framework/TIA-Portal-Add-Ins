using System;
using System.Collections.Generic;

using Core.DependencyGraph;

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
        private static readonly Node[] NoNodes = new Node[0];
        private static readonly SplitFamily[] NoFamilies = new SplitFamily[0];

        private CoreComparison(
            IReadOnlyList<ComparedObject> objects,
            IReadOnlyList<Node> absent,
            IReadOnlyList<SplitFamily> split,
            bool absentKnown)
        {
            Objects = objects;
            Absent = absent;
            Split = split;
            AbsentKnown = absentKnown;
        }

        /// <summary>One entry per mapped object, in the order the map lists them.</summary>
        public IReadOnlyList<ComparedObject> Objects { get; }

        /// <summary>
        /// Every version the core still stands behind that the project does not have at all.
        ///
        /// **A block you hold at an older version is not absent** - it is outdated, and it is in
        /// <see cref="Objects"/> saying so. This is the other question: what the core offers that
        /// the project has never taken.
        /// </summary>
        public IReadOnlyList<Node> Absent { get; }

        /// <summary>
        /// Families the project keeps in more than one folder - see <see cref="Finding.Misplaced"/>.
        /// </summary>
        public IReadOnlyList<SplitFamily> Split { get; }

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

        public static CoreComparison Of(CoreCatalog core, ProjectMap map)
        {
            List<ComparedObject> objects = new List<ComparedObject>();

            if (core == null || map == null || map.Objects == null)
                return new CoreComparison(NoObjects, NoNodes, NoFamilies, false);

            foreach (ProjectObject found in map.Objects)
                if (found != null) objects.Add(Compare(core, found));

            IReadOnlyList<SplitFamily> split = MarkSplits(objects);

            bool narrowed = map.Filter != null && map.Filter.Narrows;

            return new CoreComparison(
                objects,
                narrowed ? NoNodes : Missing(core, objects),
                split,
                !narrowed);
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

        private static ComparedObject Compare(CoreCatalog core, ProjectObject found)
        {
            List<Finding> findings = new List<Finding>();

            // A block with no metadata of any kind is not being judged against the core - it is
            // the plant's own, which in a real project is most of what is there.
            if (!found.FromCore)
            {
                findings.Add(Finding.NotFromCore);

                return new ComparedObject(found, null, null, null, NoNodes, findings);
            }

            // **The TITLE decides and the native header is the fallback**, because the TITLE is
            // the contract the core's own generator writes - and because a PLC data type has no
            // header at all in either TIA version, so for 91 of the core's 249 sources there is
            // nothing else to read.
            string version = Some(found.Version) ? found.Version.Trim() : Trimmed(found.HeaderVersion);

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

            if (CoreStatus.IsDeprecated(node.Status))
            {
                findings.Add(Finding.Outdated);
                replacement = core.ById(node.DeprecatedBy);
            }

            // A status that is neither current nor deprecated is the *core's* problem, and
            // `CoreValidator` reports it with the node's id. Inventing a finding here would say
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
        private static IReadOnlyList<SplitFamily> MarkSplits(IReadOnlyList<ComparedObject> objects)
        {
            Dictionary<string, Group> groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);

            foreach (ComparedObject one in objects)
            {
                string family = Family(one);

                if (family == null) continue;

                string folder = one.Found.Folder ?? string.Empty;
                string key = Key(family, Tree(folder));

                Group group;

                if (!groups.TryGetValue(key, out group))
                {
                    group = new Group(family, Tree(folder));
                    groups.Add(key, group);
                }

                if (!Holds(group.Folders, folder)) group.Folders.Add(folder);
            }

            List<SplitFamily> split = new List<SplitFamily>();

            foreach (KeyValuePair<string, Group> one in groups)
                if (one.Value.Folders.Count > 1)
                    split.Add(new SplitFamily(one.Value.Family, one.Value.Tree, one.Value.Folders));

            if (split.Count == 0) return NoFamilies;

            foreach (ComparedObject one in objects)
            {
                string family = Family(one);

                if (family == null) continue;

                Group group;

                if (groups.TryGetValue(Key(family, Tree(one.Found.Folder ?? string.Empty)), out group) &&
                    group.Folders.Count > 1)
                    one.Add(Finding.Misplaced);
            }

            split.Sort((left, right) =>
            {
                int byFamily = string.Compare(left.Family, right.Family, StringComparison.Ordinal);

                return byFamily != 0 ? byFamily : string.Compare(left.Tree, right.Tree, StringComparison.Ordinal);
            });

            return split;
        }

        private static string Family(ComparedObject one)
        {
            string family = one.Found == null ? null : one.Found.Family;

            return Some(family) ? family.Trim() : null;
        }

        /// <summary>
        /// The folder's first segment - <c>Program blocks</c>, <c>PLC data types</c> - which is
        /// TIA's own name for the tree an object lives in. **Never held against a literal**: it
        /// follows the interface language, and the only thing done with it is comparing it to
        /// another object's.
        /// </summary>
        private static string Tree(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return string.Empty;

            int slash = folder.IndexOf(Places.Separator, StringComparison.Ordinal);

            return slash < 0 ? folder : folder.Substring(0, slash);
        }

        private static string Key(string family, string tree) => family + " " + tree;

        private sealed class Group
        {
            public Group(string family, string tree)
            {
                Family = family;
                Tree = tree;
            }

            public string Family { get; }

            public string Tree { get; }

            public List<string> Folders { get; } = new List<string>();
        }

        // ---- What the project has not got ---------------------------------------------------

        private static IReadOnlyList<Node> Missing(CoreCatalog core, IReadOnlyList<ComparedObject> objects)
        {
            HashSet<string> held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ComparedObject one in objects)
                if (one.Found != null && Some(one.Found.Name)) held.Add(one.Found.Name.Trim());

            List<Node> absent = new List<Node>();

            foreach (Node node in core.Nodes)
            {
                // Only what the core still stands behind. Listing a retired version as missing
                // would offer to import something the core has itself withdrawn.
                if (node == null || !CoreStatus.IsCurrent(node.Status)) continue;

                if (!Some(node.Base) || held.Contains(node.Base)) continue;

                absent.Add(node);
            }

            return absent;
        }

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

        private static bool Holds(List<string> values, string value)
        {
            foreach (string one in values)
                if (string.Equals(one, value, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

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
        /// This object's family is kept in more than one folder in the project. See
        /// <see cref="CoreComparison.Split"/> for which folders, and why this does not name one
        /// of them as the wrong one.
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

        public bool Is(Finding finding) => _findings.Contains(finding);

        /// <summary>Whether the core has nothing to say against this one.</summary>
        public bool Clean => _findings.Count == 0;

        internal void Add(Finding finding)
        {
            if (!_findings.Contains(finding)) _findings.Add(finding);
        }
    }

    /// <summary>One family the project keeps in more than one folder of the same tree.</summary>
    public sealed class SplitFamily
    {
        internal SplitFamily(string family, string tree, IReadOnlyList<string> folders)
        {
            Family = family;
            Tree = tree;
            Folders = folders;
        }

        /// <summary>As the TITLE writes it: <c>core/adt/node</c>.</summary>
        public string Family { get; }

        /// <summary>
        /// The tree these folders are in, as TIA names it - <c>Program blocks</c>. A family
        /// holding a block and the data type it works on is in two trees on purpose, so the
        /// split is only ever reported within one.
        /// </summary>
        public string Tree { get; }

        /// <summary>The project folders it is spread over, in the order they were met.</summary>
        public IReadOnlyList<string> Folders { get; }

        public override string ToString() => Family + " in " + Folders.Count + " folders of " + Tree;
    }
}
