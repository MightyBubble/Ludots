using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ludots.Platform.Abstractions
{
	public sealed class FileSystemSaveStorage : ISaveStorage
	{
		private readonly string _root;

		public FileSystemSaveStorage(string root)
		{
			if (string.IsNullOrWhiteSpace(root))
			{
				throw new ArgumentException("Save storage root is required.", nameof(root));
			}

			_root = Path.GetFullPath(root);
			Directory.CreateDirectory(_root);
		}

		public IReadOnlyList<string> ListFileKeys(string prefix)
		{
			string safePrefix = NormalizeKey(prefix ?? string.Empty, allowDirectory: true);
			string directory = ResolvePath(safePrefix);
			if (!Directory.Exists(directory))
			{
				return Array.Empty<string>();
			}

			return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
				.Select(ToKey)
				.OrderBy(key => key, StringComparer.Ordinal)
				.ToArray();
		}

		public bool Exists(string key) => File.Exists(ResolvePath(key));

		public byte[] ReadAllBytes(string key) => File.ReadAllBytes(ResolvePath(key));

		public void WriteAllBytes(string key, byte[] bytes)
		{
			string path = ResolvePath(key);
			string? directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.WriteAllBytes(path, bytes ?? Array.Empty<byte>());
		}

		public void CommitTempFile(string tempKey, string finalKey)
		{
			string tempPath = ResolvePath(tempKey);
			string finalPath = ResolvePath(finalKey);
			string? directory = Path.GetDirectoryName(finalPath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.Move(tempPath, finalPath, overwrite: true);
		}

		public void Delete(string key)
		{
			string path = ResolvePath(key);
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}

		private string ResolvePath(string key)
		{
			string normalized = NormalizeKey(key, allowDirectory: false);
			string path = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
			if (!path.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Save storage key resolved outside the configured root.");
			}

			return path;
		}

		private string ToKey(string path)
		{
			string relative = Path.GetRelativePath(_root, path);
			return relative.Replace(Path.DirectorySeparatorChar, '/');
		}

		private static string NormalizeKey(string key, bool allowDirectory)
		{
			string normalized = key.Replace('\\', '/').TrimStart('/');
			if (string.IsNullOrWhiteSpace(normalized))
			{
				return allowDirectory ? string.Empty : throw new ArgumentException("Save storage key is required.", nameof(key));
			}

			if (normalized.Contains("..", StringComparison.Ordinal))
			{
				throw new InvalidOperationException("Save storage key must not contain '..'.");
			}

			return normalized;
		}
	}
}
