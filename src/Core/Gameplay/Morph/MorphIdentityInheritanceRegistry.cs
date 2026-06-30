using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Morph
{
    public sealed class MorphIdentityInheritanceFlags
    {
        public bool CopyPlayerOwner { get; set; }
        public bool CopyTeam { get; set; }
    }

    public static class MorphIdentityInheritanceRegistry
    {
        private static readonly Dictionary<string, Action<MorphIdentityInheritanceFlags>> Mutators =
            new(StringComparer.Ordinal)
            {
                ["PlayerOwner"] = flags => flags.CopyPlayerOwner = true,
                ["Team"] = flags => flags.CopyTeam = true,
                ["TeamIdentity"] = flags => flags.CopyTeam = true,
            };

        public static MorphIdentityInheritanceFlags Compile(IReadOnlyList<string> identities, string ownerId, string relativePath)
        {
            if (identities == null || identities.Count == 0)
            {
                return new MorphIdentityInheritanceFlags();
            }

            var flags = new MorphIdentityInheritanceFlags();
            for (int i = 0; i < identities.Count; i++)
            {
                string identity = RequireString(identities[i], ownerId, relativePath, "inherit.identity");
                if (!Mutators.TryGetValue(identity, out Action<MorphIdentityInheritanceFlags>? mutate))
                {
                    throw new InvalidOperationException(
                        $"'{ownerId}' in {relativePath}: unsupported inherit.identity '{identity}'. Supported: {string.Join(", ", Mutators.Keys)}.");
                }

                mutate(flags);
            }

            return flags;
        }

        private static string RequireString(string? value, string ownerId, string relativePath, string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"'{ownerId}' in {relativePath}: {fieldPath} is required.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"'{ownerId}' in {relativePath}: {fieldPath} must not include leading or trailing whitespace.");
            }

            return value;
        }
    }
}
