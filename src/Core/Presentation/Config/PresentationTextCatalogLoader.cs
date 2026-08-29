using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationTextCatalogLoader
    {
        private const string TokenPath = "Presentation/text_tokens.json";
        private const string LocalePath = "Presentation/text_locales.json";

        private readonly ConfigPipeline _configs;

        public PresentationTextCatalogLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public PresentationTextCatalog Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var tokenEntry = ConfigPipeline.RequireEntry(catalog, TokenPath, ConfigMergePolicy.ArrayById, "id");
            ValidateTokenIdsAreUnique(in tokenEntry);
            var tokenNodes = _configs.MergeArrayByIdFromCatalog(in tokenEntry, report);

            var localeEntry = ConfigPipeline.RequireEntry(catalog, LocalePath, ConfigMergePolicy.DeepObject);
            JsonObject localeRoot = _configs.MergeDeepObjectFromCatalog(in localeEntry, report);

            if (tokenNodes.Count == 0 && (localeRoot == null || localeRoot.Count == 0))
            {
                return PresentationTextCatalog.Empty;
            }

            var orderedTokenNodes = new List<(string Key, JsonNode Node)>(tokenNodes.Count);
            for (int i = 0; i < tokenNodes.Count; i++)
            {
                string key = tokenNodes[i].Node["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("Presentation text token is missing required 'id'.");
                }

                orderedTokenNodes.Add((key, tokenNodes[i].Node));
            }

            orderedTokenNodes.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

            var tokenIds = new StringIntRegistry(
                capacity: Math.Max(16, orderedTokenNodes.Count + 1),
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
            var tokenDefinitions = new PresentationTextTokenDefinition[Math.Max(2, orderedTokenNodes.Count + 1)];
            for (int i = 0; i < orderedTokenNodes.Count; i++)
            {
                var (key, node) = orderedTokenNodes[i];

                byte argCount = ParseArgCount(node, key);
                int tokenId = tokenIds.Register(key);
                EnsureCapacity(ref tokenDefinitions, tokenId);
                tokenDefinitions[tokenId] = new PresentationTextTokenDefinition
                {
                    TokenId = tokenId,
                    Key = key,
                    ArgCount = argCount,
                };
            }
            tokenIds.Freeze();

            JsonObject localesNode = localeRoot?["locales"] as JsonObject;
            if (tokenIds.Count > 0)
            {
                if (localeRoot == null)
                {
                    throw new InvalidOperationException("Presentation text locales are required when text tokens are registered.");
                }

                string defaultLocale = localeRoot["defaultLocale"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(defaultLocale))
                {
                    throw new InvalidOperationException("Presentation text locales must define a non-empty 'defaultLocale'.");
                }

                if (localesNode == null || localesNode.Count == 0)
                {
                    throw new InvalidOperationException("Presentation text locales must define at least one locale table.");
                }

                return BuildCatalog(tokenIds, tokenDefinitions, localesNode, defaultLocale);
            }

            if (localeRoot == null || localesNode == null || localesNode.Count == 0)
            {
                return PresentationTextCatalog.Empty;
            }

            throw new InvalidOperationException("Presentation text locales define entries, but no text tokens were registered.");
        }

        private static PresentationTextCatalog BuildCatalog(
            StringIntRegistry tokenIds,
            PresentationTextTokenDefinition[] tokenDefinitions,
            JsonObject localesNode,
            string defaultLocale)
        {
            var orderedLocales = new List<KeyValuePair<string, JsonNode?>>(localesNode.Count);
            foreach (KeyValuePair<string, JsonNode?> localeEntry in localesNode)
            {
                orderedLocales.Add(localeEntry);
            }
            orderedLocales.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

            var localeIds = new StringIntRegistry(
                capacity: Math.Max(16, orderedLocales.Count + 1),
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
            var localeTables = new PresentationTextLocaleTable[Math.Max(2, localesNode.Count + 1)];
            int tokenCapacity = tokenDefinitions.Length;

            for (int localeIndex = 0; localeIndex < orderedLocales.Count; localeIndex++)
            {
                KeyValuePair<string, JsonNode?> localeEntry = orderedLocales[localeIndex];
                string localeKey = localeEntry.Key;
                if (string.IsNullOrWhiteSpace(localeKey))
                {
                    throw new InvalidOperationException("Presentation text locale key must not be empty.");
                }

                if (localeEntry.Value is not JsonObject tokenMap)
                {
                    throw new InvalidOperationException($"Presentation text locale '{localeKey}' must be a JSON object.");
                }

                int localeId = localeIds.Register(localeKey);
                EnsureCapacity(ref localeTables, localeId);
                var templates = new PresentationTextTemplate[tokenCapacity];

                foreach (KeyValuePair<string, JsonNode?> tokenEntry in tokenMap)
                {
                    string tokenKey = tokenEntry.Key;
                    int tokenId = tokenIds.GetId(tokenKey);
                    if (tokenId <= 0)
                    {
                        throw new InvalidOperationException($"Presentation text locale '{localeKey}' references unknown token '{tokenKey}'.");
                    }

                    string templateValue = tokenEntry.Value?.GetValue<string>() ?? string.Empty;
                    if (!TryGetTokenDefinition(tokenDefinitions, tokenId, out var definition))
                    {
                        throw new InvalidOperationException($"Presentation text token id '{tokenId}' is not defined.");
                    }

                    templates[tokenId] = ParseTemplate(localeKey, definition, templateValue);
                }

                for (int tokenId = 1; tokenId < tokenDefinitions.Length; tokenId++)
                {
                    if (tokenDefinitions[tokenId] == null)
                    {
                        continue;
                    }

                    if (templates[tokenId] == null)
                    {
                        throw new InvalidOperationException(
                            $"Presentation text locale '{localeKey}' is missing token '{tokenDefinitions[tokenId].Key}'.");
                    }
                }

                localeTables[localeId] = new PresentationTextLocaleTable(localeId, localeKey, templates);
            }

            localeIds.Freeze();
            int defaultLocaleId = localeIds.GetId(defaultLocale);
            if (defaultLocaleId <= 0)
            {
                throw new InvalidOperationException($"Presentation default locale '{defaultLocale}' is not defined.");
            }

            return new PresentationTextCatalog(
                tokenIds,
                tokenDefinitions,
                localeIds,
                localeTables,
                defaultLocaleId,
                stringPool: new PresentationTextStringPool());
        }

        private static byte ParseArgCount(JsonNode node, string tokenKey)
        {
            int argCount = node["argCount"]?.GetValue<int>() ?? 0;
            if (argCount < 0 || argCount > PresentationTextPacket.MaxArgs)
            {
                throw new InvalidOperationException(
                    $"Presentation text token '{tokenKey}' argCount must be between 0 and {PresentationTextPacket.MaxArgs}.");
            }

            return (byte)argCount;
        }

        private static PresentationTextTemplate ParseTemplate(
            string localeKey,
            PresentationTextTokenDefinition definition,
            string source)
        {
            var parts = new List<PresentationTextTemplatePart>(4);
            var literal = new StringBuilder();
            var seenArgs = definition.ArgCount > 0 ? new bool[definition.ArgCount] : Array.Empty<bool>();
            PresentationTextStyleOverride activeStyle = PresentationTextStyleOverride.None;
            string? openTagName = null;

            for (int i = 0; i < source.Length; i++)
            {
                char ch = source[i];
                if (ch == '{')
                {
                    if (i + 1 < source.Length && source[i + 1] == '{')
                    {
                        literal.Append('{');
                        i++;
                        continue;
                    }

                    FlushLiteral(parts, literal, activeStyle);
                    int closeIndex = source.IndexOf('}', i + 1);
                    if (closeIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Presentation text locale '{localeKey}' token '{definition.Key}' contains an unterminated placeholder.");
                    }

                    string placeholder = source.Substring(i + 1, closeIndex - i - 1);
                    if (placeholder.IndexOf(':') >= 0)
                    {
                        throw new InvalidOperationException(
                            $"Presentation text locale '{localeKey}' token '{definition.Key}' uses unsupported placeholder format '{{{placeholder}}}'.");
                    }

                    if (!int.TryParse(placeholder, out int argIndex))
                    {
                        throw new InvalidOperationException(
                            $"Presentation text locale '{localeKey}' token '{definition.Key}' contains invalid placeholder '{{{placeholder}}}'.");
                    }

                    if ((uint)argIndex >= definition.ArgCount)
                    {
                        throw new InvalidOperationException(
                            $"Presentation text locale '{localeKey}' token '{definition.Key}' references arg {argIndex}, but argCount={definition.ArgCount}.");
                    }

                    seenArgs[argIndex] = true;
                    parts.Add(new PresentationTextTemplatePart(
                        PresentationTextTemplatePartKind.Argument,
                        string.Empty,
                        argIndex,
                        activeStyle));
                    i = closeIndex;
                    continue;
                }

                if (ch == '}')
                {
                    if (i + 1 < source.Length && source[i + 1] == '}')
                    {
                        literal.Append('}');
                        i++;
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Presentation text locale '{localeKey}' token '{definition.Key}' contains an unmatched '}}'.");
                }

                if (ch == '<')
                {
                    if (!TryParseMarkupTag(source, i, out MarkupTag tag, out int tagEnd))
                    {
                        throw new InvalidOperationException(
                            $"Presentation text locale '{localeKey}' token '{definition.Key}' contains invalid markup near index {i}.");
                    }

                    if (tag.IsClose)
                    {
                        if (openTagName == null ||
                            !string.Equals(openTagName, tag.Name, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Presentation text locale '{localeKey}' token '{definition.Key}' has mismatched close tag '</{tag.Name}>'.");
                        }

                        FlushLiteral(parts, literal, activeStyle);
                        openTagName = null;
                        activeStyle = PresentationTextStyleOverride.None;
                        i = tagEnd;
                        continue;
                    }

                    if (openTagName != null)
                    {
                        throw new InvalidOperationException(
                            $"Presentation text locale '{localeKey}' token '{definition.Key}' does not allow nested markup (open '<{openTagName}>', saw '<{tag.Name}>').");
                    }

                    FlushLiteral(parts, literal, PresentationTextStyleOverride.None);
                    openTagName = tag.Name;
                    activeStyle = tag.Style;
                    i = tagEnd;
                    continue;
                }

                literal.Append(ch);
            }

            if (openTagName != null)
            {
                throw new InvalidOperationException(
                    $"Presentation text locale '{localeKey}' token '{definition.Key}' has unclosed markup tag '<{openTagName}>'.");
            }

            FlushLiteral(parts, literal, PresentationTextStyleOverride.None);
            for (int argIndex = 0; argIndex < seenArgs.Length; argIndex++)
            {
                if (!seenArgs[argIndex])
                {
                    throw new InvalidOperationException(
                        $"Presentation text locale '{localeKey}' token '{definition.Key}' does not reference placeholder {{{argIndex}}}.");
                }
            }

            return new PresentationTextTemplate(source, parts.ToArray());
        }

        private readonly struct MarkupTag
        {
            public MarkupTag(string name, bool isClose, PresentationTextStyleOverride style)
            {
                Name = name;
                IsClose = isClose;
                Style = style;
            }

            public string Name { get; }
            public bool IsClose { get; }
            public PresentationTextStyleOverride Style { get; }
        }

        private static bool TryParseMarkupTag(
            string source,
            int start,
            out MarkupTag tag,
            out int endInclusive)
        {
            tag = default;
            endInclusive = start;
            if (start >= source.Length || source[start] != '<')
            {
                return false;
            }

            int cursor = start + 1;
            bool isClose = false;
            if (cursor < source.Length && source[cursor] == '/')
            {
                isClose = true;
                cursor++;
            }

            int nameStart = cursor;
            while (cursor < source.Length && IsTagNameChar(source[cursor]))
            {
                cursor++;
            }

            if (cursor == nameStart)
            {
                return false;
            }

            string name = source.Substring(nameStart, cursor - nameStart);
            while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            {
                cursor++;
            }

            PresentationTextStyleOverride style = PresentationTextStyleOverride.None;
            if (!isClose)
            {
                if (string.Equals(name, "b", StringComparison.Ordinal))
                {
                    style = PresentationTextStyleOverride.CreateBold();
                }
                else if (string.Equals(name, "i", StringComparison.Ordinal))
                {
                    style = PresentationTextStyleOverride.CreateItalic();
                }
                else if (string.Equals(name, "color", StringComparison.Ordinal))
                {
                    if (cursor >= source.Length || source[cursor] != '=')
                    {
                        return false;
                    }

                    cursor++;
                    if (cursor >= source.Length || source[cursor] != '#')
                    {
                        return false;
                    }

                    int hexStart = cursor;
                    cursor++;
                    while (cursor < source.Length && IsHexChar(source[cursor]))
                    {
                        cursor++;
                    }

                    string hex = source.Substring(hexStart, cursor - hexStart);
                    if (hex.Length != 9 ||
                        !TryParseAaRrGgBb(hex, out byte a, out byte r, out byte g, out byte b))
                    {
                        return false;
                    }

                    style = PresentationTextStyleOverride.CreateColor(a, r, g, b);
                    while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
                    {
                        cursor++;
                    }
                }
                else
                {
                    return false;
                }
            }
            else if (!(string.Equals(name, "b", StringComparison.Ordinal) ||
                       string.Equals(name, "i", StringComparison.Ordinal) ||
                       string.Equals(name, "color", StringComparison.Ordinal)))
            {
                return false;
            }

            if (cursor >= source.Length || source[cursor] != '>')
            {
                return false;
            }

            tag = new MarkupTag(name, isClose, style);
            endInclusive = cursor;
            return true;
        }

        private static bool IsTagNameChar(char ch) =>
            (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');

        private static bool IsHexChar(char ch) =>
            (ch >= '0' && ch <= '9') ||
            (ch >= 'a' && ch <= 'f') ||
            (ch >= 'A' && ch <= 'F');

        private static bool TryParseAaRrGgBb(string hexWithHash, out byte a, out byte r, out byte g, out byte b)
        {
            a = r = g = b = 0;
            if (hexWithHash.Length != 9 || hexWithHash[0] != '#')
            {
                return false;
            }

            return TryParseHexByte(hexWithHash.AsSpan(1, 2), out a) &&
                   TryParseHexByte(hexWithHash.AsSpan(3, 2), out r) &&
                   TryParseHexByte(hexWithHash.AsSpan(5, 2), out g) &&
                   TryParseHexByte(hexWithHash.AsSpan(7, 2), out b);
        }

        private static bool TryParseHexByte(ReadOnlySpan<char> span, out byte value)
        {
            value = 0;
            if (span.Length != 2)
            {
                return false;
            }

            if (!TryHexNibble(span[0], out int hi) || !TryHexNibble(span[1], out int lo))
            {
                return false;
            }

            value = (byte)((hi << 4) | lo);
            return true;
        }

        private static bool TryHexNibble(char ch, out int value)
        {
            if (ch >= '0' && ch <= '9')
            {
                value = ch - '0';
                return true;
            }

            if (ch >= 'a' && ch <= 'f')
            {
                value = 10 + (ch - 'a');
                return true;
            }

            if (ch >= 'A' && ch <= 'F')
            {
                value = 10 + (ch - 'A');
                return true;
            }

            value = 0;
            return false;
        }

        private void ValidateTokenIdsAreUnique(in ConfigCatalogEntry entry)
        {
            List<ConfigFragment> fragments = _configs.CollectFragmentsWithSources(in entry);
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                if (fragments[fragmentIndex].Node is not JsonArray array)
                {
                    continue;
                }

                for (int index = 0; index < array.Count; index++)
                {
                    if (array[index] is not JsonObject obj)
                    {
                        continue;
                    }

                    string tokenId = obj["id"]?.GetValue<string>() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(tokenId))
                    {
                        continue;
                    }

                    if (seen.TryGetValue(tokenId, out string? firstSource))
                    {
                        throw new InvalidOperationException(
                            $"Presentation text token catalog '{entry.RelativePath}' defines duplicate id '{tokenId}' in '{firstSource}' and '{fragments[fragmentIndex].SourceUri}'.");
                    }

                    seen[tokenId] = fragments[fragmentIndex].SourceUri;
                }
            }
        }

        private static void FlushLiteral(
            List<PresentationTextTemplatePart> parts,
            StringBuilder literal,
            PresentationTextStyleOverride style)
        {
            if (literal.Length == 0)
            {
                return;
            }

            PresentationTextTemplatePartKind kind = style.IsEmpty
                ? PresentationTextTemplatePartKind.Literal
                : PresentationTextTemplatePartKind.StyledLiteral;
            parts.Add(new PresentationTextTemplatePart(kind, literal.ToString(), -1, style));
            literal.Clear();
        }

        private static void FlushLiteral(List<PresentationTextTemplatePart> parts, StringBuilder literal)
        {
            FlushLiteral(parts, literal, PresentationTextStyleOverride.None);
        }

        private static bool TryGetTokenDefinition(
            PresentationTextTokenDefinition[] tokenDefinitions,
            int tokenId,
            out PresentationTextTokenDefinition definition)
        {
            if ((uint)tokenId < (uint)tokenDefinitions.Length && tokenDefinitions[tokenId] != null)
            {
                definition = tokenDefinitions[tokenId];
                return true;
            }

            definition = null!;
            return false;
        }

        private static void EnsureCapacity<T>(ref T[] array, int index)
        {
            if (index < array.Length)
            {
                return;
            }

            int newLength = Math.Max(array.Length * 2, index + 1);
            Array.Resize(ref array, newLength);
        }
    }
}
