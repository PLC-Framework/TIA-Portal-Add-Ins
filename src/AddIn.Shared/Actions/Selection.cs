namespace AddIn.Shared.Actions
{
    /// <summary>
    /// How an action describes what was selected: "PLC", "3 blocks".
    ///
    /// It lives on its own because more than one action says it - the coding-style report puts
    /// it in its header, the export puts it in its notification - and two of them wording it
    /// differently would be two ways of saying the same thing to the same operator.
    /// </summary>
    public static class Selection
    {
        public static string Scope(int count, string one, string many) =>
            count == 1 ? one : count + " " + many;
    }
}
