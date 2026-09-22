using System;
using System.Collections.Generic;

using Core.Repo.PlcCore;
using Core.Repo.PlcCore.Graph;
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
    /// project's copy of the core's graph in the time it takes to open a window.
    ///
    /// **Pure**: it is handed a catalogue and a map and opens nothing. That is what lets it be
    /// exercised against the real 264-node core with no TIA Portal anywhere near it.
    /// </summary>
    public sealed class PlcCoreComparison
    {
        private static readonly ComparedObject[] NoObjects = new ComparedObject[0];
        private static readonly PlcCoreNodeState[] NoStates = new PlcCoreNodeState[0];

        private PlcCoreComparison(
            IReadOnlyList<ComparedObject> objects,
            IReadOnlyList<PlcCoreNodeState> repository,
            bool absentKnown)
        {
            Objects = objects;
            Repository = repository;
            AbsentKnown = absentKnown;

            List<Node> absent = new List<Node>();

            foreach (PlcCoreNodeState one in repository)
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
        public IReadOnlyList<PlcCoreNodeState> Repository { get; }

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

        public static PlcCoreComparison Of(PlcCoreCatalog core, ProjectMap map)
        {
            List<ComparedObject> objects = new List<ComparedObject>();

            if (core == null || map == null || map.Objects == null)
                return new PlcCoreComparison(NoObjects, NoStates, false);

            foreach (ProjectObject found in map.Objects)
                if (found != null) objects.Add(Compare(core, found));

            MarkMisplaced(objects);

            bool narrowed = map.Filter != null && map.Filter.Narrows;

            return new PlcCoreComparison(objects, Offered(core, objects, narrowed), !narrowed);
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
        private static IReadOnlyList<PlcCoreNodeState> Offered(
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

            List<PlcCoreNodeState> offered = new List<PlcCoreNodeState>();

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

                offered.Add(new PlcCoreNodeState(node, Folder(core, node), state, found));
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
}
