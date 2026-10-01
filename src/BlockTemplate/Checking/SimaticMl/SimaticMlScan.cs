using System.Collections.Generic;
using System.IO;
using System.Xml;

using BlockTemplate.Checking.Text;

namespace BlockTemplate.Checking.SimaticMl
{
    /// <summary>
    /// Walks a rendered SimaticML document once and finds everything that is numbered - or says
    /// where it stops being XML, which is the first thing a rendered document can get wrong: a value
    /// or a loop in the wrong place, and the file TIA is handed is not a document at all.
    ///
    /// **Read with <c>XmlReader</c> for the positions, never loaded and saved**: what the check
    /// changes is numbers, written back into the very characters they came from. A document saved
    /// through a DOM comes back with its quotes, its empty elements and its line ends rewritten.
    /// </summary>
    internal static class SimaticMlScan
    {
        /// <summary>Null when the text is not well-formed, said at its line.</summary>
        public static ScannedDocument Scan(string text, string file, List<TemplateProblem> problems)
        {
            ScannedDocument scanned = new ScannedDocument();
            TextLines lines = new TextLines(text);

            XmlReaderSettings settings = new XmlReaderSettings
            {
                // An export never carries a DTD; refusing one keeps a rendered file from pointing
                // the reader at anything else on the machine.
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            try
            {
                using (StringReader source = new StringReader(text))
                using (XmlReader reader = XmlReader.Create(source, settings))
                {
                    IXmlLineInfo position = (IXmlLineInfo)reader;

                    // The local names of the elements open above the reader, one per depth.
                    List<string> path = new List<string>();
                    FlgNetwork network = null;
                    int networkDepth = -1;

                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement)
                        {
                            path.RemoveAt(path.Count - 1);
                            if (reader.Depth == networkDepth) network = null;
                            continue;
                        }

                        if (reader.NodeType != XmlNodeType.Element) continue;

                        string element = reader.LocalName;
                        int depth = reader.Depth;
                        bool empty = reader.IsEmptyElement;

                        if (network == null && element == SimaticMlNames.FlgNet &&
                            depth > 0 && path[depth - 1] == SimaticMlNames.NetworkSource)
                        {
                            network = new FlgNetwork(position.LineNumber);
                            networkDepth = depth;
                            scanned.Networks.Add(network);
                        }

                        if (reader.MoveToFirstAttribute())
                        {
                            do
                            {
                                if (reader.NamespaceURI.Length != 0) continue;

                                bool isId = reader.LocalName == SimaticMlNames.Id;
                                bool isUid = reader.LocalName == SimaticMlNames.UId && network != null;
                                if (!isId && !isUid) continue;

                                NumberedAttribute found = Attribute(text, lines, element, reader, position, file, problems);
                                if (found == null) continue;

                                if (isId) scanned.Ids.Add(found);
                                else Sort(network, networkDepth, path, depth, found);
                            }
                            while (reader.MoveToNextAttribute());

                            reader.MoveToElement();
                        }

                        if (empty)
                        {
                            if (depth == networkDepth) network = null;
                        }
                        else
                        {
                            path.Add(element);
                        }
                    }
                }
            }
            catch (XmlException broken)
            {
                problems.Add(new TemplateProblem(file, broken.LineNumber > 0 ? broken.LineNumber : (int?)null,
                                                 "It is not well-formed XML: " + broken.Message, rendered: true));
                return null;
            }

            return scanned;
        }

        /// <summary>
        /// A UId in a network, put with its kind. **A connection goes by its element**, wherever it
        /// is; anything else by the section of the network it is in - and a UId on something TIA's
        /// networks never number is kept apart, to be said rather than guessed at.
        /// </summary>
        private static void Sort(FlgNetwork network, int networkDepth, List<string> path, int depth, NumberedAttribute found)
        {
            if (found.Element == SimaticMlNames.IdentCon || found.Element == SimaticMlNames.NameCon)
            {
                network.References.Add(found);
                return;
            }

            string section = depth > networkDepth + 1 ? path[networkDepth + 1] : null;

            if (section == SimaticMlNames.Parts)
                network.Parts.Add(found);
            else if (section == SimaticMlNames.Wires && found.Element == SimaticMlNames.Wire && depth == networkDepth + 2)
                network.Wires.Add(found);
            else if (section == SimaticMlNames.Wires && found.Element == SimaticMlNames.OpenCon)
                network.OpenCons.Add(found);
            else
                network.Strays.Add(found);
        }

        /// <summary>
        /// The attribute the reader is on, with the span of its value in the text: the reader gives
        /// the line and column of the name, and the value is found after the <c>=</c> and the quote.
        /// A span that cannot be found is the checker's fault rather than the template's, and is said
        /// as such rather than written somewhere wrong.
        /// </summary>
        private static NumberedAttribute Attribute(string text, TextLines lines, string element, XmlReader reader,
                                                   IXmlLineInfo position, string file, List<TemplateProblem> problems)
        {
            string name = reader.Name;
            int at = lines.Offset(position.LineNumber, position.LinePosition);

            if (at >= 0 && at + name.Length <= text.Length && string.CompareOrdinal(text, at, name, 0, name.Length) == 0)
            {
                int i = at + name.Length;
                while (i < text.Length && IsBlank(text[i])) i++;

                if (i < text.Length && text[i] == '=')
                {
                    i++;
                    while (i < text.Length && IsBlank(text[i])) i++;

                    if (i < text.Length && (text[i] == '"' || text[i] == '\''))
                    {
                        int start = i + 1;
                        int end = text.IndexOf(text[i], start);
                        if (end >= 0)
                            return new NumberedAttribute(element, reader.Value, position.LineNumber, start, end - start);
                    }
                }
            }

            problems.Add(new TemplateProblem(file, position.LineNumber,
                                             "The " + name + " of <" + element + "> could not be found in the text to renumber it, at column " +
                                             position.LinePosition + ". That is the checker's fault, not the template's.", rendered: true));
            return null;
        }

        /// <summary>What XML allows around the <c>=</c> of an attribute.</summary>
        private static bool IsBlank(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';
    }
}
