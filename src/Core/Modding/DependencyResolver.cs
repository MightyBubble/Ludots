using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.Core.Modding
{
    public class DependencyResolver
    {
        public class ModNode
        {
            public ModManifest Manifest { get; set; }
            public int CreationIndex { get; set; } // To ensure stability for same-priority items
        }

        public List<ModManifest> Resolve(List<ModNode> mods)
        {
            var graph = new Dictionary<string, List<string>>();
            var indegree = new Dictionary<string, int>();
            var modMap = new Dictionary<string, ModNode>();
            var versionMap = new Dictionary<string, SemVersion>();

            // Initialize graph
            foreach (var mod in mods)
            {
                if (mod.Manifest == null)
                {
                    throw new Exception("Invalid mod node (missing Manifest).");
                }

                var name = mod.Manifest.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new Exception("Invalid mod manifest (missing name).");
                }

                if (!SemVersion.TryParse(mod.Manifest.Version, out var ver))
                {
                    throw new Exception($"Invalid version '{mod.Manifest.Version}' for mod '{name}'");
                }

                if (modMap.ContainsKey(name))
                {
                    throw new Exception($"Duplicate mod name detected: '{name}'");
                }

                modMap[name] = mod;
                versionMap[name] = ver;
                graph[name] = new List<string>();
                indegree[name] = 0;
            }

            // Build edges
            foreach (var mod in mods)
            {
                var modName = mod.Manifest.Name;
                foreach (var dep in mod.Manifest.Dependencies)
                {
                    var depName = dep.Key;
                    var rangeText = dep.Value;

                    if (!modMap.ContainsKey(depName))
                    {
                        throw new Exception($"Missing dependency: Mod '{modName}' requires '{depName}'");
                    }

                    if (!SemVersionRange.TryParse(rangeText, out var range))
                    {
                        throw new Exception($"Invalid dependency version range '{rangeText}' for '{modName}' -> '{depName}'");
                    }

                    var depVersion = versionMap[depName];
                    if (!range.Matches(depVersion))
                    {
                        throw new Exception($"Version mismatch: Mod '{modName}' requires '{depName}' {rangeText} but found {depVersion}");
                    }

                    graph[depName].Add(modName); // dep -> mod
                    indegree[modName]++;
                }
            }

            // Topological Sort with Priority Queue behavior
            var result = new List<ModManifest>();
            var queue = new List<ModNode>();

            // Initial candidates
            foreach (var mod in mods)
            {
                if (indegree[mod.Manifest.Name] == 0)
                {
                    queue.Add(mod);
                }
            }

            SortCandidates(queue);

            while (queue.Count > 0)
            {
                // Pop the best candidate
                var current = queue[0];
                queue.RemoveAt(0);
                result.Add(current.Manifest);

                // Update neighbors
                foreach (var neighborId in graph[current.Manifest.Name])
                {
                    indegree[neighborId]--;
                    if (indegree[neighborId] == 0)
                    {
                        queue.Add(modMap[neighborId]);
                    }
                }
                
                // Re-sort queue every time we add new candidates to maintain Priority > CreationIndex order
                // Optimization: We only need to sort if we added something, but for safety/simplicity we sort always
                SortCandidates(queue);
            }

            if (result.Count != mods.Count)
            {
                throw new Exception("Circular dependency detected!");
            }

            return result;
        }

        private void SortCandidates(List<ModNode> candidates)
        {
            // Sort by Priority Descending, then CreationIndex Ascending
            candidates.Sort((a, b) =>
            {
                int pComparison = b.Manifest.Priority.CompareTo(a.Manifest.Priority);
                if (pComparison != 0) return pComparison;
                return a.CreationIndex.CompareTo(b.CreationIndex);
            });
        }

        internal readonly struct SemVersion : IComparable<SemVersion>
        {
            public readonly int Major;
            public readonly int Minor;
            public readonly int Patch;

            public SemVersion(int major, int minor, int patch)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
            }

            public int CompareTo(SemVersion other)
            {
                var c = Major.CompareTo(other.Major);
                if (c != 0) return c;
                c = Minor.CompareTo(other.Minor);
                if (c != 0) return c;
                return Patch.CompareTo(other.Patch);
            }

            public override string ToString() => $"{Major}.{Minor}.{Patch}";

            public static bool TryParse(string text, out SemVersion version)
            {
                version = default;
                if (string.IsNullOrWhiteSpace(text)) return false;
                var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 3) return false;
                if (!int.TryParse(parts[0], out var major)) return false;
                if (!int.TryParse(parts[1], out var minor)) return false;
                if (!int.TryParse(parts[2], out var patch)) return false;
                if (major < 0 || minor < 0 || patch < 0) return false;
                version = new SemVersion(major, minor, patch);
                return true;
            }
        }

        internal readonly struct SemVersionRange
        {
            private readonly List<Comparator> _comparators;

            private SemVersionRange(List<Comparator> comparators)
            {
                _comparators = comparators;
            }

            public bool Matches(SemVersion v)
            {
                if (_comparators == null || _comparators.Count == 0) return true;
                foreach (var c in _comparators)
                {
                    if (!c.Matches(v)) return false;
                }
                return true;
            }

            public static bool TryParse(string text, out SemVersionRange range)
            {
                range = default;
                if (string.IsNullOrWhiteSpace(text) || text.Trim() == "*")
                {
                    range = new SemVersionRange(new List<Comparator>());
                    return true;
                }

                var comparators = new List<Comparator>();
                var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var token in tokens)
                {
                    if (token.StartsWith("^", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var baseVer)) return false;
                        comparators.Add(new Comparator(CompareOp.Gte, baseVer));
                        comparators.Add(new Comparator(CompareOp.Lt, new SemVersion(baseVer.Major + 1, 0, 0)));
                        continue;
                    }

                    if (token.StartsWith("~", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var baseVer)) return false;
                        comparators.Add(new Comparator(CompareOp.Gte, baseVer));
                        comparators.Add(new Comparator(CompareOp.Lt, new SemVersion(baseVer.Major, baseVer.Minor + 1, 0)));
                        continue;
                    }

                    if (token.StartsWith(">=", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[2..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Gte, ver));
                        continue;
                    }
                    if (token.StartsWith("<=", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[2..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Lte, ver));
                        continue;
                    }
                    if (token.StartsWith(">", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Gt, ver));
                        continue;
                    }
                    if (token.StartsWith("<", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Lt, ver));
                        continue;
                    }
                    if (token.StartsWith("=", StringComparison.Ordinal))
                    {
                        if (!SemVersion.TryParse(token[1..], out var ver)) return false;
                        comparators.Add(new Comparator(CompareOp.Eq, ver));
                        continue;
                    }

                    if (!SemVersion.TryParse(token, out var exact)) return false;
                    comparators.Add(new Comparator(CompareOp.Eq, exact));
                }

                range = new SemVersionRange(comparators);
                return true;
            }

            private enum CompareOp
            {
                Eq,
                Gt,
                Gte,
                Lt,
                Lte
            }

            private readonly struct Comparator
            {
                private readonly CompareOp _op;
                private readonly SemVersion _value;

                public Comparator(CompareOp op, SemVersion value)
                {
                    _op = op;
                    _value = value;
                }

                public bool Matches(SemVersion v)
                {
                    var c = v.CompareTo(_value);
                    return _op switch
                    {
                        CompareOp.Eq => c == 0,
                        CompareOp.Gt => c > 0,
                        CompareOp.Gte => c >= 0,
                        CompareOp.Lt => c < 0,
                        CompareOp.Lte => c <= 0,
                        _ => false
                    };
                }
            }
        }
    }
}
