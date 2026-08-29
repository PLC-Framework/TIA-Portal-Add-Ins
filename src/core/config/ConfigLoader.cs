using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace Core.Config
{
    /// <summary>
    /// Reads config.json into the model.
    ///
    /// Loading and validating are deliberately separate. This reports only why the file
    /// could not be turned into a Config at all; whether its contents make sense is the
    /// validator's job, and it needs a Config to work on.
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>
        /// Where the configuration lives for a TIA project directory. The convention is
        /// owned here because Core defines the file format.
        /// </summary>
        public static string PathFor(string projectDirectory) =>
            Path.Combine(projectDirectory ?? string.Empty, ConfigPaths.Folder, ConfigPaths.File);

        /// <summary>Reads the configuration belonging to a TIA project directory.</summary>
        public static ConfigLoadResult LoadFromProject(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return ConfigLoadResult.Failed("No project directory was given.");

            return LoadFile(PathFor(projectDirectory));
        }

        /// <summary>Reads the configuration from an explicit file path.</summary>
        public static ConfigLoadResult LoadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ConfigLoadResult.Failed("No configuration path was given.");

            if (!File.Exists(path))
                return ConfigLoadResult.Failed($"'{path}' does not exist.");

            try
            {
                using (FileStream stream = File.OpenRead(path))
                    return Load(stream, path);
            }
            catch (IOException ex)
            {
                return ConfigLoadResult.Failed($"'{path}' could not be read: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return ConfigLoadResult.Failed($"'{path}' could not be read: {ex.Message}");
            }
        }

        /// <summary>
        /// The primitive every other overload funnels into. Takes a stream so the model
        /// can be exercised without touching the disk.
        /// </summary>
        /// <param name="source">Only used to name the file in error messages.</param>
        public static ConfigLoadResult Load(Stream json, string source = null)
        {
            if (json == null)
                return ConfigLoadResult.Failed("No configuration stream was given.");

            try
            {
                byte[] bytes = ReadAll(json);

                // DataContractJsonSerializer rejects a UTF-8 byte order mark outright and
                // reports it as "Encountered unexpected character 'i'", which is impossible
                // to act on. Visual Studio and most editors add one when saving UTF-8, and
                // this file is meant to be hand-edited, so skip it instead.
                int offset = StartsWithUtf8ByteOrderMark(bytes) ? 3 : 0;

                // An emptied file would otherwise surface as "Expecting element 'root'".
                if (IsBlank(bytes, offset))
                    return ConfigLoadResult.Failed(Describe(source, "is empty."));

                using (MemoryStream payload = new MemoryStream(bytes, offset, bytes.Length - offset, false))
                {
                    Config config = new DataContractJsonSerializer(typeof(Config)).ReadObject(payload) as Config;

                    return config == null
                        ? ConfigLoadResult.Failed(Describe(source, "is not a configuration file."))
                        : ConfigLoadResult.Loaded(config);
                }
            }
            catch (Exception ex)
            {
                // Broad on purpose: DataContractJsonSerializer reports malformed input as
                // SerializationException, XmlException or FormatException depending on how
                // it is malformed. This runs inside TIA Portal, where a corrupt file the
                // user hand-edited must not take the host process down.
                return ConfigLoadResult.Failed(Describe(source, $"could not be parsed: {ex.Message}"));
            }
        }

        private static byte[] ReadAll(Stream stream)
        {
            // Configuration files are a few KB, so buffering costs nothing and spares the
            // caller from having to provide a seekable stream just so the BOM can be peeked.
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

        private static bool StartsWithUtf8ByteOrderMark(byte[] bytes) =>
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        private static string Describe(string source, string problem) =>
            string.IsNullOrEmpty(source) ? $"The configuration {problem}" : $"'{source}' {problem}";
    }
}
