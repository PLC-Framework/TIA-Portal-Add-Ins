using System;
using System.Collections.Generic;

using BlockTemplate.Manifest.Json;

using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Parsing
{
    /// <summary>
    /// The manifest's <c>header</c>: an open list of field names, each a text field of the form,
    /// non-empty and unique whatever the case.
    /// </summary>
    internal static class HeaderSection
    {
        public const string Key = "header";

        public static List<string> Read(JsonProblems problems, JToken token)
        {
            List<string> names = new List<string>();
            JArray array = problems.List(token, Key, "a list of field names: [\"author\", \"name\", ...]");
            if (array == null) return names;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < array.Count; i++)
            {
                JToken item = array[i];
                string at = Key + "[" + i + "]";

                if (item.Type != JTokenType.String)
                {
                    problems.Add(item, at + " must be a field name in quotes.");
                    continue;
                }

                string name = (string)item;

                if (string.IsNullOrWhiteSpace(name))
                    problems.Add(item, at + " is empty.");
                else if (!seen.Add(name))
                    problems.Add(item, at + " '" + name + "' is in the list already.");
                else
                    names.Add(name);
            }

            return names;
        }
    }
}
