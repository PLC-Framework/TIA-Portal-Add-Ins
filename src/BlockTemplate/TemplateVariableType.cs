namespace BlockTemplate
{
    /// <summary>
    /// What a variable of the CONFIG-JSON holds, and so which field the form draws for it.
    /// Spelled in the file as <c>list</c>, <c>bool</c>, <c>numeric</c> and <c>string</c>.
    /// </summary>
    public enum TemplateVariableType
    {
        /// <summary>A list of strings - one entry per sensor, per axis, per whatever repeats.</summary>
        List,

        Bool,

        /// <summary>A number, integer or not, held as a <see cref="decimal"/>.</summary>
        Numeric,

        String
    }
}
