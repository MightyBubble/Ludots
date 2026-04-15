using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Hud;

namespace EntityInfoPanelsMod.Insight;

public enum EntityInsightStatSourceKind : byte
{
    Attribute = 0,
    Constant = 1,
}

public enum EntityInsightValueDisplayMode : byte
{
    Current = 0,
    CurrentOverBase = 1,
    Constant = 2,
}

public enum EntityInsightEntityRelationKind : byte
{
    SelfTeamRelationship = 0,
}

[Flags]
public enum EntityInsightActionRuntimeFlags : byte
{
    None = 0,
    Present = 1 << 0,
    Blocked = 1 << 1,
    Active = 1 << 2,
}

public sealed class EntityInsightBadgeProfile
{
    public required string Glyph { get; init; }
    public required int TextTokenId { get; init; }
}

public sealed class EntityInsightStatProfile
{
    public required string SemanticKey { get; init; }
    public required string Glyph { get; init; }
    public required int AttributeId { get; init; }
    public required EntityInsightStatSourceKind SourceKind { get; init; }
    public required EntityInsightValueDisplayMode DisplayMode { get; init; }
    public float ConstantValue { get; init; }
}

public sealed class EntityInsightSemanticFieldProfile
{
    public required string Glyph { get; init; }
    public required string MappingId { get; init; }
    public required EntityInsightEntityRelationKind EntityRelation { get; init; }

    public string GetValueKey(int runtimeValue)
    {
        if (MappingId == WellKnownPresentationSemanticMappingKeys.TeamRelationship)
        {
            return runtimeValue switch
            {
                (int)Ludots.Core.Gameplay.Teams.TeamRelationship.Friendly => WellKnownPresentationSemanticMappingKeys.TeamRelationshipFriendly,
                (int)Ludots.Core.Gameplay.Teams.TeamRelationship.Hostile => WellKnownPresentationSemanticMappingKeys.TeamRelationshipHostile,
                _ => WellKnownPresentationSemanticMappingKeys.TeamRelationshipNeutral,
            };
        }

        return runtimeValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class EntityInsightTipProfile
{
    public required string Glyph { get; init; }
    public required int TextTokenId { get; init; }
}

public sealed class EntityInsightActionProfile
{
    public required int AbilityId { get; init; }
    public required string Glyph { get; init; }
    public required int TitleTokenId { get; init; }
    public required int BodyTokenId { get; init; }
}

public sealed class EntityInsightProfile
{
    public required string Id { get; init; }
    public required int[] TemplateKeyIds { get; init; }
    public required string AccentColorHex { get; init; }
    public required string SurfaceColorHex { get; init; }
    public required string GenreGlyph { get; init; }
    public required int PortraitImageAssetId { get; init; }
    public required int GenreLabelTokenId { get; init; }
    public required int SubtitleTokenId { get; init; }
    public required int BodyTokenId { get; init; }
    public required EntityInsightBadgeProfile[] Badges { get; init; }
    public required EntityInsightStatProfile[] Stats { get; init; }
    public required EntityInsightSemanticFieldProfile[] SemanticFields { get; init; }
    public required EntityInsightTipProfile[] Tips { get; init; }
    public required EntityInsightActionProfile[] Actions { get; init; }
}

public sealed class EntityInsightProfileCatalog
{
    public static readonly EntityInsightProfileCatalog Empty = new(
        Array.Empty<EntityInsightProfile>(),
        new Dictionary<int, int>());

    private readonly EntityInsightProfile[] _profiles;
    private readonly Dictionary<int, int> _profileIndexByTemplateKey;

    public EntityInsightProfileCatalog(
        EntityInsightProfile[] profiles,
        Dictionary<int, int> profileIndexByTemplateKey)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _profileIndexByTemplateKey = profileIndexByTemplateKey ?? throw new ArgumentNullException(nameof(profileIndexByTemplateKey));
    }

    public int Count => _profiles.Length;

    public bool TryGetProfileIndex(int templateKeyId, out int profileIndex)
    {
        return _profileIndexByTemplateKey.TryGetValue(templateKeyId, out profileIndex);
    }

    public bool TryGetProfileByTemplateKey(int templateKeyId, out EntityInsightProfile profile)
    {
        if (_profileIndexByTemplateKey.TryGetValue(templateKeyId, out int profileIndex) &&
            (uint)profileIndex < (uint)_profiles.Length)
        {
            profile = _profiles[profileIndex];
            return true;
        }

        profile = null!;
        return false;
    }

    public bool TryGetProfileByIndex(int profileIndex, out EntityInsightProfile profile)
    {
        if ((uint)profileIndex < (uint)_profiles.Length)
        {
            profile = _profiles[profileIndex];
            return true;
        }

        profile = null!;
        return false;
    }
}
