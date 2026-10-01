using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;

using BlockTemplate.Manifest;

using Scriban.Runtime;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// The values a template is rendered with, once <see cref="ValuesCheck"/> has held them against
    /// its manifest - and what one file sees of them:
    ///
    /// - **An empty value is null.** Scriban holds an empty string true, so <c>{{ if header.namespace }}</c>
    ///   took the first branch with the field left empty - the opposite of what the maintainer's own
    ///   template means. Null is false, and renders as nothing. Measured on Scriban 7.5.0.
    /// - **In SimaticML, every value is escaped for XML** - a description holding <c>&lt;</c> or
    ///   <c>&amp;</c> must not break the document. The values are escaped, not the output, so the
    ///   template's own markup is untouched and a value joined to another is escaped once. Other
    ///   formats take values as they are.
    /// - **Read-only**, a list's entries included - see <see cref="ReadOnlyList"/>.
    /// </summary>
    internal sealed class RenderModel
    {
        /// <summary>The header's fields, in the manifest's order and spelling.</summary>
        public List<KeyValuePair<string, string>> Header { get; } = new List<KeyValuePair<string, string>>();

        /// <summary>The variables' and the chosen TIA version's values, by name.</summary>
        public Dictionary<string, object> Values { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Each generated type's full name, by id.</summary>
        public Dictionary<string, string> Types { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary><c>header.name</c>, or null when the header has no such field.</summary>
        public string Name { get; set; }

        /// <summary>What one file sees, its values escaped for XML when it is SimaticML.</summary>
        public ScriptObject Script(bool xml, UidCounter uids)
        {
            ScriptObject header = new ScriptObject();
            foreach (KeyValuePair<string, string> field in Header) header.Add(field.Key, Text(field.Value, xml));
            header.IsReadOnly = true;

            ScriptObject types = new ScriptObject();
            foreach (KeyValuePair<string, string> type in Types) types.Add(type.Key, Text(type.Value, xml));
            types.IsReadOnly = true;

            ScriptObject global = new ScriptObject
            {
                { TemplateNames.Header, header },
                { TemplateNames.Types, types }
            };

            foreach (KeyValuePair<string, object> value in Values) global.Add(value.Key, Value(value.Value, xml));

            global.Import(TemplateNames.Uid, new Func<string, int>(uids.Next));
            global.IsReadOnly = true;
            return global;
        }

        private static object Value(object value, bool xml)
        {
            switch (value)
            {
                case string text: return Text(text, xml);
                case List<string> list: return new ReadOnlyList(list.Select(entry => xml ? SecurityElement.Escape(entry) : entry));
                default: return value;
            }
        }

        /// <summary>Empty is null, so a template's <c>if</c> reads it as false.</summary>
        private static string Text(string value, bool xml) =>
            string.IsNullOrEmpty(value) ? null : xml ? SecurityElement.Escape(value) : value;
    }
}
