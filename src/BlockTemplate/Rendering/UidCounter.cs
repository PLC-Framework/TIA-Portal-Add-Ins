using System;
using System.Collections.Generic;

namespace BlockTemplate.Rendering
{
    /// <summary>
    /// <c>uid("key")</c> - the same number for the same key, within one file - and <c>uid()</c>, a
    /// number of its own each time. The numbers only have to be unique while rendering; putting them
    /// in TIA's order is stage 1.3's.
    ///
    /// **A method with a default, not a lambda**: Scriban reads the parameters of the method a
    /// delegate points at, and a lambda's has no default, which made <c>uid()</c> an error -
    /// measured on Scriban 7.5.0.
    /// </summary>
    internal sealed class UidCounter
    {
        /// <summary>Where counting starts: the first UId TIA gives a part of a network.</summary>
        public const int First = 21;

        private readonly Dictionary<string, int> _keys = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _next = First;

        public int Next(string key = null)
        {
            if (key == null) return _next++;
            if (!_keys.TryGetValue(key, out int number))
            {
                number = _next++;
                _keys[key] = number;
            }

            return number;
        }
    }
}
