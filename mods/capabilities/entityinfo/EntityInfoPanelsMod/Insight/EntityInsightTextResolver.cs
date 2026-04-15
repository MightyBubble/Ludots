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

        return FormatRequiredTokenId(tokenId);
    }

    public string FormatRequiredTokenId(int tokenId, params PresentationTextArg[] args)
    {
        if (tokenId <= 0)
        {
            throw new InvalidOperationException("Entity insight text token id must be positive.");
        }

        var packet = PresentationTextPacket.FromToken(tokenId);
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                packet.SetArg(i, args[i]);
            }
        }

        if (!PresentationTextFormatter.TryFormat(
                _catalog,
                _localeSelection.ActiveLocaleId,
                in packet,
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

    public int GetRequiredTokenId(string tokenKey)
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

        return tokenId;
    }

    public string FormatRequiredTokenKey(string tokenKey, params PresentationTextArg[] args)
    {
        return FormatRequiredTokenId(GetRequiredTokenId(tokenKey), args);
    }
}
