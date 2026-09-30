using System;
using System.Collections.Generic;
using System.Linq;

using Core.Config;
using Core.Imports;

namespace Core.Repo.PlcCore
{
    /// <summary>
    /// The closed set a node's <c>kind</c> comes from, spelled once - and in the words the rest
    /// of the framework already uses for an object's kind: <see cref="CodingStyleNames"/>' for
    /// what TIA names, and <see cref="DeclaredObject.DataBlock"/> for a <c>DATA_BLOCK</c> source,
    /// which does not say whether it is a global or an instance data block.
    ///
    /// **Why the graph says it at all**: a call is written differently for an FC and an FB - an
    /// FB's needs an instance - and the only thing that knows which is the declaration, which
    /// the generator reads and this side never does.
    /// </summary>
    public static class PlcCoreKind
    {
        public const string FB = CodingStyleNames.FB;
        public const string FC = CodingStyleNames.FC;
        public const string OB = CodingStyleNames.OB;
        public const string DB = DeclaredObject.DataBlock;
        public const string PlcStruct = CodingStyleNames.PlcStruct;
        public const string PlcTagTable = CodingStyleNames.PlcTagTable;

        public static readonly IReadOnlyList<string> All = new[] { FB, FC, OB, DB, PlcStruct, PlcTagTable };

        public static bool IsKnown(string kind) => All.Contains(kind, StringComparer.Ordinal);

        /// <summary>An FB or an FC: what a call can be written to.</summary>
        public static bool IsCallable(string kind) =>
            string.Equals(kind, FB, StringComparison.Ordinal) || string.Equals(kind, FC, StringComparison.Ordinal);
    }
}
