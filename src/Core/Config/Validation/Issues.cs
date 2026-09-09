using System;
using System.Collections.Generic;

namespace Core.Config.Validation
{
    /// <summary>
    /// The accumulator every validator writes into, plus the handful of checks they all
    /// repeat.
    ///
    /// Internal on purpose: the validators are the public surface, and a caller assembling
    /// its own issue list would be writing a validator without saying so.
    /// </summary>
    internal sealed class Issues
    {
        private readonly List<ValidationIssue> _issues = new List<ValidationIssue>();

        public List<ValidationIssue> All => _issues;

        public void Add(string path, string message) =>
            _issues.Add(new ValidationIssue(path, message));

        /// <summary>
        /// A required string. Blank counts as missing: a key present with an empty value
        /// says no more than an absent one, and treating them differently would only make
        /// the same mistake report two different ways.
        /// </summary>
        /// <returns>True when the value is usable, so the caller can skip dependent checks.</returns>
        public bool Required(string path, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) return true;

            Add(path, "Required.");
            return false;
        }

        /// <summary>
        /// A required object. Absent is an error; what is inside it is the caller's business.
        /// </summary>
        public bool RequiredObject(string path, object value)
        {
            if (value != null) return true;

            Add(path, "Required.");
            return false;
        }

        /// <summary>
        /// A required list that is allowed to be empty.
        ///
        /// The distinction matters and is the reason this is not the same as
        /// <see cref="RequiredObject"/> with a different message: <c>[]</c> states that
        /// this concern has nothing in it, while a missing key states nothing at all. Only
        /// one of the two is a decision somebody made.
        /// </summary>
        public bool RequiredList<T>(string path, IReadOnlyList<T> value)
        {
            if (value != null) return true;

            Add(path, "Required. Use [] to say there are none.");
            return false;
        }

        public void OneOf(string path, string value, params string[] allowed)
        {
            foreach (string candidate in allowed)
                if (string.Equals(value, candidate, StringComparison.Ordinal)) return;

            Add(path, "Must be one of: " + string.Join(", ", allowed) + ".");
        }

        /// <summary>Appends a member: <c>metadata</c> + <c>coreSource</c>.</summary>
        public static string Field(string path, string name) =>
            string.IsNullOrEmpty(path) ? name : path + "." + name;

        /// <summary>Appends a subscript: <c>rules</c> + 3 → <c>rules[3]</c>.</summary>
        public static string At(string path, int index) => path + "[" + index + "]";
    }
}
