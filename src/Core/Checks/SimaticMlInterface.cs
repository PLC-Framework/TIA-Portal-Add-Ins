using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

using Core.Config;

namespace Core.Checks
{
    /// <summary>
    /// Reads a block's or a PLC data type's interface out of the SimaticML TIA exports, and
    /// hands back the members the coding-style check holds against the rules.
    ///
    /// **This exists because the object model cannot answer the question.** `Member` there
    /// carries a `Name` and nothing else, so a member inside a `Struct` arrives as
    /// `variableA.variableB` - its parent's name and its own, joined by a dot - and every one
    /// of them fails a rule that reads a single name. The export shows the tree, so a nested
    /// member is checked by its own name with its parents recorded beside it.
    ///
    /// Pure, and reading only: it is handed a stream and returns rows. The format it reads is
    /// written down in docs/reference/S7-exports.md, measured off real exports of both TIA
    /// versions rather than taken from documentation.
    ///
    /// Two rules decide what comes back, and both are read off the file's shape rather than
    /// off any list of Siemens names:
    ///
    /// **Only a member's child `Member` elements are its members.** A member also carries
    /// `AttributeList`, `Comment`, `StartValue` and `Subelement`, which are about the member
    /// rather than inside it.
    ///
    /// **A nested `Sections` is another type's interface, and is not entered.** A `Struct`
    /// carries its members as children and those names were typed in this block; a member of a
    /// UDT, of a library FB or of a system type carries a `Sections` holding the interface of
    /// that type, whose names belong to it and are checked wherever it is defined. That is why
    /// a GRAPH block's MOP, SQ_FLAGS and OFFSETS never appear here: they live inside RT_DATA.
    /// </summary>
    public static class SimaticMlInterface
    {
        /// <summary>
        /// What TIA calls the one section of a PLC data type, which has no sections in the
        /// editor. It is read as <see cref="CodingStyleNames.Static"/>, the section the
        /// contract uses for a UDT's elements.
        /// </summary>
        public const string UnnamedSection = "None";

        /// <summary>
        /// How deep a member may nest. Not a style rule: this runs inside TIA Portal's process
        /// on its own thread, and a file that nests without end would otherwise recurse until
        /// the host dies. A hand-built interface is two or three deep.
        /// </summary>
        private const int MaxDepth = 32;

        private const string InterfaceElement = "Interface";
        private const string SectionsElement = "Sections";
        private const string SectionElement = "Section";
        private const string MemberElement = "Member";
        private const string NameAttribute = "Name";

        /// <summary>Separates a member's parents from each other, as TIA writes a path in its own editors.</summary>
        public const string ParentSeparator = ".";

        /// <summary>
        /// The members of the first interface in the document, in the order the file lists
        /// them. Null when the document could not be read, with the reason in
        /// <paramref name="problem"/> - never an exception, because the caller turns that
        /// reason into a skipped row rather than losing the whole check over one block.
        /// </summary>
        public static IReadOnlyList<CheckedMember> Read(Stream xml, out string problem)
        {
            problem = null;

            if (xml == null)
            {
                problem = "There is no export to read.";
                return null;
            }

            try
            {
                XElement interfaces = FirstInterface(xml);

                if (interfaces == null)
                {
                    problem = "The export holds no interface.";
                    return null;
                }

                List<CheckedMember> members = new List<CheckedMember>();

                // The interface holds one Sections, and that holds a Section per part of the
                // interface. A section with nothing in it is written as a closed element, so an
                // empty Input simply contributes nothing.
                foreach (XElement section in Children(Child(interfaces, SectionsElement), SectionElement))
                {
                    string name = SectionOf(section);

                    foreach (XElement member in Children(section, MemberElement))
                        Walk(member, name, null, 1, members);
                }

                return members;
            }
            catch (InterfaceTooDeepException)
            {
                problem = "The export nests members more than " + MaxDepth + " levels deep, so it was not read.";
                return null;
            }
            catch (Exception exception)
            {
                problem = "The export could not be read: " + exception.Message;
                return null;
            }
        }

        /// <summary>The same, given the document as text - which is what a test has.</summary>
        public static IReadOnlyList<CheckedMember> Parse(string xml, out string problem)
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
        /// The first <c>Interface</c> element, materialised on its own.
        ///
        /// A code block's export is mostly its code - a GRAPH block runs to 3,600 lines of
        /// which the interface is the first 400 - and a project-wide check reads thousands of
        /// them, so the reader walks to the interface and lifts out that subtree instead of
        /// building a document out of the whole file.
        /// </summary>
        private static XElement FirstInterface(Stream xml)
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
                    // By local name: the interface itself is in no namespace while everything
                    // inside it is in the Interface/v5 one, and matching the namespace would
                    // make the reader a hostage to a version TIA has not shipped yet.
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != InterfaceElement) continue;

                    return XNode.ReadFrom(reader) as XElement;
                }
            }

            return null;
        }

        private static void Walk(XElement member, string section, string parent, int depth, List<CheckedMember> into)
        {
            if (depth > MaxDepth) throw new InterfaceTooDeepException();

            string name = Attribute(member, NameAttribute);
            if (string.IsNullOrEmpty(name)) return;

            into.Add(new CheckedMember(section, name, parent));

            string chain = string.IsNullOrEmpty(parent) ? name : parent + ParentSeparator + name;

            foreach (XElement child in Children(member, MemberElement))
                Walk(child, section, chain, depth + 1, into);
        }

        /// <summary>
        /// The section a member belongs to, spelled as TIA spells it - except a PLC data
        /// type's, which TIA leaves unnamed and the contract calls <c>Static</c>.
        ///
        /// Anything else travels as written, so a section the configuration does not name -
        /// an FC's <c>Return</c>, a GRAPH block's <c>Base</c> - simply has no rules and is not
        /// reported, without this reader deciding that on its own.
        /// </summary>
        private static string SectionOf(XElement section)
        {
            string name = Attribute(section, NameAttribute);

            return string.Equals(name, UnnamedSection, StringComparison.Ordinal)
                ? CodingStyleNames.Static
                : name;
        }

        private static XElement Child(XElement parent, string name) =>
            parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name);

        private static IEnumerable<XElement> Children(XElement parent, string name) =>
            parent == null
                ? Enumerable.Empty<XElement>()
                : parent.Elements().Where(element => element.Name.LocalName == name);

        private static string Attribute(XElement element, string name) =>
            element?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;

        private sealed class InterfaceTooDeepException : Exception
        {
        }
    }
}
