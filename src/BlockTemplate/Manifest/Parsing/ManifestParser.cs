using System.Collections.Generic;

using BlockTemplate.Manifest.Json;

using Newtonsoft.Json.Linq;

namespace BlockTemplate.Manifest.Parsing
{
    /// <summary>
    /// A template's manifest read into a <see cref="TemplateManifest"/> or into every problem it
    /// has, each at its line. Reads the JSON, checks the top for keys nobody knows, and hands each
    /// section to its own reader - in the order one section's answer is the next one's question:
    /// the variables' names before the TIA versions that may not take them, the header before the
    /// types named after it.
    /// </summary>
    internal static class ManifestParser
    {
        private static readonly string[] Keys =
        {
            HeaderSection.Key, TiaVersionsSection.Key, VariablesSection.Key, GeneratedTypesSection.Key
        };

        /// <summary>
        /// The manifest as far as it reads - **null only when there is no object to read at all**.
        /// What did read comes back beside the problems, because the reader holds the sub-templates
        /// against it: a missing one is worth saying on the same read as an unknown key, and Codex's
        /// fourth review of stage 1.1 found it waiting for the key to be fixed. It is never handed to
        /// anybody with problems beside it - the reader returns no template then.
        /// </summary>
        /// <param name="typesWhole">
        /// Every <c>generatedTypes</c> entry read into a type. Only then can a sub-template no entry
        /// describes be called a stray: otherwise it may be the one a broken entry meant.
        /// </param>
        public static TemplateManifest Parse(string json, string file, List<TemplateProblem> problems, out bool typesWhole)
        {
            JsonProblems said = new JsonProblems(file, problems, json);
            typesWhole = false;

            JToken root = ManifestJson.Read(json, said);
            if (root == null) return null;

            JObject top = root as JObject;
            if (top == null)
            {
                said.Add(root, "The manifest must be an object: { \"header\": [...], \"variables\": [...], ... }.");
                return null;
            }

            said.Keys(top, Keys, "the manifest");

            List<string> header = HeaderSection.Read(said, top[HeaderSection.Key]);
            List<TemplateVariable> variables = VariablesSection.Read(said, top[VariablesSection.Key], out HashSet<string> names);
            List<TiaVersion> versions = TiaVersionsSection.Read(said, top[TiaVersionsSection.Key], names);
            List<GeneratedType> types = GeneratedTypesSection.Read(said, top[GeneratedTypesSection.Key], header, out typesWhole);

            return new TemplateManifest(header, versions, variables, types);
        }
    }
}
