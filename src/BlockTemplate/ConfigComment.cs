using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockTemplate
{
    /// <summary>
    /// Where a template's CONFIG-JSON is: the file's first comment, opening with the word
    /// <c>CONFIG-JSON</c> - optionally after <c>//</c> - and holding the JSON after it.
    ///
    /// **Which comment depends on the format**, and only the template's author and this engine ever
    /// read it, since rendering takes it out: <c>&lt;!-- --&gt;</c> in a SimaticML <c>.xml</c>, which
    /// keeps the template a well-formed document in an XML editor, and <c>(* *)</c> in every text
    /// format - <c>.scl</c>, <c>.udt</c>, <c>.db</c>, <c>.awl</c>, <c>.s7dcl</c>.
    ///
    /// **A text template is read the way SCL reads, not searched**: a <c>(*</c> inside a <c>//</c>
    /// line, a <c>/* */</c> comment, a <c>'string'</c> or a <c>"quoted name"</c> is not a comment.
    /// The core's own sources open with <c>//(*--</c>, exactly the shape a plain search gets wrong.
    /// </summary>
    internal sealed class ConfigComment
    {
        public const string Marker = "CONFIG-JSON";

        private static readonly char[] LineEnds = { '\r', '\n' };

        private static readonly char[] QuoteOrLineEnd = { '"', '\r', '\n' };

        private ConfigComment(int start, int length, int jsonStart, string json)
        {
            Start = start;
            Length = length;
            JsonStart = jsonStart;
            Json = json;
        }

        /// <summary>Where the comment starts, its opening delimiter included.</summary>
        public int Start { get; }

        /// <summary>The comment's length, both delimiters included.</summary>
        public int Length { get; }

        /// <summary>Where the JSON starts in the file's text - right after the marker.</summary>
        public int JsonStart { get; }

        public string Json { get; }

        /// <summary>
        /// The CONFIG-JSON in <paramref name="text"/>, or null when there is none to read - and in
        /// <paramref name="problems"/> everything wrong with the file's comments either way.
        /// </summary>
        public static ConfigComment Find(string text, bool xml, List<string> problems)
        {
            string kind = xml ? "<!-- -->" : "(* *)";

            List<Span> comments = xml ? XmlComments(text, out string unclosed) : SourceComments(text, out unclosed);
            List<Span> marked = comments.Where(c => AfterMarker(text, c.BodyStart, c.BodyEnd) >= 0).ToList();

            ConfigComment found = null;

            if (marked.Count > 0 && comments[0].Start == marked[0].Start)
            {
                Span comment = marked[0];
                int jsonStart = AfterMarker(text, comment.BodyStart, comment.BodyEnd);
                found = new ConfigComment(comment.Start, comment.End - comment.Start, jsonStart,
                                          text.Substring(jsonStart, comment.BodyEnd - jsonStart));
            }
            else if (marked.Count > 0)
            {
                problems.Add("Its CONFIG-JSON, at line " + LineAt(text, marked[0].Start) + ", is not its first " + kind +
                             " comment. It has to be: a template says what it is before anything else does.");
            }

            // Rendering takes out the first and only the first, so a second would reach TIA Portal.
            // Codex's ninth review found one passing (2026-10-01).
            foreach (Span extra in marked.Skip(1))
                problems.Add("A second CONFIG-JSON, at line " + LineAt(text, extra.Start) + ": a template has one, " +
                             "and any other would be left in what is sent to TIA Portal.");

            if (unclosed != null) problems.Add(unclosed);

            if (marked.Count == 0 && unclosed == null)
                problems.Add("It carries no CONFIG-JSON: a template says what it is in its first " + kind +
                             " comment, opening with " + Marker + ".");

            return found;
        }

        /// <summary>
        /// What is wrong with a sub-template's comments: a CONFIG-JSON of its own - nothing takes one
        /// out of a sub-template, so it would be sent to TIA Portal; Codex's tenth review found a
        /// copied one passing (2026-10-01) - and a comment that never closes.
        /// </summary>
        public static List<string> InPart(string text, bool xml)
        {
            List<string> problems = new List<string>();
            List<Span> comments = xml ? XmlComments(text, out string unclosed) : SourceComments(text, out unclosed);

            foreach (Span comment in comments.Where(c => AfterMarker(text, c.BodyStart, c.BodyEnd) >= 0))
                problems.Add("It carries a CONFIG-JSON, at line " + LineAt(text, comment.Start) + ": only the template has one, " +
                             "and nothing would take this one out before it is sent to TIA Portal.");

            if (unclosed != null) problems.Add(unclosed);
            return problems;
        }

        /// <summary>
        /// The 1-based line <paramref name="index"/> falls on. A line ends at <c>\r\n</c>, <c>\n</c>
        /// or a <c>\r</c> alone - Json.NET's count, which the problems' lines are added to.
        /// </summary>
        public static int LineAt(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
                if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))) line++;
            return line;
        }

        /// <summary>Where the JSON starts when the body opens with the marker, or -1.</summary>
        private static int AfterMarker(string text, int bodyStart, int bodyEnd)
        {
            int i = bodyStart;
            while (i < bodyEnd && char.IsWhiteSpace(text[i])) i++;

            if (i + 1 < bodyEnd && text[i] == '/' && text[i + 1] == '/')
            {
                i += 2;
                while (i < bodyEnd && (text[i] == ' ' || text[i] == '\t')) i++;
            }

            if (i + Marker.Length > bodyEnd) return -1;
            return string.CompareOrdinal(text, i, Marker, 0, Marker.Length) == 0 ? i + Marker.Length : -1;
        }

        private static List<Span> XmlComments(string text, out string problem)
        {
            problem = null;
            List<Span> comments = new List<Span>();
            int position = 0;

            while (true)
            {
                int open = text.IndexOf("<!--", position, StringComparison.Ordinal);
                if (open < 0) break;

                int close = text.IndexOf("-->", open + 4, StringComparison.Ordinal);
                if (close < 0)
                {
                    problem = "The <!-- comment that opens at line " + LineAt(text, open) + " never closes.";
                    break;
                }

                comments.Add(new Span(open, close + 3, open + 4, close));
                position = close + 3;
            }

            return comments;
        }

        /// <summary>
        /// Every <c>(* *)</c> comment of a source, in order. <c>//</c> lines, <c>/* */</c> comments,
        /// <c>'strings'</c> - where <c>$</c> escapes the next character, SCL's own rule - and
        /// <c>"quoted names"</c> are stepped over - and the first of any of them that never closes is
        /// said, since everything after it would be read wrongly by TIA Portal as much as by this.
        /// </summary>
        private static List<Span> SourceComments(string text, out string problem)
        {
            problem = null;
            List<Span> comments = new List<Span>();
            int i = 0;

            while (i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';

                if (c == '/' && next == '/')
                {
                    // A \r alone ends a line too: Codex's seventh review found a file of them read
                    // as one long comment (2026-10-01).
                    int end = text.IndexOfAny(LineEnds, i);
                    i = end < 0 ? text.Length : end + 1;
                }
                else if (c == '/' && next == '*')
                {
                    // Every opening that never closes is said, not only the (* one: what follows it
                    // reaches TIA Portal as a source it will refuse. Codex's eleventh review found a
                    // /* passing (2026-10-01).
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        problem = "The /* comment that opens at line " + LineAt(text, i) + " never closes.";
                        break;
                    }

                    i = end + 2;
                }
                else if (c == '(' && next == '*')
                {
                    // They nest, and TIA's own exports rely on it: the core's _tcpCommunication holds
                    // (*`state`*) inside a (*-- --*) block. Closed at the first *) instead, the rest
                    // of that block read as source, and its "doesn't" as a string that never closed -
                    // found by running this over every real source there is (2026-10-01).
                    int depth = 1;
                    int j = i + 2;

                    while (j < text.Length && depth > 0)
                    {
                        if (text[j] == '(' && j + 1 < text.Length && text[j + 1] == '*') { depth++; j += 2; }
                        else if (text[j] == '*' && j + 1 < text.Length && text[j + 1] == ')') { depth--; j += 2; }
                        else j++;
                    }

                    if (depth > 0)
                    {
                        problem = "The (* comment that opens at line " + LineAt(text, i) + " never closes.";
                        break;
                    }

                    comments.Add(new Span(i, j, i + 2, j - 2));
                    i = j;
                }
                // A string and a quoted name end on their own line: neither spans one in SCL, so an
                // unclosed one must not borrow the quote of a line further down - Codex's twelfth
                // review found two unclosed strings closing each other (2026-10-01).
                else if (c == '\'')
                {
                    int open = i++;
                    while (i < text.Length && text[i] != '\'' && text[i] != '\r' && text[i] != '\n')
                        i += text[i] == '$' && i + 1 < text.Length && text[i + 1] != '\r' && text[i + 1] != '\n' ? 2 : 1;

                    if (i >= text.Length || text[i] != '\'')
                    {
                        problem = "The ' string that opens at line " + LineAt(text, open) + " never closes.";
                        break;
                    }

                    i++;
                }
                else if (c == '"')
                {
                    int end = text.IndexOfAny(QuoteOrLineEnd, i + 1);
                    if (end < 0 || text[end] != '"')
                    {
                        problem = "The \" name that opens at line " + LineAt(text, i) + " never closes.";
                        break;
                    }

                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }

            return comments;
        }

        /// <summary>A comment: where it starts and ends, delimiters included, and where its body does.</summary>
        private struct Span
        {
            public Span(int start, int end, int bodyStart, int bodyEnd)
            {
                Start = start;
                End = end;
                BodyStart = bodyStart;
                BodyEnd = bodyEnd;
            }

            public int Start { get; }
            public int End { get; }
            public int BodyStart { get; }
            public int BodyEnd { get; }
        }
    }
}
