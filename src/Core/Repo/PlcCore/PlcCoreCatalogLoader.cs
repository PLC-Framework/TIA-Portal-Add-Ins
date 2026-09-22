using System;
using System.IO;
using System.Runtime.Serialization.Json;

using Core.Repo.PlcCore.Graph;

namespace Core.Repo.PlcCore
{
    /// <summary>
    /// Reads <c>core.json</c> into a <see cref="PlcCoreCatalog"/>.
    ///
    /// Loading and validating are separate here for the same reason they are for
    /// <c>config.json</c>: this says only why the file could not be turned into a catalogue
    /// at all, and <see cref="PlcCoreValidator"/> says what is wrong with one that loaded.
    ///
    /// **The parsing is `ConfigLoader`'s, byte for byte**, including the byte order mark it
    /// has to skip and the empty file it has to name. `DataContractJsonSerializer` reports a
    /// BOM as *"Encountered unexpected character 'i'"*, which nobody can act on, and both
    /// files are written by tools that add one.
    /// </summary>
    public static class PlcCoreCatalogLoader
    {
        /// <summary>
        /// Reads the graph a <see cref="Local.LocalSource"/> points at, **in place** - for a
        /// tool or a test that wants to look at a repository. It has nowhere to bring a source,
        /// so the catalogue it returns resolves no files; a project always reads its own copy.
        /// </summary>
        public static PlcCoreLoadResult Load(Local.LocalSource source)
        {
            if (source == null) return PlcCoreLoadResult.Failed("No repository was given.");
            if (!source.Resolved) return PlcCoreLoadResult.Failed(source.Problem);

            return LoadFile(source.GraphFile, null, source.Folder);
        }

        /// <summary>
        /// Reads the graph copied into a project's own <c>repo\core.json</c>, which is what every
        /// comparison runs against - never the repository itself.
        ///
        /// **The sources resolve into <c>repo\tmp\</c>**, which is where a download brings them
        /// and the only place anything reads them from.
        /// </summary>
        /// <param name="folderInRepository">
        /// The core's path inside its repository, from the configuration's <c>folder</c>. A
        /// node's <c>file</c> is written relative to the repository root, so this is the prefix
        /// that says which part of it is the core - and so which folder in TIA it belongs in.
        /// </param>
        public static PlcCoreLoadResult LoadFromProject(string projectDirectory, string folderInRepository)
        {
            string graph = RepoPaths.GraphFor(projectDirectory);

            if (graph == null)
                return PlcCoreLoadResult.Failed("This project has no folder yet, so there is no core copied into it.");

            return LoadFile(graph, RepoPaths.TmpFor(projectDirectory), folderInRepository);
        }

        public static PlcCoreLoadResult LoadFile(string path, string sourceFolder, string folderInRepository)
        {
            if (string.IsNullOrWhiteSpace(path)) return PlcCoreLoadResult.Failed("No dependency file was given.");

            if (!File.Exists(path))
                return PlcCoreLoadResult.Failed("'" + path + "' does not exist.");

            try
            {
                using (FileStream stream = File.OpenRead(path))
                    return Load(stream, sourceFolder, folderInRepository, path);
            }
            catch (Exception exception)
            {
                return PlcCoreLoadResult.Failed("'" + path + "' could not be read: " + exception.Message);
            }
        }

        /// <summary>
        /// The primitive the others funnel into. Takes a stream so the catalogue can be
        /// exercised without a repository on disk.
        /// </summary>
        /// <param name="source">Only used to name the file in a message.</param>
        public static PlcCoreLoadResult Load(Stream json, string sourceFolder, string folderInRepository, string source = null)
        {
            if (json == null) return PlcCoreLoadResult.Failed("No dependency stream was given.");

            try
            {
                byte[] bytes = ReadAll(json);
                int offset = StartsWithByteOrderMark(bytes) ? 3 : 0;

                if (IsBlank(bytes, offset))
                    return PlcCoreLoadResult.Failed(Describe(source, "is empty."));

                using (MemoryStream payload = new MemoryStream(bytes, offset, bytes.Length - offset, false))
                {
                    DependencyGraph graph = new DataContractJsonSerializer(typeof(DependencyGraph)).ReadObject(payload) as DependencyGraph;

                    return graph == null
                        ? PlcCoreLoadResult.Failed(Describe(source, "is not a dependency file."))
                        : PlcCoreLoadResult.Loaded(PlcCoreCatalog.Of(graph, sourceFolder, folderInRepository));
                }
            }
            catch (Exception exception)
            {
                // Broad on purpose, as in ConfigLoader: the serializer reports malformed
                // input as SerializationException, XmlException or FormatException depending
                // on how it is malformed, and this runs inside TIA Portal, where a file
                // somebody edited by hand must not take the host down.
                return PlcCoreLoadResult.Failed(Describe(source, "could not be parsed: " + exception.Message));
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
