using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AddIn.Shared
{
    /// <summary>
    /// Opens the binary assets embedded from the repo-level assets\ folder.
    ///
    /// This returns a Stream rather than an image type on purpose: what is worth sharing
    /// is the lookup, not the type. Adapters/Icons materialises a System.Drawing.Icon
    /// from it for the TIA menu, and that conversion is what keeps System.Drawing out of
    /// the lookup itself.
    ///
    /// The assets are embedded in this assembly, and they have to be: embedded resources
    /// are scoped to the assembly that carries them, so a loader looking anywhere else
    /// finds nothing. Moving this class without moving the EmbeddedResource glob in
    /// AddIn.Shared.csproj breaks every lookup - and breaks it silently, because Open
    /// returns null by design so callers can degrade.
    /// </summary>
    public static class Assets
    {
        private static readonly Assembly Owner = typeof(Assets).Assembly;

        /// <summary>
        /// Opens "Feature/file.ico", mirroring the assets\ layout. Null when the asset is
        /// not embedded, so callers can degrade instead of failing. The caller owns the
        /// returned stream.
        /// </summary>
        public static Stream Open(string path)
        {
            string resource = Resolve(path);
            return resource == null ? null : Owner.GetManifestResourceStream(resource);
        }

        /// <summary>Whether the asset is embedded, without opening it.</summary>
        public static bool Exists(string path) => Resolve(path) != null;

        private static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string[] parts = path.Split('/', '\\');
            string fileName = parts[parts.Length - 1];
            string feature = parts.Length > 1 ? parts[parts.Length - 2] : null;

            // MSBuild derives names as "<RootNamespace>.Assets.<Feature>.<file>", but this
            // matches on the tail rather than on that prefix: a hardcoded prefix compiles
            // fine when it goes stale and only returns null at runtime, which is the kind
            // of failure that is very hard to trace from inside TIA Portal.
            return Owner.GetManifestResourceNames().FirstOrDefault(name =>
                name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) &&
                (feature == null || name.IndexOf(feature, StringComparison.OrdinalIgnoreCase) >= 0));
        }
    }
}
