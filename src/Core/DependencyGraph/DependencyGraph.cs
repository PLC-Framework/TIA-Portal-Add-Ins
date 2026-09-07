using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.DependencyGraph
{
    /// <summary>
    /// Root object of the dependency file (core.json), one graph per PLC family.
    /// Mirrors the Graph model of tools/dependency_graph_builder.
    /// </summary>
    [DataContract]
    public class DependencyGraph
    {
        /// <summary>
        /// ISO-8601 with offset, second precision — e.g. "2026-07-22T17:29:54+00:00".
        ///
        /// Kept as a string on purpose: DataContractJsonSerializer only understands the
        /// legacy "\/Date(ticks)\/" form and throws a SerializationException on ISO-8601,
        /// aborting the whole document. Parse it explicitly if a DateTime is needed.
        /// </summary>
        [DataMember(Name = "generatedAt")]
        public string GeneratedAt { get; set; }

        [DataMember(Name = "nodes")]
        public List<Node> Nodes { get; set; }

        [DataMember(Name = "edges")]
        public List<Edge> Edges { get; set; }

        [DataMember(Name = "reports")]
        public List<Report> Reports { get; set; }
    }
}
