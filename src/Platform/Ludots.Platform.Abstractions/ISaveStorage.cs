using System.Collections.Generic;

namespace Ludots.Platform.Abstractions
{
    public interface ISaveStorage
    {
        IReadOnlyList<string> ListFileKeys(string prefix);
        bool Exists(string key);
        byte[] ReadAllBytes(string key);
        void WriteAllBytes(string key, byte[] bytes);
        void CommitTempFile(string tempKey, string finalKey);
        void Delete(string key);
    }
}
