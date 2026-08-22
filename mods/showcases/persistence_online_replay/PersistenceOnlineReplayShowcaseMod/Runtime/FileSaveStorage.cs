using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ludots.Platform.Abstractions;

namespace PersistenceOnlineReplayShowcaseMod.Runtime;

internal sealed class FileSaveStorage : ISaveStorage
{
    private readonly string _root;
    public FileSaveStorage(string root) => _root = root ?? throw new ArgumentNullException(nameof(root));
    public IReadOnlyList<string> ListFileKeys(string prefix) => Directory.Exists(_root)
        ? Directory.EnumerateFiles(_root, "*.ldsave", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_root, path).Replace('\\', '/'))
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray()
        : Array.Empty<string>();
    public bool Exists(string key) => File.Exists(Resolve(key));
    public byte[] ReadAllBytes(string key) => File.ReadAllBytes(Resolve(key));
    public void WriteAllBytes(string key, byte[] bytes)
    {
        string path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
    public void CommitTempFile(string tempKey, string finalKey)
    {
        string temp = Resolve(tempKey);
        string final = Resolve(finalKey);
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        File.Move(temp, final, true);
    }
    public void Delete(string key)
    {
        string path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
    }
    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(key))
            throw new InvalidOperationException($"Invalid save storage key '{key}'.");
        return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
