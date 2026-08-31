using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationTextCatalog
    {
        public static readonly PresentationTextCatalog Empty = new PresentationTextCatalog(
            new StringIntRegistry(capacity: 1, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
            new PresentationTextTokenDefinition[1],
            new StringIntRegistry(capacity: 1, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
            new PresentationTextLocaleTable[1],
            defaultLocaleId: 0,
            stringPool: new PresentationTextStringPool());

        private readonly StringIntRegistry _tokenIds;
        private readonly PresentationTextTokenDefinition[] _tokens;
        private readonly StringIntRegistry _localeIds;
        private readonly PresentationTextLocaleTable[] _locales;

        public PresentationTextCatalog(
            StringIntRegistry tokenIds,
            PresentationTextTokenDefinition[] tokens,
            StringIntRegistry localeIds,
            PresentationTextLocaleTable[] locales,
            int defaultLocaleId,
            PresentationTextStringPool? stringPool = null)
        {
            _tokenIds = tokenIds ?? throw new ArgumentNullException(nameof(tokenIds));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _localeIds = localeIds ?? throw new ArgumentNullException(nameof(localeIds));
            _locales = locales ?? throw new ArgumentNullException(nameof(locales));
            DefaultLocaleId = defaultLocaleId;
            StringPool = stringPool ?? new PresentationTextStringPool();
        }

        public int TokenCount => _tokenIds.Count;

        public int LocaleCount => _localeIds.Count;

        public int DefaultLocaleId { get; }

        public PresentationTextStringPool StringPool { get; }

        public int GetTokenId(string key) => _tokenIds.GetId(key);

        public int GetLocaleId(string localeKey) => _localeIds.GetId(localeKey);

        public string GetTokenKey(int tokenId) => _tokenIds.GetName(tokenId);

        public string GetLocaleKey(int localeId) => _localeIds.GetName(localeId);

        public bool TryGetTokenDefinition(int tokenId, out PresentationTextTokenDefinition definition)
        {
            if ((uint)tokenId < (uint)_tokens.Length && _tokens[tokenId] != null)
            {
                definition = _tokens[tokenId];
                return true;
            }

            definition = null!;
            return false;
        }

        public bool TryGetLocaleTable(int localeId, out PresentationTextLocaleTable localeTable)
        {
            if ((uint)localeId < (uint)_locales.Length && _locales[localeId] != null)
            {
                localeTable = _locales[localeId];
                return true;
            }

            localeTable = null!;
            return false;
        }

        public bool TryGetTemplate(int localeId, int tokenId, out PresentationTextTemplate template)
        {
            if (TryGetLocaleTable(localeId, out var localeTable))
            {
                return localeTable.TryGetTemplate(tokenId, out template);
            }

            template = null!;
            return false;
        }
    }
}
