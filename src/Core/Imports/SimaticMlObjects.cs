using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Core.Imports
{
    /// <summary>
    /// The objects a SimaticML document holds, by name and kind, read off the document itself:
    ///
    /// <code>
    /// &lt;Document&gt;
    ///   &lt;Engineering version="V21" /&gt;
    ///   &lt;SW.Blocks.FB ID="0"&gt;
    ///     &lt;AttributeList&gt;
    ///       &lt;Name&gt;_oc_template&lt;/Name&gt;
    /// </code>
    ///
    /// **The kind is the element's last segment**, which is already the framework's own word for
    /// it: <c>SW.Blocks.GlobalDB</c> is <c>GlobalDB</c>, <c>SW.Types.PlcStruct</c> is
    /// <c>PlcStruct</c>, <c>SW.TechnologicalObjects.TechnologicalInstanceDB</c> is
    /// <c>TechnologicalInstanceDB</c> - the same strings <see cref="Config.CodingStyleNames"/>
    /// spells, so no table maps one onto the other.
    ///
    /// **The <c>Engineering</c> line is not read.** Whether a file from another TIA version goes
    /// in is TIA's to decide (the maintainer's choice, 2026-09-28), and a check here would be a
    /// second answer to a question TIA already answers.
    /// </summary>
    internal static class SimaticMlObjects
    {
        private const string DocumentElement = "Document";
        private const string ObjectPrefix = "SW.";
        private const string AttributeListElement = "AttributeList";
        private const string NameElement = "Name";

        /// <summary>
        /// Every object at the top of the document, in its order. **Never throws**: a document
        /// that will not read comes back empty with the reason.
        /// </summary>
        internal static IReadOnlyList<DeclaredObject> Read(Stream xml, out string problem)
        {
            problem = null;
            List<DeclaredObject> found = new List<DeclaredObject>();

            try
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
                    reader.MoveToContent();

                    if (reader.LocalName != DocumentElement)
                    {
                        problem = "It is not a SimaticML document: its root is <" + reader.LocalName + ">.";
                        return found;
                    }

                    // Walked rather than loaded, and skipped past wherever a name cannot be: a
                    // code block's export is mostly its code, and the object's own name sits at
                    // Document / SW.* / AttributeList / Name. Every Name deeper down belongs to
                    // something inside the object - a member, a network, a comment.
                    string kind = null;
                    bool named = false;

                    reader.Read();

                    while (!reader.EOF)
                    {
                        if (reader.NodeType != XmlNodeType.Element)
                        {
                            reader.Read();
                            continue;
                        }

                        switch (reader.Depth)
                        {
                            case 1:
                                // <Engineering>, and anything else that is not an object.
                                kind = reader.LocalName.StartsWith(ObjectPrefix, StringComparison.Ordinal)
                                    ? reader.LocalName.Substring(reader.LocalName.LastIndexOf('.') + 1)
                                    : null;
                                named = false;

                                if (kind == null) reader.Skip();
                                else reader.Read();
                                break;

                            case 2:
                                // The object's ObjectList - its code, title and comment - and any
                                // AttributeList after the name has been found.
                                if (named || reader.LocalName != AttributeListElement) reader.Skip();
                                else reader.Read();
                                break;

                            case 3:
                                if (!named && reader.LocalName == NameElement)
                                {
                                    string name = reader.ReadElementContentAsString().Trim();
                                    if (name.Length > 0) found.Add(new DeclaredObject(name, kind));
                                    named = true;
                                }
                                else
                                {
                                    // The Interface above all, which is most of a data block.
                                    reader.Skip();
                                }
                                break;

                            default:
                                reader.Skip();
                                break;
                        }
                    }
                }

                if (found.Count == 0) problem = "It names no block, data type, tag table or technology object.";
                return found;
            }
            catch (Exception exception)
            {
                problem = "It could not be read as SimaticML: " + exception.Message;
                return new DeclaredObject[0];
            }
        }
    }
}
