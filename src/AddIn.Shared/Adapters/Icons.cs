using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// Materialises the embedded assets into System.Drawing.Icon, which is what the TIA
    /// menu API expects.
    ///
    /// The lookup itself lives in Assets and returns a plain Stream; only this conversion
    /// needs System.Drawing, and keeping the two apart is what lets the lookup stay free
    /// of it.
    /// </summary>
    public static class Icons
    {
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
            using (Stream stream = Assets.Open(path))
                return stream == null ? null : new Icon(stream);
        }
    }
}
