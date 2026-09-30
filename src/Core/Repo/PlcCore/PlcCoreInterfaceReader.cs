using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;

using Core.Repo.PlcCore.Graph;

namespace Core.Repo.PlcCore
{
    /// <summary>
    /// Reads each node's <c>interface</c> out of <c>core.json</c>, **in both shapes the file has
    /// carried**: a parameter is <c>{"name", "type"}</c> since 2026-09-30 and was a bare name
    /// before. <c>DataContractJsonSerializer</c> cannot read a list that could hold either, and a
    /// <c>core.json</c> that is still the older shape - a repository not yet regenerated, a
    /// branch nobody has touched - must not stop a Load; so this reads that one key by hand, from
    /// the same bytes, after the serializer has read everything else.
    ///
    /// **Through the JSON reader the serializer itself uses**, which hands the document over as
    /// XML - an object an element, a value typed in its <c>type</c> attribute - so no second
    /// parser of JSON is written here, and nothing it needs is denied under partial trust.
    ///
    /// **An interface that does not read cleanly is null, never partial.** A parameter silently
    /// missing from a list would produce a call that looks right and is not - the generator's
    /// own rule, kept on this side.
    /// </summary>
    internal static class PlcCoreInterfaceReader
    {
        public static void Fill(byte[] json, int offset, DependencyGraph graph)
        {
            if (graph?.Nodes == null) return;

            XElement root;

            using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(
                       json, offset, json.Length - offset, XmlDictionaryReaderQuotas.Max))
                root = XElement.Load(reader);

            List<XElement> nodes = root.Element("nodes")?.Elements("item").ToList() ?? new List<XElement>();

            // The serializer read the same array in the same order, so position is identity.
            for (int i = 0; i < graph.Nodes.Count && i < nodes.Count; i++)
            {
                Node node = graph.Nodes[i];
                if (node != null) node.Interface = Interface(nodes[i].Element("interface"));
            }
        }

        private static BlockInterface Interface(XElement element)
        {
            if (element == null || TypeOf(element) != "object") return null;

            List<Parameter> input = Parameters(element.Element("input"));
            List<Parameter> output = Parameters(element.Element("output"));
            List<Parameter> inout = Parameters(element.Element("inout"));

            if (input == null || output == null || inout == null) return null;

            XElement returns = element.Element("return");

            return new BlockInterface
            {
                Input = input,
                Output = output,
                InOut = inout,
                Return = returns != null && TypeOf(returns) == "string" ? returns.Value : null
            };
        }

        /// <summary>A section's parameters, or null when the list is not one this can read whole.</summary>
        private static List<Parameter> Parameters(XElement list)
        {
            if (list == null || TypeOf(list) != "array") return null;

            List<Parameter> parameters = new List<Parameter>();

            foreach (XElement item in list.Elements("item"))
            {
                Parameter parameter = ParameterOf(item);
                if (parameter == null) return null;

                parameters.Add(parameter);
            }

            return parameters;
        }

        private static Parameter ParameterOf(XElement item)
        {
            switch (TypeOf(item))
            {
                case "string":
                    // The older shape: a name, and nothing known about its type.
                    return string.IsNullOrEmpty(item.Value) ? null : new Parameter { Name = item.Value };

                case "object":
                    XElement name = item.Element("name");
                    XElement type = item.Element("type");

                    if (name == null || TypeOf(name) != "string" || name.Value.Length == 0) return null;

                    return new Parameter
                    {
                        Name = name.Value,
                        Type = type != null && TypeOf(type) == "string" && type.Value.Length > 0 ? type.Value : null
                    };

                default:
                    return null;
            }
        }

        /// <summary>
        /// What JSON value an element holds. **A missing attribute is a string**: that is the
        /// mapping's documented default, so it is read as one rather than trusted to be written.
        /// </summary>
        private static string TypeOf(XElement element) => (string)element.Attribute("type") ?? "string";
    }
}
