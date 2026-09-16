using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace Core.Repo
{
    /// <summary>
    /// Reads and writes <c>repo\project.json</c>.
    ///
    /// **Written indented, which `DataContractJsonSerializer` will not do on its own.**
    /// `JsonReaderWriterFactory` makes the writer it uses underneath, and that one takes an
    /// indent flag - so the file stays readable without `Newtonsoft.Json`, which `Core` is
    /// not allowed to take. This file is generated, but it is generated to be *looked at*
    /// when a comparison says something surprising, and one long line is not.
    /// </summary>
    public static class ProjectMapFile
    {
        /// <summary>
        /// Writes the map into the project's own workspace, and answers why it could not be.
        ///
        /// **It writes to a temporary file and moves it into place**, so a run that fails
        /// half way leaves the previous map rather than a truncated one - the same rule the
        /// report export follows, and for the same reason: what is left behind gets read as
        /// though it were whole.
        /// </summary>
        /// <returns>Null when written, or a sentence.</returns>
        public static string Write(ProjectMap map, string projectDirectory)
        {
            if (map == null) return "There is no map to write.";

            string path = RepoPaths.ProjectFor(projectDirectory);

            if (path == null)
                return "This project has no folder yet, so there is nowhere to write the map.";

            string scratch = path + ".writing";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                using (FileStream stream = File.Create(scratch))
                using (XmlDictionaryWriter writer =
                       JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(ProjectMap)).WriteObject(writer, map);
                    writer.Flush();
                }

                if (File.Exists(path)) File.Delete(path);

                File.Move(scratch, path);

                return null;
            }
            catch (Exception exception)
            {
                Discard(scratch);

                return "The map could not be written to '" + path + "': " + exception.Message;
            }
        }

        /// <summary>The map a project already holds, or null when there is none to read.</summary>
        public static ProjectMap Read(string projectDirectory, out string problem)
        {
            problem = null;

            string path = RepoPaths.ProjectFor(projectDirectory);

            if (path == null || !File.Exists(path)) return null;

            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    ProjectMap map =
                        new DataContractJsonSerializer(typeof(ProjectMap)).ReadObject(stream) as ProjectMap;

                    if (map == null)
                    {
                        problem = "'" + path + "' is not a project map.";
                        return null;
                    }

                    if (map.Format > ProjectMap.CurrentFormat)
                    {
                        problem = "'" + path + "' was written by a newer version of the framework.";
                        return null;
                    }

                    return map;
                }
            }
            catch (Exception exception)
            {
                problem = "'" + path + "' could not be read: " + exception.Message;
                return null;
            }
        }

        private static void Discard(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // A half-written file that will not go is not worth failing over: it carries
                // the .writing suffix, so nothing reads it as a map.
            }
        }
    }
}
