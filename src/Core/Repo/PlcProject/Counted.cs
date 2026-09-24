using System.Runtime.Serialization;

namespace Core.Repo.PlcProject
{
    /// <summary>
    /// One name and how many of it there are.
    ///
    /// **Serialized since 2026-09-23**, because the counts travel inside `repo\project.json`
    /// rather than being walked for again: public setters for the same reason `ProjectObject`
    /// has them, and a parameterless constructor because that is what the serializer calls.
    /// </summary>
    [DataContract]
    public sealed class Counted
    {
        public Counted()
        {
        }

        public Counted(string name, int count)
        {
            Name = name;
            Count = count;
        }

        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; }

        [DataMember(Name = "count", Order = 1)]
        public int Count { get; set; }

        /// <summary>
        /// What a tick box shows: <c>SCL (312)</c>. **`ToString` and not a template binding**,
        /// because a bound object's `ToString` is also what a screen reader and a test read -
        /// the trap `GroupNode` and `DataBlockItem` both hit before this.
        /// </summary>
        public override string ToString() => Name + " (" + Count + ")";
    }
}
