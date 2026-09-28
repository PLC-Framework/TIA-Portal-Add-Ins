using Core.Config;

namespace Core.Imports
{
    /// <summary>
    /// One object a file says it holds: its name, and what kind of object it is. What an
    /// import is checked against - whether the PLC already has something of that name - and
    /// what decides the order files go in.
    /// </summary>
    public sealed class DeclaredObject
    {
        /// <summary>
        /// The kind of a data block declared in a source, which says <c>DATA_BLOCK</c> and not
        /// which kind of data block it is. A SimaticML export always says, and there the kind
        /// is <see cref="CodingStyleNames"/>' own.
        /// </summary>
        public const string DataBlock = "DB";

        public DeclaredObject(string name, string kind)
        {
            Name = name;
            Kind = kind;
        }

        /// <summary>The object's name in TIA, as the file writes it.</summary>
        public string Name { get; }

        /// <summary>
        /// <see cref="CodingStyleNames"/>' vocabulary - <c>FB</c>, <c>GlobalDB</c>,
        /// <c>PlcStruct</c>, <c>PlcTagTable</c>, <c>TechnologicalInstanceDB</c> - or
        /// <see cref="DataBlock"/>. An element SimaticML has and this does not know keeps the
        /// last segment of its own name, and ranks as unknown.
        /// </summary>
        public string Kind { get; }

        public ImportRank Rank => RankOf(Kind);

        public static ImportRank RankOf(string kind)
        {
            switch (kind)
            {
                case CodingStyleNames.PlcStruct:
                    return ImportRank.Type;
                case CodingStyleNames.PlcTagTable:
                    return ImportRank.TagTable;
                case CodingStyleNames.FC:
                case CodingStyleNames.FB:
                    return ImportRank.Code;
                case CodingStyleNames.GlobalDB:
                case CodingStyleNames.InstanceDB:
                case CodingStyleNames.ArrayDB:
                case CodingStyleNames.TechnologicalInstanceDB:
                case DataBlock:
                    return ImportRank.Data;
                case CodingStyleNames.OB:
                    return ImportRank.Organization;
                default:
                    return ImportRank.Unknown;
            }
        }

        public override string ToString() => Kind + " " + Name;
    }
}
