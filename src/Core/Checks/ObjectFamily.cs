namespace Core.Checks
{
    /// <summary>
    /// Which of the five <c>codingStyle</c> lists holds the rules for an object.
    ///
    /// An enum rather than the JSON key as a string, because the caller is the Add-In and a
    /// misspelt "tagTables" would read as "this type is not configured" - a wrong answer
    /// that looks exactly like a right one.
    /// </summary>
    public enum ObjectFamily
    {
        Blocks,
        TechnologyObjects,
        TagTables,
        Types,
        AlarmTextLists
    }
}
