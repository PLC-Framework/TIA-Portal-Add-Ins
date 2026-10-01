using System;
using System.Collections.Generic;

using BlockTemplate.Checking.Text;

namespace BlockTemplate.Checking
{
    /// <summary>
    /// **Nothing a template marks is left in what it rendered**, in any format. Scriban reads every
    /// <c>{{</c> it meets, so one that survives was put there on purpose - inside <c>{%{ }%}</c> -
    /// or arrived in a value; either way TIA would take it as text and keep it, in a comment or a
    /// name, with nothing saying so.
    ///
    /// **Only the opening <c>{{</c>**: a closing <c>}}</c> is what JSON writes when an object ends
    /// inside another, and a core block's <c>TITLE</c> is JSON.
    /// </summary>
    internal static class MarkerCheck
    {
        public const string Marker = "{{";

        public static void Check(string text, string file, List<TemplateProblem> problems)
        {
            if (text == null) return;

            TextLines lines = null;
            int lastLine = 0;

            for (int at = text.IndexOf(Marker, StringComparison.Ordinal); at >= 0; at = text.IndexOf(Marker, at + Marker.Length, StringComparison.Ordinal))
            {
                if (lines == null) lines = new TextLines(text);

                // One problem a line: a line holding three is one place to look.
                int line = lines.LineOf(at);
                if (line == lastLine) continue;
                lastLine = line;

                problems.Add(new TemplateProblem(file, line, "'" + Marker + "' is in what it rendered - a marker Scriban was told not to read, " +
                                                             "or a value holding one - and TIA would keep it as text.", rendered: true));
            }
        }
    }
}
