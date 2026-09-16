using System;
using System.IO;
using System.Runtime.Serialization.Json;

using CoreGraph = Core.DependencyGraph.DependencyGraph;

namespace Core.Repo
{
    /// <summary>
    /// Reads <c>core.json</c> into a <see cref="CoreCatalog"/>.
    ///
    /// Loading and validating are separate here for the same reason they are for
    /// <c>config.json</c>: this says only why the file could not be turned into a catalogue
    /// at all, and <see cref="CoreValidator"/> says what is wrong with one that loaded.
    ///
    /// **The parsing is `ConfigLoader`'s, byte for byte**, including the byte order mark it
    /// has to skip and the empty file it has to name. `DataContractJsonSerializer` reports a
    /// BOM as *"Encountered unexpected character 'i'"*, which nobody can act on, and both
    /// files are written by tools that add one.
    /// </summary>
    public static class CoreCatalogLoader
    {
        /// <summary>Reads the core a <see cref="RepoSource"/> points at.</summary>
        public static CoreLoadResult Load(RepoSource source)
        {
            if (source == null) return CoreLoadResult.Failed("No repository was given.");
            if (!source.Resolved) return CoreLoadResult.Failed(source.Problem);

            return LoadFile(source.GraphFile, source.CoreFolder, source.Folder);
        }

        /// <summary>
        /// Reads the core copied into a project's own <c>repo\core\</c>, which is what every
        /// comparison runs against - never the repository itself.
        /// </summary>
        /// <param name="folderInRepository">
        /// The core's path inside its repository, from <c>coreLocalRepositoryConfig.folder</c>.
        /// The copy mirrors the core folder, but a node's <c>file</c> is still written
        /// relative to the repository root, so the prefix is needed to resolve one.
        /// </param>
        public static CoreLoadResult LoadFromProject(string projectDirectory, string folderInRepository, string dependencyFile)
        {
            string coreFolder = RepoPaths.CoreFor(projectDirectory);

            if (coreFolder == null)
                return CoreLoadResult.Failed("This project has no folder yet, so there is no core copied into it.");

            if (string.IsNullOrWhiteSpace(dependencyFile))
                return CoreLoadResult.Failed("coreLocalRepositoryConfig.dependencyFile is empty.");

            return LoadFile(Path.Combine(coreFolder, dependencyFile.Trim()), coreFolder, folderInRepository);
        }

        public static CoreLoadResult LoadFile(string path, string coreFolder, string folderInRepository)
        {
            if (string.IsNullOrWhiteSpace(path)) return CoreLoadResult.Failed("No dependency file was given.");

            if (!File.Exists(path))
                return CoreLoadResult.Failed("'" + path + "' does not exist.");

            try
            {
                using (FileStream stream = File.OpenRead(path))
                    return Load(stream, coreFolder, folderInRepository, path);
            }
            catch (Exception exception)
            {
                return CoreLoadResult.Failed("'" + path + "' could not be read: " + exception.Message);
            }
        }

        /// <summary>
        /// The primitive the others funnel into. Takes a stream so the catalogue can be
        /// exercised without a repository on disk.
        /// </summary>
        /// <param name="source">Only used to name the file in a message.</param>
        public static CoreLoadResult Load(Stream json, string coreFolder, string folderInRepository, string source = null)
        {
            if (json == null) return CoreLoadResult.Failed("No dependency stream was given.");

            try
            {
                byte[] bytes = ReadAll(json);
                int offset = StartsWithByteOrderMark(bytes) ? 3 : 0;

                if (IsBlank(bytes, offset))
                    return CoreLoadResult.Failed(Describe(source, "is empty."));

                using (MemoryStream payload = new MemoryStream(bytes, offset, bytes.Length - offset, false))
                {
                    CoreGraph graph = new DataContractJsonSerializer(typeof(CoreGraph)).ReadObject(payload) as CoreGraph;

                    return graph == null
                        ? CoreLoadResult.Failed(Describe(source, "is not a dependency file."))
                        : CoreLoadResult.Loaded(CoreCatalog.Of(graph, coreFolder, folderInRepository));
                }
            }
            catch (Exception exception)
            {
                // Broad on purpose, as in ConfigLoader: the serializer reports malformed
                // input as SerializationException, XmlException or FormatException depending
                // on how it is malformed, and this runs inside TIA Portal, where a file
                // somebody edited by hand must not take the host down.
                return CoreLoadResult.Failed(Describe(source, "could not be parsed: " + exception.Message));
            }
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        private static bool IsBlank(byte[] bytes, int offset)
        {
            for (int i = offset; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b != 0x20 && b != 0x09 && b != 0x0D && b != 0x0A) return false;
            }

            return true;
        }

        private static bool StartsWithByteOrderMark(byte[] bytes) =>
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        private static string Describe(string source, string problem) =>
            string.IsNullOrEmpty(source) ? "The dependency file " + problem : "'" + source + "' " + problem;
    }
}
