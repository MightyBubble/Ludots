using System.Collections.Generic;

namespace Ludots.Platform.Abstractions
{
    public interface ISaveStorage
    {
        /// <summary>
        /// Absolute filesystem root when the adapter is path-backed; empty otherwise.
        /// Author-facing display only — never used as an IO decision input.
        /// </summary>
        string DisplayRoot { get; }

        IReadOnlyList<string> ListFileKeys(string prefix);
        bool Exists(string key);
        byte[] ReadAllBytes(string key);
        void WriteAllBytes(string key, byte[] bytes);
        void CommitTempFile(string tempKey, string finalKey);
        void Delete(string key);
    }
}
