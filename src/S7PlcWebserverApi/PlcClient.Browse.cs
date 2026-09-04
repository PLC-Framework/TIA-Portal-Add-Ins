using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace S7PlcWebserverApi
{
    public sealed partial class PlcClient
    {
        private struct ArrayDimension
        {
            public int Start { get; }
            public int Count { get; }

            public ArrayDimension(int start, int count)
            {
                Start = start;
                Count = count;
            }
        }

        /// <summary>
        /// Walk a data block recursively and return every readable leaf inside it.
        ///
        /// A block that does not exist throws: the caller is expected to report which
        /// one failed and carry on with the rest, rather than lose the whole run.
        /// </summary>
        public BrowseResult BrowseDb(string db)
        {
            string display = Unquote(db);
            List<PlcVariable> leaves = new List<PlcVariable>();
            bool truncated = Walk(Quote(display), display, leaves, depth: 1);
            return new BrowseResult(leaves, truncated);
        }

        /// <summary>Wrap a symbol in the double quotes the web API expects.</summary>
        internal static string Quote(string name) => "\"" + Unquote(name) + "\"";
                
        private bool Walk(string path, string display, List<PlcVariable> leaves, int depth)
        {
            if (depth > PlcLimits.MaxBrowseDepth) return true;

            JToken result = Rpc("PlcProgram.Browse", new JObject
            {
                ["var"] = path,
                ["mode"] = "children"
            });

            if (!(result is JArray children)) return false;

            bool truncated = false;

            foreach (JToken child in children)
            {
                if (!(child is JObject node)) continue;
                if (leaves.Count >= PlcLimits.MaxBrowseNodes) return true;

                string name = node["name"]?.Value<string>() ?? string.Empty;
                string childPath = path + "." + Quote(name);
                string childDisplay = display + "." + name;

                // Arrays first, before has_children. An array of structs reports
                // has_children too, but browsing it without an index answers error 203
                // instead of descending.
                ArrayDimension[] dimensions = ArrayDimensions(node);
                if (dimensions != null)
                {
                    truncated |= ExpandArray(node, childPath, childDisplay, dimensions, leaves, depth);
                    continue;
                }

                if (node["has_children"]?.Value<bool>() == true)
                {
                    truncated |= Walk(childPath, childDisplay, leaves, depth + 1);
                    continue;
                }

                leaves.Add(Leaf(node, childPath, childDisplay));
            }

            return truncated;
        }

        /// <summary>Turn an array node into one entry per element.</summary>
        private bool ExpandArray(
            JObject node, string path, string display,
            ArrayDimension[] dimensions, List<PlcVariable> leaves, int depth)
        {
            if (dimensions.Length == 0) return true;

            bool hasChildren = node["has_children"]?.Value<bool>() == true;
            bool truncated = false;

            int[] index = new int[dimensions.Length];
            for (int i = 0; i < index.Length; i++) index[i] = dimensions[i].Start;

            for (int produced = 0; ; produced++)
            {
                if (produced >= PlcLimits.MaxArrayElements ||
                    leaves.Count >= PlcLimits.MaxBrowseNodes)
                    return true;

                // One bracket, comma separated: "DB"."Arr"[1,2] -- never [1][2].
                string suffix = "[" + string.Join(",", index) + "]";

                if (hasChildren)
                    truncated |= Walk(path + suffix, display + suffix, leaves, depth + 1);
                else
                    leaves.Add(Leaf(node, path + suffix, display + suffix));

                if (!Advance(index, dimensions)) break;
            }

            return truncated;
        }

        /// <summary>
        /// The index ranges of every dimension, or null when the node is not an array.
        ///
        /// The CPU reports an array as an ordinary node carrying "array_dimensions", one
        /// {"start_index": 0, "count": 4} per dimension. Its "datatype" holds the
        /// *element* type, so it says nothing at all about the bounds.
        ///
        /// An empty array means the dimensions could not be read; the caller then skips
        /// the node rather than guessing an index.
        /// </summary>
        private static ArrayDimension[] ArrayDimensions(JObject node)
        {
            if (!(node["array_dimensions"] is JArray dims) || dims.Count == 0) return null;

            ArrayDimension[] ranges = new ArrayDimension[dims.Count];

            for (int i = 0; i < dims.Count; i++)
            {
                if (!(dims[i] is JObject dim)) return new ArrayDimension[0];

                int start = dim["start_index"]?.Value<int?>() ?? 0;
                int count = dim["count"]?.Value<int?>() ?? 0;
                if (count <= 0) return new ArrayDimension[0];

                ranges[i] = new ArrayDimension(start, count);
            }

            return ranges;
        }

        /// <summary>Odometer over the dimensions. False once every index has been produced.</summary>
        private static bool Advance(int[] index, ArrayDimension[] dimensions)
        {
            for (int i = index.Length - 1; i >= 0; i--)
            {
                index[i]++;
                if (index[i] < dimensions[i].Start + dimensions[i].Count) return true;
                index[i] = dimensions[i].Start;
            }

            return false;
        }

        private static PlcVariable Leaf(JObject node, string path, string display) =>
            new PlcVariable(
                path,
                display,
                node["datatype"]?.Value<string>() ?? string.Empty,
                node["read_only"]?.Value<bool>() == true);


        private static string Unquote(string name) => (name ?? string.Empty).Trim().Trim('"');
        
    }
}
