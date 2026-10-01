using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// One text rendered by Scriban - **the only place it runs**, so how it is set up is decided once:
    ///
    /// - **Strict**: a name the template uses and nothing defines is an error at its line, where
    ///   Scriban's default renders it as nothing - a typo would otherwise be a silent hole in a block.
    ///   A member that is not there is one too, <c>header.nmae</c> included.
    /// - **Numbers in the invariant culture**: 0.5 is <c>0.5</c> on a Spanish Windows too.
    /// - **A fresh context per text, and no template loader**, so <c>include</c> reaches nothing.
    ///   Reusing a context is what Scriban's one critical advisory needed, fixed in 7.0.0.
    /// - **The model is read-only**, with a writable scope on top where the template's own
    ///   assignments land: it may make <c>{{ x = 5 }}</c>, and may not change what the form said.
    /// </summary>
    internal static class ScribanText
    {
        /// <summary>The text rendered, or null with every problem said against <paramref name="file"/>.</summary>
        public static string Render(string text, string file, ScriptObject model, List<TemplateProblem> problems)
        {
            Template parsed = Template.Parse(text, file);

            if (parsed.HasErrors)
            {
                foreach (LogMessage message in parsed.Messages.Where(m => m.Type == ParserMessageType.Error))
                    problems.Add(new TemplateProblem(file, message.Span.Start.Line + 1, message.Message));
                return null;
            }

            TemplateContext context = new TemplateContext
            {
                StrictVariables = true,
                EnableRelaxedMemberAccess = false,
                TemplateLoader = null
            };

            context.PushCulture(CultureInfo.InvariantCulture);
            context.PushGlobal(model);
            context.PushGlobal(new ScriptObject());   // where the template's own assignments land

            try
            {
                return parsed.Render(context);
            }
            catch (ScriptRuntimeException failed)
            {
                problems.Add(new TemplateProblem(file, failed.Span.Start.Line + 1, failed.OriginalMessage));
            }
            catch (Exception failed)
            {
                problems.Add(new TemplateProblem(file, null, "It could not be rendered: " + failed.Message));
            }

            return null;
        }
    }
}
