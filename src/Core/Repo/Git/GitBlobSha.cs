using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Core.Repo.Git
{
    /// <summary>
    /// The hash git gives a file's contents, computed here so a file already on disk can be
    /// held against a listing from the repository without downloading it.
    ///
    /// **This is what replaces an index file.** The obvious way to download only what changed
    /// is to write down what was fetched last time - and then that record is a second thing to
    /// keep in step, wrong the moment somebody edits a file in the copy, deletes it, or
    /// restores an old one. The copy being its own index cannot drift: a file whose content
    /// hash no longer matches the repository's is fetched again, whatever happened to it.
    ///
    /// **The format is git's and it is not a plain SHA-1 of the bytes**: git hashes the string
    /// <c>blob &lt;length&gt;\0</c> followed by the content. Hashing only the content produces
    /// a value that never matches anything, which is a mistake that fails quietly - every file
    /// downloads every time and nothing looks broken.
    ///
    /// **SHA-1 here is not a security claim.** It is the identifier git uses, so this has to
    /// be the same function to compare with it at all.
    ///
    /// **It lives under <c>Git\</c> rather than under a host's name**, because nothing in it is
    /// one host's: GitHub, GitLab, Gitea and Azure DevOps all answer this same blob id in their
    /// listings, so a second provider needs no second hash. A remote that was not git would be
    /// the thing that changed, and it would change <see cref="Remote.RemoteCopy"/>, not this.
    /// </summary>
    public static class GitBlobSha
    {
        /// <summary>The hash of a file on disk, or null when it cannot be read.</summary>
        public static string OfFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                return Of(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                // Locked, gone between the check and the read, or denied. Null means "no idea
                // what is there", and the caller downloads it again - which is the safe half.
                return null;
            }
        }

        /// <summary>The hash of these bytes, in lower-case hex, as git and GitHub write it.</summary>
        public static string Of(byte[] content)
        {
            if (content == null) return null;

            byte[] header = Encoding.ASCII.GetBytes("blob " + content.Length + "\0");

            using (SHA1 sha = SHA1.Create())
            {
                sha.TransformBlock(header, 0, header.Length, null, 0);
                sha.TransformFinalBlock(content, 0, content.Length);

                return Hex(sha.Hash);
            }
        }

        private static string Hex(byte[] hash)
        {
            StringBuilder text = new StringBuilder(hash.Length * 2);

            foreach (byte value in hash) text.Append(value.ToString("x2"));

            return text.ToString();
        }
    }
}
