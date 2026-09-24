using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;

namespace Ludots.Core.Networking.Session
{
    /// <summary>
    /// Builds a path-independent multiplayer content identity from ordered mod ids and
    /// gameplay bytes (manifest, assets/config/map payload, declared main assembly only).
    /// Absolute install roots and host/adapter metadata are excluded.
    /// </summary>
    public static class ContentFingerprintCanonicalizer
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        public static ContentFingerprint FromOrderedMods(IReadOnlyList<ResolvedModLoadEntry> orderedMods)
        {
            if (orderedMods == null)
            {
                throw new ArgumentNullException(nameof(orderedMods));
            }

            if (orderedMods.Count == 0)
            {
                throw new ArgumentException("Ordered mods are required for a content fingerprint.", nameof(orderedMods));
            }

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var relativePaths = new List<string>(64);
            for (int i = 0; i < orderedMods.Count; i++)
            {
                ResolvedModLoadEntry entry = orderedMods[i]
                    ?? throw new ArgumentException($"Ordered mod at index {i} is null.", nameof(orderedMods));
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    throw new ArgumentException($"Ordered mod at index {i} is missing an id.", nameof(orderedMods));
                }

                if (string.IsNullOrWhiteSpace(entry.RootPath))
                {
                    throw new ArgumentException($"Ordered mod '{entry.Id}' is missing a root path.", nameof(orderedMods));
                }

                string root = Path.GetFullPath(entry.RootPath);
                if (!Directory.Exists(root))
                {
                    throw new DirectoryNotFoundException($"Ordered mod '{entry.Id}' root does not exist: {root}");
                }

                AppendText(hasher, "mod\n");
                AppendText(hasher, entry.Id);
                AppendText(hasher, "\n");

                string manifestPath = Path.Combine(root, "mod.json");
                if (!File.Exists(manifestPath))
                {
                    throw new FileNotFoundException($"Ordered mod '{entry.Id}' is missing mod.json.", manifestPath);
                }

                AppendFile(hasher, "mod.json", File.ReadAllBytes(manifestPath));

                ModManifest manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath);

                relativePaths.Clear();
                CollectGameplayRelativePaths(root, relativePaths);
                relativePaths.Sort(StringComparer.Ordinal);
                for (int pathIndex = 0; pathIndex < relativePaths.Count; pathIndex++)
                {
                    string relative = relativePaths[pathIndex];
                    byte[] bytes = File.ReadAllBytes(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                    AppendFile(hasher, relative, bytes);
                }

                if (!string.IsNullOrWhiteSpace(manifest.Main))
                {
                    if (Path.IsPathRooted(manifest.Main))
                    {
                        throw new InvalidOperationException(
                            $"Ordered mod '{entry.Id}' declares a rooted main assembly path.");
                    }

                    string mainFull = Path.GetFullPath(Path.Combine(root, manifest.Main));
                    string mainRelative = ToRootRelative(root, mainFull);

                    if (!File.Exists(mainFull))
                    {
                        throw new FileNotFoundException(
                            $"Ordered mod '{entry.Id}' declared main assembly is missing.",
                            mainFull);
                    }

                    AppendFile(hasher, "assembly:" + mainRelative, File.ReadAllBytes(mainFull));
                }
            }

            byte[] digest = hasher.GetHashAndReset();
            if (digest.Length != ContentFingerprint.ByteLength)
            {
                throw new InvalidOperationException(
                    $"Content fingerprint SHA-256 produced {digest.Length} bytes; expected {ContentFingerprint.ByteLength}.");
            }

            return ContentFingerprint.FromBytes(digest);
        }

        private static void CollectGameplayRelativePaths(string root, List<string> destination)
        {
            string assetsRoot = Path.Combine(root, "assets");
            if (Directory.Exists(assetsRoot))
            {
                CollectFilesRecursive(root, assetsRoot, destination);
            }

            foreach (string candidate in Directory.EnumerateFiles(root))
            {
                string name = Path.GetFileName(candidate);
                if (string.Equals(name, "game.json", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                {
                    destination.Add(NormalizeRelative(name));
                }
            }
        }

        private static void CollectFilesRecursive(string root, string directory, List<string> destination)
        {
            string[] files;
            string[] children;
            try
            {
                files = Directory.GetFiles(directory);
                children = Directory.GetDirectories(directory);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to enumerate gameplay content under '{directory}'.", ex);
            }

            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                destination.Add(ToRootRelative(root, files[i]));
            }

            Array.Sort(children, StringComparer.Ordinal);
            for (int i = 0; i < children.Length; i++)
            {
                string child = children[i];
                if (IsIgnoredDirectoryName(Path.GetFileName(child)))
                {
                    continue;
                }

                CollectFilesRecursive(root, child, destination);
            }
        }

        private static bool IsIgnoredDirectoryName(string name) =>
            string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase);

        private static string ToRootRelative(string root, string fullPath)
        {
            string relative = Path.GetRelativePath(root, fullPath);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Gameplay file escapes mod root: {fullPath}");
            }

            return NormalizeRelative(relative);
        }

        private static string NormalizeRelative(string relative) =>
            relative.Replace('\\', '/');

        private static void AppendFile(IncrementalHash hasher, string relativePath, byte[] bytes)
        {
            AppendText(hasher, "file\n");
            AppendText(hasher, relativePath);
            AppendText(hasher, "\n");
            AppendText(hasher, bytes.Length.ToString(CultureInfo.InvariantCulture));
            AppendText(hasher, "\n");
            hasher.AppendData(bytes);
        }

        private static void AppendText(IncrementalHash hasher, string value)
        {
            int byteCount = Utf8.GetByteCount(value);
            Span<byte> buffer = byteCount <= 512
                ? stackalloc byte[byteCount]
                : new byte[byteCount];
            int written = Utf8.GetBytes(value, buffer);
            hasher.AppendData(buffer[..written]);
        }
    }
}
