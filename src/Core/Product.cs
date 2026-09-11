using System;
using System.Reflection;

namespace Core
{
    /// <summary>Literals every consumer shares.</summary>
    public static class Product
    {
        public const string Title = "PLC-Framework";

        /// <summary>What is reported when the version cannot be read at all.</summary>
        public const string UnknownVersion = "unknown";

        /// <summary>
        /// The shipped version, as `Directory.Build.props` set it — read from this
        /// assembly's own metadata so the number is stated in exactly one place.
        ///
        /// **Read from an attribute, never from `Assembly.GetName()`.** An `AssemblyName`
        /// carries the code base, so asking for it demands `FileIOPermission` — the same
        /// demand that makes `Assembly.Location` throw inside TIA Portal's sandbox. A custom
        /// attribute is metadata: no file is touched and no permission is needed. Measured
        /// in the restricted `AppDomain`, both of them.
        ///
        /// Computed once and never throwing: a window that cannot show its version is a
        /// small loss, and a version lookup that takes down the Add-In is not a trade
        /// anybody would make.
        /// </summary>
        public static string Version { get; } = ReadVersion();

        private static string ReadVersion()
        {
            try
            {
                Assembly assembly = typeof(Product).Assembly;

                // Informational first: it is what <Version> produces, and it is the number a
                // person recognises. FileVersion is the four-part fallback.
                AssemblyInformationalVersionAttribute informational =
                    (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                        assembly, typeof(AssemblyInformationalVersionAttribute));

                if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
                    return Displayable(informational.InformationalVersion);

                AssemblyFileVersionAttribute file =
                    (AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(
                        assembly, typeof(AssemblyFileVersionAttribute));

                return file != null && !string.IsNullOrWhiteSpace(file.Version)
                    ? file.Version
                    : UnknownVersion;
            }
            catch (Exception)
            {
                return UnknownVersion;
            }
        }

        /// <summary>
        /// Drops the build metadata the SDK appends.
        ///
        /// Building inside a git repository makes the SDK write the commit onto the
        /// informational version — <c>1.0.0+97fd4a55ab…</c> — which is useful in the
        /// assembly and noise in a window. The part before the <c>+</c> is what a person
        /// reads back to you in a bug report; the full string is still in the metadata for
        /// anyone who needs to pin down an exact build.
        /// </summary>
        private static string Displayable(string informational)
        {
            int build = informational.IndexOf('+');
            return build < 0 ? informational : informational.Substring(0, build);
        }
    }
}
