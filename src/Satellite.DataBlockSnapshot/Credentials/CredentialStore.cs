using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Core;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Satellite.DataBlockSnapshot.Credentials
{
    /// <summary>What was remembered for one CPU.</summary>
    public sealed class Credential
    {
        public Credential(string address, string user, string password)
        {
            Address = address;
            User = user;
            Password = password;
        }

        public string Address { get; }
        public string User { get; }

        /// <summary>In the clear. It is stored encrypted; decryption happens on the way out.</summary>
        public string Password { get; }
    }

    /// <summary>
    /// Remembers web server credentials so an afternoon of captures is not an afternoon
    /// of typing.
    ///
    /// <b>Per user, and outside the TIA project.</b> The obvious home would be the project
    /// itself, next to the exports - but TIA projects are under version control, and a
    /// file of credentials in there reaches a commit or a zipped copy sooner or later.
    /// It is also why the password is protected with DPAPI at <i>user</i> scope rather
    /// than machine scope: those two choices together mean this file is worthless to
    /// anyone but the person who wrote it, on the machine they wrote it on.
    ///
    /// The consequence, stated plainly: <b>it does not travel</b>. Copy the project to
    /// another station and the credentials are typed again. That is the price of not
    /// having a shared key, and a shared key would be obfuscation rather than encryption.
    ///
    /// Nothing here throws. A credential cache that breaks the application it is meant to
    /// smooth would be worse than no cache at all. **But a write says when it failed**: not
    /// throwing and not saying are two different things, and only the first was ever meant.
    /// </summary>
    public static class CredentialStore
    {
        // Application entropy, so a protected blob from some other program cannot be
        // dropped into this file and decrypted by it.
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("PLC-Framework.Satellite.DataBlockSnapshot.credentials.v1");

        // The folder is Core's to decide - three applications write into it - while the
        // file name stays here, because nothing else reads it.
        private static string FilePath => InstallPaths.UserFile("credentials.json");

        public static Credential Find(string projectDirectory, string plc, string address)
        {
            try
            {
                JArray entries = Load();
                JObject entry = Match(entries, projectDirectory, Key(plc, address));
                if (entry == null) return null;

                string password = Unprotect(entry["password"]?.Value<string>());
                if (password == null) return null;

                return new Credential(
                    entry["address"]?.Value<string>(),
                    entry["user"]?.Value<string>(),
                    password);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <returns>
        /// Null once it is stored, or why it could not be. **Still never a throw** - the capture
        /// already worked and this is a courtesy - but no longer a silence either: it used to
        /// swallow the failure, so a window whose Remember box was ticked said nothing, and the
        /// next launch asked for the password again with no way of knowing why.
        /// </returns>
        public static string Save(
            string projectDirectory, string plc, string address, string user, string password)
        {
            try
            {
                string key = Key(plc, address);
                if (string.IsNullOrWhiteSpace(key))
                    return "There is neither a PLC name nor an address to remember them under.";

                JArray entries = Load();
                Remove(entries, projectDirectory, key);

                entries.Add(new JObject
                {
                    ["project"] = projectDirectory ?? string.Empty,
                    ["key"] = key,
                    ["address"] = address,
                    ["user"] = user,
                    ["password"] = Protect(password)
                });

                Store(entries);
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        /// <returns>
        /// Null once nothing is kept for this CPU - including when nothing was - or why what was
        /// kept could not be removed, which matters more than it looks: a password somebody
        /// asked to forget is still on disk.
        /// </returns>
        public static string Forget(string projectDirectory, string plc, string address)
        {
            try
            {
                JArray entries = Load();
                if (Remove(entries, projectDirectory, Key(plc, address))) Store(entries);

                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        /// <summary>
        /// The device name from the project, which is stable when an address is not: a CPU
        /// can be readdressed, and it has several addresses anyway. The address is the
        /// fallback for a window started by hand, where no project handed a name over.
        /// </summary>
        private static string Key(string plc, string address) =>
            string.IsNullOrWhiteSpace(plc) ? (address ?? string.Empty).Trim() : plc.Trim();

        private static JObject Match(JArray entries, string projectDirectory, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            foreach (JToken token in entries)
            {
                JObject entry = token as JObject;
                if (entry == null) continue;

                if (Same(entry["project"]?.Value<string>(), projectDirectory) &&
                    Same(entry["key"]?.Value<string>(), key))
                    return entry;
            }

            return null;
        }

        private static bool Remove(JArray entries, string projectDirectory, string key)
        {
            JObject found = Match(entries, projectDirectory, key);
            if (found == null) return false;

            found.Remove();
            return true;
        }

        private static bool Same(string left, string right) =>
            string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
                          StringComparison.OrdinalIgnoreCase);

        private static JArray Load()
        {
            string path = FilePath;
            if (!File.Exists(path)) return new JArray();

            JObject document = JObject.Parse(File.ReadAllText(path));
            return document["entries"] as JArray ?? new JArray();
        }

        private static void Store(JArray entries)
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            File.WriteAllText(path,
                new JObject { ["entries"] = entries }.ToString(Formatting.Indented),
                new UTF8Encoding(false));
        }

        private static string Protect(string password)
        {
            if (password == null) password = string.Empty;

            byte[] cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(cipher);
        }

        private static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return null;

            try
            {
                byte[] clear = ProtectedData.Unprotect(
                    Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(clear);
            }
            catch (Exception)
            {
                // Written by another user, or on another machine. Not an error: it just
                // means there is nothing here for whoever is running now.
                return null;
            }
        }
    }
}
