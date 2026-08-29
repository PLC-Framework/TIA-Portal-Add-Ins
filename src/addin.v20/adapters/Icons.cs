using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AddIn.Adapters
{
    /// <summary>
    /// Resolves the icons embedded from the repo-level assets\ folder into
    /// System.Drawing.Icon, which is what the TIA menu API expects.
    ///
    /// Paths are given as "Feature/file.ico", mirroring the assets\ layout, so a
    /// caller in Core can name its icon without knowing anything about resources
    /// or about System.Drawing.
    /// </summary>
    internal static class Icons
    {
        private static readonly Assembly Owner = typeof(Icons).Assembly;
        private static readonly Dictionary<string, Icon> Cache =
            new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the icon for "Feature/file.ico", or null when it is not embedded.
        /// Callers fall back to a menu entry without an icon rather than failing.
        /// </summary>
        public static Icon Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // The cache is deliberate: GetContextMenuAddIns() builds a new controller on
            // every right-click, so without it each icon would be re-read every time.
            // A miss is cached too, so a missing asset is not retried on every menu build.
            lock (Cache)
            {
                if (Cache.TryGetValue(path, out Icon cached)) return cached;

                Icon icon = Load(path);
                Cache[path] = icon;
                return icon;
            }
        }

        private static Icon Load(string path)
        {
            string[] parts = path.Split('/', '\\');
            string fileName = parts[parts.Length - 1];
            string feature = parts.Length > 1 ? parts[parts.Length - 2] : null;

            // Resource names are "<RootNamespace>.Assets.<Feature>.<file>", but matching on
            // the tail instead of a hardcoded prefix keeps this working when the root
            // namespace or the link path changes - which would otherwise fail silently at
            // runtime, since GetManifestResourceStream just returns null.
            string resource = Owner.GetManifestResourceNames().FirstOrDefault(name =>
                name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) &&
                (feature == null || name.IndexOf(feature, StringComparison.OrdinalIgnoreCase) >= 0));

            if (resource == null) return null;

            using (Stream stream = Owner.GetManifestResourceStream(resource))
                return stream == null ? null : new Icon(stream);
        }
    }
}
