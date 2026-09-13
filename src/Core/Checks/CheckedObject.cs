using System.Collections.Generic;

namespace Core.Checks
{
    /// <summary>
    /// One TIA object as the Add-In found it, in terms this layer understands.
    ///
    /// Nothing Siemens crosses into <c>Core</c>: the version project walks the project and
    /// hands over names and strings, which is what lets the whole check be exercised here
    /// with no TIA installed - the same arrangement the config validators and the web
    /// server client already use.
    /// </summary>
    public sealed class CheckedObject
    {
        public CheckedObject(
            ObjectFamily family,
            string type,
            string name,
            string path = null,
            IReadOnlyList<CheckedMember> members = null,
            string membersUnreadable = null)
        {
            Family = family;
            Type = type;
            Name = name;
            Path = path ?? string.Empty;
            Members = members ?? new List<CheckedMember>();
            MembersUnreadable = membersUnreadable;
        }

        public ObjectFamily Family { get; }

        /// <summary>
        /// TIA's own type name, which is what the configuration's <c>type</c> is matched
        /// against: <c>FB</c>, <c>GlobalDB</c>, <c>PlcTagTable</c>. It comes from the
        /// runtime type of the object the adapter was holding.
        /// </summary>
        public string Type { get; }

        public string Name { get; }

        /// <summary>Where it sits in the project tree, for a report a person has to act on.</summary>
        public string Path { get; }

        /// <summary>
        /// What lives inside it, as far as the object model shows. In phase one that is a
        /// tag table's tags and constants and a global DB's top-level members; the members
        /// of a code block and of a UDT need the export that phase two brings.
        /// </summary>
        public IReadOnlyList<CheckedMember> Members { get; }

        /// <summary>
        /// Why the members could not be read - a know-how protected block, or TIA refusing
        /// the call - and null when they were, or when nothing was asked of them.
        ///
        /// Carried rather than swallowed: an object whose members could not be read and an
        /// object with no members to report look identical in a table, and only one of them
        /// is a clean result.
        /// </summary>
        public string MembersUnreadable { get; }
    }

    /// <summary>One member of an object's interface, or one tag or constant of a table.</summary>
    public sealed class CheckedMember
    {
        public CheckedMember(string section, string name)
        {
            Section = section;
            Name = name;
        }

        /// <summary>
        /// The section it belongs to, spelled as TIA spells it: <c>Input</c>, <c>Static</c>,
        /// <c>Tag</c>, <c>UserConstant</c>. Matched against an interface section's
        /// <c>type</c> without folding case, which is why both sides use TIA's spelling.
        /// </summary>
        public string Section { get; }

        public string Name { get; }
    }
}
