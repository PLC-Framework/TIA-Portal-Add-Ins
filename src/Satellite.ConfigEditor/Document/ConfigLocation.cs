using System;
using System.IO;

using Core.Config;

namespace Satellite.ConfigEditor.Document
{
    /// <summary>
    /// Turns whatever names a project into the config.json it means.
    ///
    /// **Shared rather than private to the window on purpose**: the single-instance guard
    /// keys on this path too, and two ways of working it out would let two windows open the
    /// same file while each believed it was alone.
    /// </summary>
    public static class ConfigLocation
    {
        /// <summary>
        /// The convention is <c>&lt;project&gt;\.plc-framework\config.json</c>, and simply
        /// appending it to whatever was picked is wrong the moment somebody picks one step
        /// deeper - which is the natural thing to do, because <c>.plc-framework</c> is the
        /// folder with the file visibly in it. That produced
        /// <c>…\.plc-framework\.plc-framework\config.json</c> and a window reporting no
        /// configuration on a project that had one.
        ///
        /// So all three sensible answers are accepted: the project folder, the
        /// <c>.plc-framework</c> inside it, or the file itself.
        /// </summary>
        public static string Resolve(string chosen)
        {
            if (string.IsNullOrWhiteSpace(chosen)) return null;

            try
            {
                // The file itself. The Vista-style folder picker has a path box, so this
                // can be typed or pasted even though it only lists folders.
                if (File.Exists(chosen))
                {
                    return ConfigPaths.File.Equals(Path.GetFileName(chosen),
                                                   StringComparison.OrdinalIgnoreCase)
                        ? chosen
                        : ConfigLoader.PathFor(Path.GetDirectoryName(chosen));
                }

                // .plc-framework itself.
                if (ConfigPaths.Folder.Equals(new DirectoryInfo(chosen).Name,
                                              StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(chosen, ConfigPaths.File);

                // The ordinary case: the project folder.
                return ConfigLoader.PathFor(chosen);
            }
            catch (Exception)
            {
                // A malformed path is the operator's to fix, and the window says so plainly
                // rather than throwing at them.
                return ConfigLoader.PathFor(chosen);
            }
        }
    }
}
