using System;
using System.Collections.Generic;

using Core.Config.Validation;
using Core.DependencyGraph;

namespace Core.Repo
{
    /// <summary>
    /// What is wrong with a core that loaded.
    ///
    /// **The generator already reports most of this, and that is not a reason to skip it.**
    /// `core.json` is a committed artefact: it can be stale, it can be hand-edited, and it
    /// arrives here from a repository this machine does not control. Trusting the `reports`
    /// array inside a file to describe that same file is trusting the thing being checked.
    ///
    /// **Its issues are `ValidationIssue`s**, the same type the config validators produce, so
    /// a window that already shows one list shows this one. A second issue type would be the
    /// drift this project keeps writing down.
    ///
    /// Pure: handed a catalogue, it returns issues. It opens nothing, so it is safe inside
    /// TIA Portal and can be exercised against invented graphs.
    /// </summary>
    public static class CoreValidator
    {
        public static ValidationResult Validate(CoreCatalog catalog, string path = "core.json")
        {
            Issues issues = new Issues();

            if (!issues.RequiredObject(path, catalog)) return new ValidationResult(issues.All);

            issues.Required(Issues.Field(path, "generatedAt"), catalog.GeneratedAt);

            if (catalog.Graph.Nodes == null)
                issues.Add(Issues.Field(path, "nodes"), "Required. Use [] to say there are none.");

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            Nodes(catalog, Issues.Field(path, "nodes"), ids, issues);
            Deprecations(catalog, Issues.Field(path, "nodes"), ids, issues);
            Edges(catalog, Issues.Field(path, "edges"), ids, issues);

            return new ValidationResult(issues.All);
        }

        /// <summary>
        /// **Several current versions of one block is legal, and this deliberately says
        /// nothing about it.** It was an error here for about an hour, until the first run
        /// against the real core reported `_mc_positioning1Axis`, whose v1.0, v1.1 and v2.0
        /// are all current on purpose - plants run different majors and none of them is
        /// retired. Nothing in the format's contract says one base has one current version,
        /// and the generator already reports the case that genuinely breaks - an *unversioned*
        /// dependency that could mean either - as `ambiguous-dependency`. A rule invented here
        /// would have failed a core that is correct.
        /// </summary>
        private static void Nodes(
            CoreCatalog catalog,
            string path,
            HashSet<string> ids,
            Issues issues)
        {
            IReadOnlyList<Node> nodes = catalog.Nodes;

            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                string at = Issues.At(path, i);

                if (!issues.RequiredObject(at, node)) continue;

                if (issues.Required(Issues.Field(at, "id"), node.Id) && !ids.Add(node.Id))
                    issues.Add(Issues.Field(at, "id"), "'" + node.Id + "' is used by more than one node.");

                issues.Required(Issues.Field(at, "name"), node.Name);
                issues.Required(Issues.Field(at, "base"), node.Base);

                // The generator reports a name that does not match its file as a warning and
                // carries on, so a graph can arrive holding one. It matters here because the
                // TIA symbol of a project block is matched against `base`: if the two differ,
                // the block the core means cannot be found by the name the project uses.
                if (!string.IsNullOrEmpty(node.Base) && !string.IsNullOrEmpty(node.Name) &&
                    !string.Equals(node.Base, node.Name, StringComparison.Ordinal))
                    issues.Add(at, "base '" + node.Base + "' and name '" + node.Name + "' differ, so this block cannot be matched by the name a project uses.");

                if (issues.Required(Issues.Field(at, "file"), node.File) && catalog.InsideCore(node) == null)
                    issues.Add(Issues.Field(at, "file"),
                        "'" + node.File + "' does not sit under the core folder, so its source cannot be found.");

                if (!CoreStatus.IsKnown(node.Status))
                    issues.Add(Issues.Field(at, "status"),
                        "Must be one of: " + CoreStatus.Current + ", " + CoreStatus.Deprecated + ".");
            }
        }

        /// <summary>
        /// The two halves of a deprecation, which the file can carry only one of.
        ///
        /// A deprecated node with nowhere to go leaves an update with nothing to offer, and a
        /// current node carrying a replacement contradicts itself - both are broken files
        /// rather than broken machines, so they are structural.
        /// </summary>
        private static void Deprecations(CoreCatalog catalog, string path, HashSet<string> ids, Issues issues)
        {
            IReadOnlyList<Node> nodes = catalog.Nodes;

            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (node == null) continue;

                string at = Issues.Field(Issues.At(path, i), "deprecatedBy");

                if (CoreStatus.IsDeprecated(node.Status))
                {
                    if (!issues.Required(at, node.DeprecatedBy)) continue;

                    if (!ids.Contains(node.DeprecatedBy))
                        issues.Add(at, "'" + node.DeprecatedBy + "' is not a node in this core.");
                    else if (string.Equals(node.DeprecatedBy, node.Id, StringComparison.Ordinal))
                        issues.Add(at, "A node cannot be deprecated by itself.");
                }
                else if (CoreStatus.IsCurrent(node.Status) && !string.IsNullOrWhiteSpace(node.DeprecatedBy))
                {
                    issues.Add(at, "Set on a node whose status is '" + CoreStatus.Current + "'.");
                }
            }
        }

        private static void Edges(CoreCatalog catalog, string path, HashSet<string> ids, Issues issues)
        {
            IReadOnlyList<Edge> edges = catalog.Edges;

            for (int i = 0; i < edges.Count; i++)
            {
                Edge edge = edges[i];
                string at = Issues.At(path, i);

                if (!issues.RequiredObject(at, edge)) continue;

                if (issues.Required(Issues.Field(at, "from"), edge.From) && !ids.Contains(edge.From))
                    issues.Add(Issues.Field(at, "from"), "'" + edge.From + "' is not a node in this core.");

                // `to` is a node **only when the edge says it resolved**. An unresolved one
                // carries the raw name of a Siemens system block or of something tracked
                // elsewhere - which is what `external` and `kind` are for - so demanding a
                // node there would report 147 of this core's 296 edges as broken. Checking
                // the resolved ones is the half that means something: an edge claiming it
                // found its target, in a graph that no longer holds it, is a stale file.
                if (issues.Required(Issues.Field(at, "to"), edge.To) && edge.Resolved && !ids.Contains(edge.To))
                    issues.Add(Issues.Field(at, "to"),
                        "'" + edge.To + "' is marked resolved but is not a node in this core.");
            }
        }
    }
}
