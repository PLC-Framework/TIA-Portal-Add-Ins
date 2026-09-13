namespace Core.Config
{
    /// <summary>
    /// The closed sets of <c>codingStyle</c>, spelled once.
    ///
    /// Two components have to agree on every one of these strings: the validator, which
    /// refuses a <c>type</c> outside the set, and the Add-In, which names what it found in a
    /// PLC so the checker can look it up. A misspelling on the Add-In side does not fail - it
    /// reads as "no rule for this type", which is a wrong answer that looks exactly like a
    /// right one. Constants make that a compile error instead.
    ///
    /// Spelled as TIA spells them, which is what lets every comparison be ordinal.
    /// </summary>
    public static class CodingStyleNames
    {
        // codingStyle.blocks
        public const string OB = "OB";
        public const string ArrayDB = "ArrayDB";
        public const string GlobalDB = "GlobalDB";
        public const string InstanceDB = "InstanceDB";
        public const string FC = "FC";
        public const string FB = "FB";

        // codingStyle.technologyObjects, tagTables, types, alarmTextLists
        public const string TechnologicalInstanceDB = "TechnologicalInstanceDB";
        public const string PlcTagTable = "PlcTagTable";
        public const string PlcStruct = "PlcStruct";
        public const string AlarmTexts = "AlarmTexts";

        // An interface section: the six of a block interface...
        public const string Input = "Input";
        public const string Output = "Output";
        public const string InOut = "InOut";
        public const string Static = "Static";
        public const string Temp = "Temp";
        public const string Constant = "Constant";

        // ...and the two of a tag table, named apart because they answer to different rules.
        public const string Tag = "Tag";
        public const string UserConstant = "UserConstant";
    }
}
