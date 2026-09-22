namespace Core.Repo.Remote
{
    /// <summary>
    /// One file in a remote repository: where it is, and the hash of what is in it.
    ///
    /// **The hash is the whole design.** It is git's own, over the content, so the same hash
    /// can be computed from the file already on disk - which is what lets the copy be its own
    /// index and a second download fetch nothing. See <see cref="Git.GitBlobSha"/>.
    /// </summary>
    public sealed class RemoteFile
    {
        public RemoteFile(string path, string hash, long size)
        {
            Path = path;
            Hash = hash;
            Size = size;
        }

        /// <summary>The path from the repository root, with forward slashes.</summary>
        public string Path { get; }

        public string Hash { get; }

        public long Size { get; }

        public override string ToString() => Path;
    }
}
