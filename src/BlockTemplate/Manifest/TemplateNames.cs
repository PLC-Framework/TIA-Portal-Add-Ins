using System.Collections.Generic;

namespace BlockTemplate.Manifest
{
    /// <summary>
    /// The names a template and its manifest share with the engine - each spelled once, here.
    ///
    /// **They belong to the manifest's side**: a variable may not take <c>header</c> because the
    /// template reaches the header through it, and that is a rule of what a manifest may say. Until
    /// 2026-10-01 the parser read them off the renderer's constants and the renderer read its range
    /// wording off the parser - a cycle between the two halves, removed when the engine was put in
    /// order.
    /// </summary>
    public static class TemplateNames
    {
        /// <summary>Where the template reads the header's fields: <c>header.author</c>.</summary>
        public const string Header = "header";

        /// <summary>Where the template reads each generated type's full name: <c>types.settings</c>.</summary>
        public const string Types = "types";

        /// <summary>The template's numbering function: <c>uid("key")</c> and <c>uid()</c>.</summary>
        public const string Uid = "uid";

        /// <summary>
        /// The one header field the engine needs: a generated type is named
        /// <c>prefix + header.name + suffix</c>, so it is required as soon as there are types.
        /// </summary>
        public const string NameField = "name";

        /// <summary>The one header field the engine starts somewhere: at <see cref="FirstVersion"/>.</summary>
        public const string VersionField = "version";

        public const string FirstVersion = "0.1";

        /// <summary>The names a variable or a TIA version's value may not take, since the template itself uses them.</summary>
        internal static readonly IReadOnlyList<string> Reserved = new[] { Header, Types, Uid };

        /// <summary>
        /// Names Scriban keeps for itself, spelled exactly - <c>If</c> is a name like any other.
        /// **Measured on Scriban 7.5.0**, not recalled: each rendered as <c>{{ name }}</c> with a value
        /// of that name. Its keywords and <c>tablerow</c>, <c>this</c>, <c>true</c>, <c>false</c> and
        /// <c>null</c> did not parse, threw, or rendered something else; its built-in objects did
        /// render, and would hide <c>string.upcase</c> and the like from the template that named a
        /// variable after them. Codex's twelfth review of stage 1.1 found the first kind passing.
        /// </summary>
        internal static readonly IReadOnlyList<string> ScribanWords = new[]
        {
            "if", "else", "end", "for", "case", "when", "while", "break", "continue", "func", "import", "readonly",
            "with", "capture", "ret", "wrap", "do", "tablerow", "this", "true", "false", "null",
            "array", "blank", "date", "empty", "html", "include", "include_join", "math", "object", "regex", "string", "timespan"
        };
    }
}
