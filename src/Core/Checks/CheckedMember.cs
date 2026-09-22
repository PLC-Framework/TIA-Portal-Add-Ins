namespace Core.Checks
{
    /// <summary>One member of an object's interface, or one tag or constant of a table.</summary>
    public sealed class CheckedMember
    {
        public CheckedMember(string section, string name, string parent = null)
        {
            Section = section;
            Name = name;
            Parent = parent ?? string.Empty;
        }

        /// <summary>
        /// The section it belongs to, spelled as TIA spells it: <c>Input</c>, <c>Static</c>,
        /// <c>Tag</c>, <c>UserConstant</c>. Matched against an interface section's
        /// <c>type</c> without folding case, which is why both sides use TIA's spelling.
        /// </summary>
        public string Section { get; }

        /// <summary>
        /// Its own name, and only that: <c>maxSpeed</c>, not <c>motor.maxSpeed</c>. A naming
        /// rule reads one name, so a member declared inside a <c>Struct</c> is held against
        /// the rule the same way a top-level one is.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The members it is declared inside, joined with dots - <c>motor</c>, or
        /// <c>motor.drive</c> - and empty at the top of a section. It is what tells a reader
        /// where a name lives without becoming part of the name being checked.
        /// </summary>
        public string Parent { get; }
    }
}
