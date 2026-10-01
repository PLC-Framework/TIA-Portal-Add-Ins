using System;
using System.Collections;
using System.Collections.Generic;

using Scriban.Runtime;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// A list the template reads and cannot change. The object holding it being read-only protects
    /// the name, not the entries, and <see cref="ScriptArray.IsReadOnly"/> protects members, not
    /// entries either: <c>{{ sensors[0] = "x" }}</c> changed the list through both - Codex's review
    /// of stage 1.2 found it, and the second was measured.
    ///
    /// **Scriban writes an entry through <see cref="IList"/>**, which <c>ScriptArray</c> implements
    /// explicitly - and an explicit implementation cannot be overridden, which is why overriding the
    /// indexer alone changed nothing, also measured. Naming <see cref="IList"/> again here
    /// re-implements it, and that is what reaches the write.
    /// </summary>
    internal sealed class ReadOnlyList : ScriptArray, IList
    {
        private readonly bool _filled;

        public ReadOnlyList(IEnumerable<string> entries)
        {
            foreach (string entry in entries) base.Add(entry);
            _filled = true;
            IsReadOnly = true;
        }

        public override object this[int index]
        {
            get => base[index];
            set => Refuse();
        }

        public override void Add(object item)
        {
            if (_filled) Refuse();
            base.Add(item);
        }

        public override void Insert(int index, object item) => Refuse();

        public override void RemoveAt(int index) => Refuse();

        public override bool Remove(object item)
        {
            Refuse();
            return false;
        }

        public override void Clear() => Refuse();

        object IList.this[int index]
        {
            get => base[index];
            set => Refuse();
        }

        int IList.Add(object value)
        {
            Refuse();
            return -1;
        }

        void IList.Insert(int index, object value) => Refuse();

        void IList.Remove(object value) => Refuse();

        void IList.RemoveAt(int index) => Refuse();

        void IList.Clear() => Refuse();

        bool IList.IsReadOnly => true;

        bool IList.IsFixedSize => true;

        bool IList.Contains(object value) => Contains(value);

        int IList.IndexOf(object value) => IndexOf(value);

        int ICollection.Count => Count;

        object ICollection.SyncRoot => this;

        bool ICollection.IsSynchronized => false;

        void ICollection.CopyTo(Array array, int index)
        {
            for (int i = 0; i < Count; i++) array.SetValue(base[i], index + i);
        }

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<object>)this).GetEnumerator();

        private static void Refuse() =>
            throw new InvalidOperationException("The list is read-only: what the form filled in cannot be changed by the template.");
    }
}
