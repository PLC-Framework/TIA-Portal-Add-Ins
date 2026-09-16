using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Core.Repo
{
    /// <summary>
    /// Reads a block's or a PLC data type's title out of a SimaticML export.
    ///
    /// **This exists because V17-V20 cannot answer the question any other way.** `PlcBlock`
    /// and `PlcType` gained a typed `Title` only in V21, and the untyped escape hatch refuses
    /// it outright - measured on the VM: *"'Title' is not supported by type
    /// 'Siemens.Engineering.SW.Blocks.OB'"*. The export shows it, so an older TIA writes the
    /// object out and this reads it back.
    ///
    /// The title matters because it is where the core library keeps its metadata: a JSON
    /// object naming the version, the family it belongs to, and what it depends on.
    ///
    /// <code>
    /// &lt;MultilingualText ID="5" CompositionName="Title"&gt;
    ///   &lt;ObjectList&gt;
    ///     &lt;MultilingualTextItem ID="6" CompositionName="Items"&gt;
    ///       &lt;AttributeList&gt;
    ///         &lt;Culture&gt;en-US&lt;/Culture&gt;
    ///         &lt;Text&gt;{"version":"v3.0", …}&lt;/Text&gt;
    /// </code>
    ///
    /// **Any language will do.** The metadata is the same JSON in every one of them, and
    /// demanding a particular culture would make the answer depend on which language the
    /// project happens to be edited in. The first non-empty one wins.
    ///
    /// Pure and reading only, like <see cref="Core.Checks.SimaticMlInterface"/> beside it, and
    /// the format it reads is written down in docs/reference/S7-exports.md.
    /// </summary>
    public static class SimaticMlTitle
    {
        private const string MultilingualTextElement = "MultilingualText";
        private const string ItemElement = "MultilingualTextItem";
        private const string TextElement = "Text";
        private const string CompositionAttribute = "CompositionName";
        private const string TitleComposition = "Title";

        /// <summary>
        /// The title in the first <c>MultilingualText</c> the document names <c>Title</c>, or
        /// null. **Never throws**: a block that will not read is one object without metadata,
        /// not the end of a walk over several thousand.
        /// </summary>
        /// <param name="problem">Why it could not be read, or null - including when there simply is no title.</param>
        public static string Read(Stream xml, out string problem)
        {
            problem = null;

            if (xml == null)
            {
                problem = "There is no export to read.";
                return null;
            }

            try
            {
                XElement title = FirstTitle(xml);

                if (title == null) return null;

                foreach (XElement text in title.Descendants())
                {
                    if (text.Name.LocalName != TextElement) continue;

                    string value = text.Value;

                    // Written out per culture, and most of them are empty on a block whose
                    // title was only ever typed in one language.
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }

                return null;
            }
            catch (Exception exception)
            {
                problem = "The export could not be read: " + exception.Message;
                return null;
            }
        }

        /// <summary>The same, given the document as text - which is what a test has.</summary>
        public static string Parse(string xml, out string problem)
        {
            problem = null;

            if (string.IsNullOrWhiteSpace(xml))
            {
                problem = "There is no export to read.";
                return null;
            }

            using (MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)))
                return Read(stream, out problem);
        }

        /// <summary>
        /// The first <c>MultilingualText</c> whose <c>CompositionName</c> is <c>Title</c>,
        /// lifted out on its own.
        ///
        /// **Walked to rather than loaded whole**, for the reason the interface reader gives:
        /// a code block's export is mostly its code - a GRAPH block runs to 3,600 lines - and
        /// this reads one per object across a whole PLC. The title sits in the `ObjectList`
        /// beside the comment, so the walk stops at the first match instead of building a
        /// document out of the rest.
        /// </summary>
        private static XElement FirstTitle(Stream xml)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                // An export never carries a DTD, and refusing one closes the door on a file
                // that points at something else on the machine.
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true
            };

            using (XmlReader reader = XmlReader.Create(xml, settings))
            {
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.LocalName != MultilingualTextElement) continue;
                    if (reader.GetAttribute(CompositionAttribute) != TitleComposition) continue;

                    return XNode.ReadFrom(reader) as XElement;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether an element is one of a title's per-language items. Not used by the reader,
        /// which matches on <c>Text</c> directly, but it names the shape for anyone reading
        /// this beside the format notes.
        /// </summary>
        internal static bool IsItem(XElement element) =>
            element != null && element.Name.LocalName == ItemElement;
    }
}
