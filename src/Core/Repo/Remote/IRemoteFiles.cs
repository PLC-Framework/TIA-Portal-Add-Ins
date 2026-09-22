using System;
using System.Collections.Generic;

namespace Core.Repo.Remote
{
    /// <summary>
    /// One repository, open: which commit it is at, what is in it, and one file's bytes.
    ///
    /// **Three questions in that order, and the first two are what make the third rare.** A
    /// core is a few hundred files and a project uses a handful of them, so the third question
    /// is asked for exactly what is needed: the graph on a Load, and the sources an import is
    /// about to read. A file's own hash then says whether even that one has to come down.
    /// </summary>
    public interface IRemoteFiles : IDisposable
    {
        /// <summary>The commit the branch is at.</summary>
        string Head();

        /// <summary>
        /// Every file under <paramref name="folder"/> at that commit, with the hash of each.
        ///
        /// **Whoever implements this refuses a listing it cannot vouch for.** A partial
        /// answer read as a whole one is a core quietly missing blocks, which is the one
        /// thing a comparison must never be handed.
        /// </summary>
        IReadOnlyList<RemoteFile> Under(string commit, string folder);

        /// <summary>One file's bytes, by the hash <see cref="Under"/> gave for it.</summary>
        byte[] Read(string hash);
    }
}
