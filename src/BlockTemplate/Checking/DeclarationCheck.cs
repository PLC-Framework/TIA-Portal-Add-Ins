using System;
using System.Collections.Generic;

using BlockTemplate.Manifest;

using Core.Config;
using Core.Imports;

namespace BlockTemplate.Checking
{
    /// <summary>
    /// **Every file declares what the template promised**, read with <c>Core.Imports</c>' own
    /// readers - the ones the import will read it with, so the two cannot disagree about a file:
    ///
    /// - **Something to import.** A file declaring nothing would reach TIA with nothing for the
    ///   import's overwrite question to ask about - the one check that stands between a template and
    ///   an object the project already has.
    /// - **A generated type is one data type, under the name the block calls it by**:
    ///   <c>prefix + header.name + suffix</c>, spelled as the manifest spells it. A sub-template
    ///   declaring another name makes a type nothing calls, and the block a call to a type that will
    ///   never exist - which TIA would report against the block, far from the cause. Writing the name
    ///   as <c>{{ types.&lt;id&gt; }}</c> is what keeps the two together.
    /// </summary>
    internal static class DeclarationCheck
    {
        public static IReadOnlyList<DeclaredObject> Check(string text, ImportFormat format, GeneratedType type, string name,
                                                          string file, List<TemplateProblem> problems)
        {
            IReadOnlyList<DeclaredObject> declared = ImportFiles.Declared(text, format, out string problem);

            if (problem != null)
            {
                problems.Add(new TemplateProblem(file, null, problem, rendered: true));
                return declared;
            }

            if (type == null) return declared;

            if (declared.Count != 1)
            {
                problems.Add(new TemplateProblem(file, null, "It declares " + declared.Count + " objects (" + string.Join(", ", declared) +
                                                             "), where a generated type is one data type, '" + name + "'.", rendered: true));
            }
            else if (declared[0].Kind != CodingStyleNames.PlcStruct)
            {
                problems.Add(new TemplateProblem(file, null, "It declares " + declared[0] + ", where a generated type is a data type, '" +
                                                             name + "'.", rendered: true));
            }
            else if (!string.Equals(declared[0].Name, name, StringComparison.Ordinal))
            {
                problems.Add(new TemplateProblem(file, null, "It declares the data type '" + declared[0].Name + "', where the block calls it '" +
                                                             name + "' - write the name as {{ " + TemplateNames.TypeReference(type.Id) + " }}.",
                                                 rendered: true));
            }

            return declared;
        }
    }
}
