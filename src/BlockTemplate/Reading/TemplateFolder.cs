using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlockTemplate.Reading
{
    /// <summary>
    /// The folder a template sits in - **the only place the engine touches the disk**. Neither call
    /// throws: a folder that will not list and a file that will not read are problems said, and the
    /// answer is null.
    /// </summary>
    internal static class TemplateFolder
    {
        /// <summary>
        /// Every template file beside <paramref name="path"/> of the same base and major, the
        /// manifest itself included. Null when the folder will not list, said.
        /// </summary>
        public static List<SiblingFile> Siblings(string path, TemplateFileName name, List<TemplateProblem> problems, string file)
        {
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(path));

                return Directory.GetFiles(folder)
                    .Select(p => new SiblingFile(p, TemplateFileName.Parse(Path.GetFileName(p), out string _)))
                    .Where(s => s.Name != null &&
                                s.Name.Major == name.Major &&
                                string.Equals(s.Name.Base, name.Base, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception failed)
            {
                problems.Add(new TemplateProblem(file, null, "Its folder could not be listed: " + failed.Message));
                return null;
            }
        }

        public static string ReadText(string path, List<TemplateProblem> problems, string file)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception failed)
            {
                problems.Add(new TemplateProblem(file, null, "It could not be read: " + failed.Message));
                return null;
            }
        }
    }
}
