using System;
using System.Collections.Generic;

using Core.Repo.PlcCore.Graph;
using Core.Repo.PlcProject;

namespace Core.Repo
{
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
}
