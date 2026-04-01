using System;
using Ludots.Core.Presentation.Hud;

namespace EntityInfoPanelsMod.Insight;

public sealed class EntityInsightTextResolver
{
    private readonly PresentationTextCatalog _catalog;
    private readonly PresentationTextLocaleSelection _localeSelection;

    public EntityInsightTextResolver(
        PresentationTextCatalog catalog,
        PresentationTextLocaleSelection localeSelection)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _localeSelection = localeSelection ?? throw new ArgumentNullException(nameof(localeSelection));
    }

    public int ActiveLocaleId => _localeSelection.ActiveLocaleId;

    public string ResolveRequiredTokenId(int tokenId)
    {
        if (tokenId <= 0)
        {
            throw new InvalidOperationException("Entity insight text token id must be positive.");
        }

        if (!PresentationTextFormatter.TryFormat(
                _catalog,
                _localeSelection.ActiveLocaleId,
                PresentationTextPacket.FromToken(tokenId),
                out string text))
        {
            throw new InvalidOperationException($"Entity insight text token id '{tokenId}' is not available for locale '{_localeSelection.ActiveLocaleKey}'.");
        }

        return text;
    }

    public string ResolveRequiredTokenKey(string tokenKey)
    {
        if (string.IsNullOrWhiteSpace(tokenKey))
        {
            throw new InvalidOperationException("Entity insight text token key must not be empty.");
        }

        int tokenId = _catalog.GetTokenId(tokenKey);
        if (tokenId <= 0)
        {
            throw new InvalidOperationException($"Entity insight text token '{tokenKey}' is not registered.");
        }

        return ResolveRequiredTokenId(tokenId);
    }
}
