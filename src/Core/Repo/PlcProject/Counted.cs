namespace Core.Repo.PlcProject
{
    /// <summary>One name and how many of it there are.</summary>
    public sealed class Counted
    {
        public Counted(string name, int count)
        {
            Name = name;
            Count = count;
        }

        public string Name { get; }

        public int Count { get; }

        /// <summary>
        /// What a tick box shows: <c>SCL (312)</c>. **`ToString` and not a template binding**,
        /// because a bound object's `ToString` is also what a screen reader and a test read -
        /// the trap `GroupNode` and `DataBlockItem` both hit before this.
        /// </summary>
        public override string ToString() => Name + " (" + Count + ")";
    }
}
