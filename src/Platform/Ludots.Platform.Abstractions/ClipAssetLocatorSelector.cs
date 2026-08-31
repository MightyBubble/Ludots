using System;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Parses the optional "#kind:value" fragment in an animation locator.
    /// Named and numeric animation selectors are validated while loading config;
    /// the backend resolves the selected clip from its loaded asset.
    /// </summary>
    public readonly struct ClipAssetLocatorSelector : IEquatable<ClipAssetLocatorSelector>
    {
        public const string AnimKind = "anim";
        public const string GraphKind = "graph";
        public const string PoseKind = "pose";

        /// <summary>Asset path before '#', or the complete reference without a fragment.</summary>
        public readonly string AssetPath;

        /// <summary>Fragment kind (anim, graph, or pose), or empty when absent.</summary>
        public readonly string Kind;

        /// <summary>Selector text: a name or a decimal zero-based index.</summary>
        public readonly string Selector;

        /// <summary>Zero-based index, or -1 for a named or path-only selector.</summary>
        public readonly int Index;

        public readonly bool HasFragment;
        public readonly bool IsIndex;
        public readonly bool IsName;

        private ClipAssetLocatorSelector(string assetPath, string kind, string selector, int index, bool hasFragment, bool isIndex)
        {
            AssetPath = assetPath;
            Kind = kind;
            Selector = selector;
            Index = index;
            HasFragment = hasFragment;
            IsIndex = isIndex;
            IsName = hasFragment && !isIndex;
        }

        public static bool TryParse(ReadOnlySpan<char> assetRef, out ClipAssetLocatorSelector selector)
        {
            selector = default;
            if (assetRef.IsEmpty)
            {
                return false;
            }

            int hashIndex = assetRef.IndexOf('#');
            if (hashIndex < 0)
            {
                string pathOnly = assetRef.Trim().ToString();
                if (pathOnly.Length == 0)
                {
                    return false;
                }

                selector = new ClipAssetLocatorSelector(pathOnly, string.Empty, string.Empty, -1, hasFragment: false, isIndex: false);
                return true;
            }

            if (assetRef.Slice(hashIndex + 1).IndexOf('#') >= 0)
            {
                return false;
            }

            ReadOnlySpan<char> path = assetRef.Slice(0, hashIndex).Trim();
            ReadOnlySpan<char> fragment = assetRef.Slice(hashIndex + 1).Trim();
            if (path.IsEmpty || fragment.IsEmpty)
            {
                return false;
            }

            int colonIndex = fragment.IndexOf(':');
            if (colonIndex <= 0 || colonIndex == fragment.Length - 1)
            {
                return false;
            }

            ReadOnlySpan<char> kind = fragment.Slice(0, colonIndex);
            ReadOnlySpan<char> rawSelector = fragment.Slice(colonIndex + 1).Trim();
            if (rawSelector.IsEmpty)
            {
                return false;
            }

            if (!IsKnownKind(kind))
            {
                return false;
            }

            bool isIndex;
            int index;
            if (IsAllAsciiDigits(rawSelector))
            {
                // 纯数字选择器必须能解析为合法索引：溢出（如 int.MaxValue+1）是错误，不允许降级为名字。
                if (!TryParseIndex(rawSelector, out index))
                {
                    return false;
                }

                isIndex = true;
            }
            else
            {
                if (!IsValidName(rawSelector))
                {
                    return false;
                }

                index = -1;
                isIndex = false;
            }

            selector = new ClipAssetLocatorSelector(
                path.ToString(),
                kind.ToString(),
                rawSelector.ToString(),
                index,
                hasFragment: true,
                isIndex: isIndex);
            return true;
        }

        public static ClipAssetLocatorSelector Parse(string assetRef)
        {
            if (assetRef == null)
            {
                throw new ArgumentNullException(nameof(assetRef));
            }

            if (!TryParse(assetRef.AsSpan(), out ClipAssetLocatorSelector selector))
            {
                throw new InvalidOperationException(
                    $"Clip asset locator '{assetRef}' is malformed. Expected 'assetPath', 'assetPath#kind:name' with kind in {{anim, graph, pose}}, " +
                    "or 'assetPath#anim:index' with a zero-based numeric index.");
            }

            return selector;
        }

        private static bool IsKnownKind(ReadOnlySpan<char> kind)
        {
            return kind.SequenceEqual(AnimKind.AsSpan()) ||
                kind.SequenceEqual(GraphKind.AsSpan()) ||
                kind.SequenceEqual(PoseKind.AsSpan());
        }

        private static bool IsAllAsciiDigits(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty)
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] is < '0' or > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseIndex(ReadOnlySpan<char> text, out int index)
        {
            index = -1;
            if (text.IsEmpty)
            {
                return false;
            }

            int value = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c is < '0' or > '9')
                {
                    return false;
                }

                int digit = c - '0';
                if (value > (int.MaxValue - digit) / 10)
                {
                    return false;
                }

                value = (value * 10) + digit;
            }

            index = value;
            return true;
        }

        private static bool IsValidName(ReadOnlySpan<char> name)
        {
            if (name.IsEmpty)
            {
                return false;
            }

            // 拒绝看起来像带符号数字的名字（-1 / +3），避免与索引形态混淆。
            char first = name[0];
            if (first is '-' or '+')
            {
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '\0' || c == '#' || c == ':')
                {
                    return false;
                }
            }

            return true;
        }

        public readonly bool Equals(ClipAssetLocatorSelector other)
        {
            return string.Equals(AssetPath, other.AssetPath, StringComparison.Ordinal) &&
                string.Equals(Kind, other.Kind, StringComparison.Ordinal) &&
                string.Equals(Selector, other.Selector, StringComparison.Ordinal) &&
                Index == other.Index &&
                HasFragment == other.HasFragment &&
                IsIndex == other.IsIndex;
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is ClipAssetLocatorSelector other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(AssetPath, StringComparer.Ordinal);
            hash.Add(Kind, StringComparer.Ordinal);
            hash.Add(Selector, StringComparer.Ordinal);
            hash.Add(Index);
            hash.Add(HasFragment);
            hash.Add(IsIndex);
            return hash.ToHashCode();
        }

        public override readonly string ToString()
        {
            return HasFragment ? $"{AssetPath}#{Kind}:{Selector}" : AssetPath;
        }
    }
}
