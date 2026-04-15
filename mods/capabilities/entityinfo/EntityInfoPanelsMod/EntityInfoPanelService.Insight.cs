using System;
using Arch.Core;
using Arch.Core.Extensions;
using EntityInfoPanelsMod.Insight;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    private const string ActionStateReadyToken = "entityinfo.actionstate.ready";
    private const string ActionStateBlockedToken = "entityinfo.actionstate.blocked";
    private const string ActionStateActiveToken = "entityinfo.actionstate.active";
    private const string ActionStateUnavailableToken = "entityinfo.actionstate.unavailable";
    private const string EntityCollectionTitleToken = "entityinfo.collection.title";
    private const string EntityCollectionEmptyTitleToken = "entityinfo.collection.empty_title";
    private const string EntityCollectionWaitingBodyToken = "entityinfo.collection.waiting_body";
    private const string EntityCollectionNoCategoriesToken = "entityinfo.collection.no_categories";
    private const string EntityCollectionPrimaryToken = "entityinfo.collection.primary";
    private const string EntityCollectionEntitiesToken = "entityinfo.collection.entities";
    private const string EntityCollectionCategoriesToken = "entityinfo.collection.categories";
    private const string EntityCollectionRowsToken = "entityinfo.collection.rows";
    private const string EntityCollectionNoAttributesToken = "entityinfo.collection.no_attributes";
    private const string EntityCollectionMoreAttributesToken = "entityinfo.collection.more_attributes";

    public bool TryGetInsightProfile(int slot, out EntityInsightProfile profile)
    {
        int profileIndex = _insightProfileIndices[slot] - 1;
        if (profileIndex >= 0 && _insightCatalog.TryGetProfileByIndex(profileIndex, out profile))
        {
            return true;
        }

        profile = null!;
        return false;
    }

    public bool TryGetEntityInsightProfile(World world, Entity entity, out EntityInsightProfile profile)
    {
        profile = null!;
        if (entity == Entity.Null ||
            !world.IsAlive(entity) ||
            !world.TryGet(entity, out EntityTemplateKeyCm templateKey) ||
            !_insightCatalog.TryGetProfileByTemplateKey(templateKey.TemplateKeyId, out profile))
        {
            return false;
        }

        return true;
    }

    public string ResolveTextTokenKey(string tokenKey) => _insightTextResolver.ResolveRequiredTokenKey(tokenKey);
    public string ResolveTextTokenId(int tokenId) => _insightTextResolver.ResolveRequiredTokenId(tokenId);
    public string FormatTextTokenKey(string tokenKey, params PresentationTextArg[] args) => _insightTextResolver.FormatRequiredTokenKey(tokenKey, args);
    public string FormatTextTokenId(int tokenId, params PresentationTextArg[] args) => _insightTextResolver.FormatRequiredTokenId(tokenId, args);
    public string BuildInsightPortraitIconUri(EntityInsightProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.PortraitImageAssetId > 0 && _imageSourceResolver != null && _imageSourceResolver.TryResolveSource(profile.PortraitImageAssetId, out string source))
        {
            return source;
        }

        if (profile.PortraitImageAssetId > 0 &&
            _imageSourceResolver != null &&
            _imageSourceResolver.TryResolveGlyphFallback(profile.PortraitImageAssetId, out PresentationImageGlyphFallbackDefinition assetFallback))
        {
            return BuildInsightGlyphIconUri(
                assetFallback.Glyph,
                assetFallback.AccentColorHex,
                assetFallback.SurfaceColorHex,
                emphatic: true);
        }

        if (!string.IsNullOrWhiteSpace(profile.PortraitGlyph))
        {
            return BuildInsightGlyphIconUri(profile.PortraitGlyph, profile.AccentColorHex, profile.SurfaceColorHex, emphatic: true);
        }

        if (profile.PortraitImageAssetId > 0 && _imageSourceResolver == null)
        {
            throw new InvalidOperationException("Entity insight portrait resolution requires a configured presentation image source resolver.");
        }

        if (profile.PortraitImageAssetId > 0)
        {
            return _imageSourceResolver!.ResolveRequiredSource(profile.PortraitImageAssetId);
        }

        throw new InvalidOperationException($"Entity insight profile '{profile.Id}' must define either 'portraitImageAsset' or migration 'portraitGlyph'.");
    }

    public string BuildInsightGenreIconUri(EntityInsightProfile profile) => _insightIconFactory.Build(profile.GenreGlyph, profile.AccentColorHex, profile.SurfaceColorHex);
    public string BuildInsightGlyphIconUri(string glyph, string accentHex, string surfaceHex, bool emphatic = false) => _insightIconFactory.Build(glyph, accentHex, surfaceHex, emphatic);
    public string BuildInsightSummary(World world, Entity entity, EntityInsightProfile profile, int maxStats)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (entity == Entity.Null || !world.IsAlive(entity))
        {
            throw new InvalidOperationException("Entity insight summary requires a live entity.");
        }

        int statsToInclude = Math.Min(Math.Max(0, maxStats), profile.Stats.Length);
        int semanticFieldsToInclude = Math.Min(1, profile.SemanticFields.Length);
        int capacity = statsToInclude + semanticFieldsToInclude;
        if (capacity == 0)
        {
            return string.Empty;
        }

        var segments = new string[capacity];
        int segmentCount = 0;
        AttributeBuffer attributes = world.TryGet(entity, out AttributeBuffer runtimeAttributes) ? runtimeAttributes : default;
        for (int i = 0; i < statsToInclude; i++)
        {
            EntityInsightStatProfile stat = profile.Stats[i];
            float currentValue;
            float baseValue;
            if (stat.SourceKind == EntityInsightStatSourceKind.Attribute)
            {
                currentValue = attributes.GetCurrent(stat.AttributeId);
                baseValue = attributes.GetBase(stat.AttributeId);
            }
            else
            {
                currentValue = stat.ConstantValue;
                baseValue = stat.ConstantValue;
            }

            PresentationAttributeValueDisplayKind displayKind = stat.DisplayMode switch
            {
                EntityInsightValueDisplayMode.Current => PresentationAttributeValueDisplayKind.Current,
                EntityInsightValueDisplayMode.CurrentOverBase => PresentationAttributeValueDisplayKind.CurrentOverBase,
                _ => PresentationAttributeValueDisplayKind.Constant,
            };

            segments[segmentCount++] = $"{GetInsightStatLabelForProfile(stat)} { _semanticResolver.FormatAttributeValueRequired(stat.SemanticKey, displayKind, currentValue, baseValue)}";
        }

        if (semanticFieldsToInclude > 0)
        {
            EntityInsightSemanticFieldProfile field = profile.SemanticFields[0];
            int runtimeValue = ResolveSemanticFieldRuntimeValue(world, entity, field);
            segments[segmentCount++] = $"{_semanticResolver.ResolveMappingLabelRequired(field.MappingId)} {_semanticResolver.ResolveMappedRuntimeValueRequired(field.MappingId, runtimeValue)}";
        }

        if (segmentCount == 0)
        {
            return string.Empty;
        }

        return string.Join(" | ", segments, 0, segmentCount);
    }

    public string GetEntityCollectionTitleText() => ResolveTextTokenKey(EntityCollectionTitleToken);
    public string GetEntityCollectionEmptyTitleText() => ResolveTextTokenKey(EntityCollectionEmptyTitleToken);
    public string GetEntityCollectionWaitingBodyText() => ResolveTextTokenKey(EntityCollectionWaitingBodyToken);
    public string GetEntityCollectionNoCategoriesText() => ResolveTextTokenKey(EntityCollectionNoCategoriesToken);
    public string GetEntityCollectionPrimaryText() => ResolveTextTokenKey(EntityCollectionPrimaryToken);
    public string BuildEntityCollectionSubtitle(string viewKey, string aliasKey, int count) =>
        $"{viewKey} -> {aliasKey} | {count} {ResolveTextTokenKey(EntityCollectionEntitiesToken)}";

    public string BuildEntityCollectionRowsText(int startInclusive, int endExclusive, int totalCount, int visibleCount)
    {
        int start = totalCount <= 0 || visibleCount <= 0 ? 0 : startInclusive + 1;
        int end = totalCount <= 0 || visibleCount <= 0 ? 0 : endExclusive;
        return FormatTextTokenKey(
            EntityCollectionRowsToken,
            CreateNumericArg(start),
            CreateNumericArg(end));
    }

    public string BuildEntityCollectionSummaryText(int slot, int startInclusive, int endExclusive, int totalCount, int visibleCount)
    {
        string rowsText = BuildEntityCollectionRowsText(startInclusive, endExclusive, totalCount, visibleCount);
        return $"{GetEntityCollectionViewKey(slot)} -> {GetEntityCollectionAliasKey(slot)} | {GetEntityCollectionCount(slot)} {ResolveTextTokenKey(EntityCollectionEntitiesToken)} | {GetEntityCollectionCategoryCount(slot)} {ResolveTextTokenKey(EntityCollectionCategoriesToken)} | {rowsText}";
    }

    public string GetInsightAccentColor(int slot)
    {
        return GetRequiredInsightProfile(slot).AccentColorHex;
    }

    public string GetInsightSurfaceColor(int slot)
    {
        return GetRequiredInsightProfile(slot).SurfaceColorHex;
    }

    public string GetInsightGenreLabel(int slot)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        return ResolveTextTokenId(profile.GenreLabelTokenId);
    }

    public string GetInsightBody(int slot)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        return ResolveTextTokenId(profile.BodyTokenId);
    }

    public string GetInsightPortraitIconUri(int slot)
    {
        return BuildInsightPortraitIconUri(GetRequiredInsightProfile(slot));
    }

    public string GetInsightGenreIconUri(int slot)
    {
        return BuildInsightGenreIconUri(GetRequiredInsightProfile(slot));
    }

    public int GetInsightBadgeCount(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? profile.Badges.Length
            : 0;
    }

    public string GetInsightBadgeText(int slot, int badgeIndex)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile) &&
               (uint)badgeIndex < (uint)profile.Badges.Length
            ? ResolveTextTokenId(profile.Badges[badgeIndex].TextTokenId)
            : string.Empty;
    }

    public string GetInsightBadgeIconUri(int slot, int badgeIndex)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile) &&
               (uint)badgeIndex < (uint)profile.Badges.Length
            ? BuildInsightGlyphIconUri(profile.Badges[badgeIndex].Glyph, profile.AccentColorHex, profile.SurfaceColorHex)
            : string.Empty;
    }

    public int GetInsightStatCount(int slot) => _insightStatCounts[slot];

    public string GetInsightStatLabel(int slot, int statIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)statIndex >= (uint)profile.Stats.Length)
        {
            throw new InvalidOperationException($"Entity insight stat index '{statIndex}' is out of range for slot '{slot}'.");
        }

        return _semanticResolver.ResolveAttributeLabelRequired(profile.Stats[statIndex].SemanticKey);
    }

    public string GetInsightStatLabelForProfile(EntityInsightStatProfile stat)
    {
        ArgumentNullException.ThrowIfNull(stat);
        return _semanticResolver.ResolveAttributeLabelRequired(stat.SemanticKey);
    }

    public string GetInsightStatValueText(int slot, int statIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)statIndex >= (uint)profile.Stats.Length)
        {
            throw new InvalidOperationException($"Entity insight stat index '{statIndex}' is out of range for slot '{slot}'.");
        }

        int index = InsightStatIndex(slot, statIndex);
        float currentValue = _insightStatCurrentValues[index];
        float baseValue = _insightStatBaseValues[index];
        PresentationAttributeValueDisplayKind displayKind = profile.Stats[statIndex].DisplayMode switch
        {
            EntityInsightValueDisplayMode.Current => PresentationAttributeValueDisplayKind.Current,
            EntityInsightValueDisplayMode.CurrentOverBase => PresentationAttributeValueDisplayKind.CurrentOverBase,
            _ => PresentationAttributeValueDisplayKind.Constant,
        };

        return _semanticResolver.FormatAttributeValueRequired(
            profile.Stats[statIndex].SemanticKey,
            displayKind,
            currentValue,
            baseValue);
    }

    public string GetInsightStatIconUri(int slot, int statIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)statIndex >= (uint)profile.Stats.Length)
        {
            throw new InvalidOperationException($"Entity insight stat index '{statIndex}' is out of range for slot '{slot}'.");
        }

        return BuildInsightGlyphIconUri(profile.Stats[statIndex].Glyph, profile.AccentColorHex, profile.SurfaceColorHex);
    }

    public int GetInsightSemanticFieldCount(int slot) => _insightSemanticFieldCounts[slot];

    public string GetInsightSemanticFieldLabel(int slot, int fieldIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)fieldIndex >= (uint)profile.SemanticFields.Length)
        {
            throw new InvalidOperationException($"Entity insight semantic field index '{fieldIndex}' is out of range for slot '{slot}'.");
        }

        return _semanticResolver.ResolveMappingLabelRequired(profile.SemanticFields[fieldIndex].MappingId);
    }

    public string GetInsightSemanticFieldValueText(int slot, int fieldIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)fieldIndex >= (uint)profile.SemanticFields.Length)
        {
            throw new InvalidOperationException($"Entity insight semantic field index '{fieldIndex}' is out of range for slot '{slot}'.");
        }

        int value = _insightSemanticFieldRuntimeValues[InsightStatIndex(slot, fieldIndex)];
        return _semanticResolver.ResolveMappedRuntimeValueRequired(
            profile.SemanticFields[fieldIndex].MappingId,
            value);
    }

    public string GetInsightSemanticFieldIconUri(int slot, int fieldIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)fieldIndex >= (uint)profile.SemanticFields.Length)
        {
            throw new InvalidOperationException($"Entity insight semantic field index '{fieldIndex}' is out of range for slot '{slot}'.");
        }

        return BuildInsightGlyphIconUri(profile.SemanticFields[fieldIndex].Glyph, profile.AccentColorHex, profile.SurfaceColorHex);
    }

    public int GetInsightTipCount(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? profile.Tips.Length
            : 0;
    }

    public string GetInsightTipText(int slot, int tipIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)tipIndex >= (uint)profile.Tips.Length)
        {
            throw new InvalidOperationException($"Entity insight tip index '{tipIndex}' is out of range for slot '{slot}'.");
        }

        return ResolveTextTokenId(profile.Tips[tipIndex].TextTokenId);
    }

    public string GetInsightTipIconUri(int slot, int tipIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)tipIndex >= (uint)profile.Tips.Length)
        {
            throw new InvalidOperationException($"Entity insight tip index '{tipIndex}' is out of range for slot '{slot}'.");
        }

        return BuildInsightGlyphIconUri(profile.Tips[tipIndex].Glyph, profile.AccentColorHex, profile.SurfaceColorHex);
    }

    public int GetInsightActionCount(int slot) => _insightActionCounts[slot];

    public string GetInsightActionTitle(int slot, int actionIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)actionIndex >= (uint)profile.Actions.Length)
        {
            throw new InvalidOperationException($"Entity insight action index '{actionIndex}' is out of range for slot '{slot}'.");
        }

        return ResolveTextTokenId(profile.Actions[actionIndex].TitleTokenId);
    }

    public string GetInsightActionBody(int slot, int actionIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)actionIndex >= (uint)profile.Actions.Length)
        {
            throw new InvalidOperationException($"Entity insight action index '{actionIndex}' is out of range for slot '{slot}'.");
        }

        return ResolveTextTokenId(profile.Actions[actionIndex].BodyTokenId);
    }

    public string GetInsightActionIconUri(int slot, int actionIndex)
    {
        EntityInsightProfile profile = GetRequiredInsightProfile(slot);
        if ((uint)actionIndex >= (uint)profile.Actions.Length)
        {
            throw new InvalidOperationException($"Entity insight action index '{actionIndex}' is out of range for slot '{slot}'.");
        }

        EntityInsightActionRuntimeFlags flags = GetInsightActionRuntimeFlags(slot, actionIndex);
        bool emphatic = (flags & EntityInsightActionRuntimeFlags.Active) != 0;
        return BuildInsightGlyphIconUri(profile.Actions[actionIndex].Glyph, profile.AccentColorHex, profile.SurfaceColorHex, emphatic);
    }

    public bool IsInsightActionPresent(int slot, int actionIndex)
    {
        return (GetInsightActionRuntimeFlags(slot, actionIndex) & EntityInsightActionRuntimeFlags.Present) != 0;
    }

    public bool IsInsightActionBlocked(int slot, int actionIndex)
    {
        return (GetInsightActionRuntimeFlags(slot, actionIndex) & EntityInsightActionRuntimeFlags.Blocked) != 0;
    }

    public bool IsInsightActionActive(int slot, int actionIndex)
    {
        return (GetInsightActionRuntimeFlags(slot, actionIndex) & EntityInsightActionRuntimeFlags.Active) != 0;
    }

    public string GetInsightActionStateText(int slot, int actionIndex)
    {
        EntityInsightActionRuntimeFlags flags = GetInsightActionRuntimeFlags(slot, actionIndex);
        if ((flags & EntityInsightActionRuntimeFlags.Active) != 0)
        {
            return ResolveTextTokenKey(ActionStateActiveToken);
        }

        if ((flags & EntityInsightActionRuntimeFlags.Blocked) != 0)
        {
            return ResolveTextTokenKey(ActionStateBlockedToken);
        }

        if ((flags & EntityInsightActionRuntimeFlags.Present) != 0)
        {
            return ResolveTextTokenKey(ActionStateReadyToken);
        }

        return ResolveTextTokenKey(ActionStateUnavailableToken);
    }

    private EntityInsightProfile GetRequiredInsightProfile(int slot)
    {
        if (!TryGetInsightProfile(slot, out EntityInsightProfile profile))
        {
            throw new InvalidOperationException($"Entity insight profile is required for slot '{slot}'.");
        }

        return profile;
    }

    private int ResolveSemanticFieldRuntimeValue(World world, Entity entity, EntityInsightSemanticFieldProfile field)
    {
        int entityTeamId = world.TryGet(entity, out Team team) ? team.Id : 0;
        return field.EntityRelation switch
        {
            EntityInsightEntityRelationKind.SelfTeamRelationship => (int)TeamManager.GetRelationship(entityTeamId, entityTeamId),
            _ => throw new InvalidOperationException($"Unsupported entity relation kind '{field.EntityRelation}'."),
        };
    }

    private static PresentationTextArg CreateNumericArg(int value) => PresentationTextArg.FromInt32(value);

    private EntityInsightActionRuntimeFlags GetInsightActionRuntimeFlags(int slot, int actionIndex)
    {
        if ((uint)actionIndex >= (uint)MaxInsightActionsPerPanel)
        {
            return EntityInsightActionRuntimeFlags.None;
        }

        return (EntityInsightActionRuntimeFlags)_insightActionFlags[InsightActionIndex(slot, actionIndex)];
    }

    private bool SampleInsightBrief(int slot, World world, Entity entity)
    {
        bool dirty = false;

        if (entity == Entity.Null || !world.IsAlive(entity))
        {
            dirty |= SetString(_titles, slot, "Entity Insight");
            dirty |= SetString(_subtitles, slot, ResolveMissingSubtitle(_targets[slot]));
            dirty |= SetInsightProfileIndex(slot, 0);
            dirty |= SetInsightStatCount(slot, 0);
            dirty |= SetInsightSemanticFieldCount(slot, 0);
            dirty |= SetInsightActionCount(slot, 0);
            ClearInsightState(slot);
            return dirty;
        }

        string title = world.TryGet(entity, out Name name) && !string.IsNullOrWhiteSpace(name.Value)
            ? name.Value
            : $"Entity #{entity.Id}";
        dirty |= SetString(_titles, slot, title);

        if (!world.TryGet(entity, out EntityTemplateKeyCm templateKey) ||
            !_insightCatalog.TryGetProfileIndex(templateKey.TemplateKeyId, out int profileIndex) ||
            !_insightCatalog.TryGetProfileByIndex(profileIndex, out EntityInsightProfile profile))
        {
            string templateSubtitle = world.TryGet(entity, out EntityTemplateKeyCm resolvedTemplateKey)
                ? $"Template `{resolvedTemplateKey.TemplateKeyId}` has no insight profile."
                : "Template key is unavailable for this entity.";
            dirty |= SetString(_subtitles, slot, templateSubtitle);
            dirty |= SetInsightProfileIndex(slot, 0);
            dirty |= SetInsightStatCount(slot, 0);
            dirty |= SetInsightSemanticFieldCount(slot, 0);
            dirty |= SetInsightActionCount(slot, 0);
            ClearInsightState(slot);
            return dirty;
        }

        dirty |= SetString(_subtitles, slot, ResolveTextTokenId(profile.SubtitleTokenId));
        dirty |= SetInsightProfileIndex(slot, profileIndex + 1);
        dirty |= SampleInsightStats(slot, world, entity, profile);
        dirty |= SampleInsightSemanticFields(slot, world, entity, profile);
        dirty |= SampleInsightActions(slot, world, entity, profile);
        return dirty;
    }

    private bool SampleInsightStats(int slot, World world, Entity entity, EntityInsightProfile profile)
    {
        bool dirty = false;
        AttributeBuffer attributes = world.TryGet(entity, out AttributeBuffer runtimeAttributes) ? runtimeAttributes : default;
        int count = Math.Min(profile.Stats.Length, MaxInsightStatsPerPanel);
        for (int statIndex = 0; statIndex < count; statIndex++)
        {
            EntityInsightStatProfile stat = profile.Stats[statIndex];
            float currentValue = 0f;
            float baseValue = 0f;
            if (stat.SourceKind == EntityInsightStatSourceKind.Attribute &&
                stat.AttributeId >= 0)
            {
                currentValue = attributes.GetCurrent(stat.AttributeId);
                baseValue = attributes.GetBase(stat.AttributeId);
            }
            else
            {
                currentValue = stat.ConstantValue;
                baseValue = stat.ConstantValue;
            }

            int valueIndex = InsightStatIndex(slot, statIndex);
            dirty |= SetInsightStatValue(_insightStatCurrentValues, valueIndex, currentValue);
            dirty |= SetInsightStatValue(_insightStatBaseValues, valueIndex, baseValue);
        }

        for (int statIndex = count; statIndex < MaxInsightStatsPerPanel; statIndex++)
        {
            int valueIndex = InsightStatIndex(slot, statIndex);
            dirty |= SetInsightStatValue(_insightStatCurrentValues, valueIndex, 0f);
            dirty |= SetInsightStatValue(_insightStatBaseValues, valueIndex, 0f);
        }

        dirty |= SetInsightStatCount(slot, count);
        return dirty;
    }

    private bool SampleInsightSemanticFields(int slot, World world, Entity entity, EntityInsightProfile profile)
    {
        bool dirty = false;
        int count = Math.Min(profile.SemanticFields.Length, MaxInsightStatsPerPanel);
        int entityTeamId = world.TryGet(entity, out Team team) ? team.Id : 0;
        for (int fieldIndex = 0; fieldIndex < count; fieldIndex++)
        {
            EntityInsightSemanticFieldProfile field = profile.SemanticFields[fieldIndex];
            int value = field.EntityRelation switch
            {
                EntityInsightEntityRelationKind.SelfTeamRelationship => (int)TeamManager.GetRelationship(entityTeamId, entityTeamId),
                _ => 0,
            };

            dirty |= SetInsightSemanticFieldValue(slot, fieldIndex, value);
        }

        for (int fieldIndex = count; fieldIndex < MaxInsightStatsPerPanel; fieldIndex++)
        {
            dirty |= SetInsightSemanticFieldValue(slot, fieldIndex, 0);
        }

        dirty |= SetInsightSemanticFieldCount(slot, count);
        return dirty;
    }

    private bool SampleInsightActions(int slot, World world, Entity entity, EntityInsightProfile profile)
    {
        bool dirty = false;
        bool hasAbilities = world.TryGet(entity, out AbilityStateBuffer baseSlots);
        bool hasForm = world.TryGet(entity, out AbilityFormSlotBuffer formSlots);
        bool hasGranted = world.TryGet(entity, out GrantedSlotBuffer grantedSlots);
        bool hasTags = world.TryGet(entity, out GameplayTagContainer tags);
        bool hasExec = world.TryGet(entity, out AbilityExecInstance activeExec);

        int count = Math.Min(profile.Actions.Length, MaxInsightActionsPerPanel);
        for (int actionIndex = 0; actionIndex < count; actionIndex++)
        {
            EntityInsightActionProfile action = profile.Actions[actionIndex];
            EntityInsightActionRuntimeFlags flags = EntityInsightActionRuntimeFlags.None;
            if (hasAbilities &&
                TryResolveAbilitySlot(in baseSlots, in formSlots, hasForm, in grantedSlots, hasGranted, action.AbilityId, out int slotIndex))
            {
                flags |= EntityInsightActionRuntimeFlags.Present;
                if (IsAbilityBlocked(action.AbilityId, hasTags, in tags))
                {
                    flags |= EntityInsightActionRuntimeFlags.Blocked;
                }

                if (hasExec && (activeExec.AbilityId == action.AbilityId || activeExec.AbilitySlot == slotIndex))
                {
                    flags |= EntityInsightActionRuntimeFlags.Active;
                }
            }

            dirty |= SetInsightActionFlags(slot, actionIndex, flags);
        }

        for (int actionIndex = count; actionIndex < MaxInsightActionsPerPanel; actionIndex++)
        {
            dirty |= SetInsightActionFlags(slot, actionIndex, EntityInsightActionRuntimeFlags.None);
        }

        dirty |= SetInsightActionCount(slot, count);
        return dirty;
    }

    private bool TryResolveAbilitySlot(
        in AbilityStateBuffer baseSlots,
        in AbilityFormSlotBuffer formSlots,
        bool hasForm,
        in GrantedSlotBuffer grantedSlots,
        bool hasGranted,
        int abilityId,
        out int slotIndex)
    {
        for (int i = 0; i < baseSlots.Count; i++)
        {
            AbilitySlotState slot = AbilitySlotResolver.Resolve(in baseSlots, in formSlots, hasForm, in grantedSlots, hasGranted, i);
            if (slot.AbilityId == abilityId)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    private bool IsAbilityBlocked(int abilityId, bool hasTags, in GameplayTagContainer tags)
    {
        if (_abilityDefinitions == null ||
            !_abilityDefinitions.TryGet(abilityId, out var definition) ||
            !definition.HasActivationBlockTags)
        {
            return false;
        }

        GameplayTagContainer blockedAny = definition.ActivationBlockTags.BlockedAny;
        GameplayTagContainer requiredAll = definition.ActivationBlockTags.RequiredAll;
        if (!hasTags)
        {
            return !requiredAll.IsEmpty;
        }

        GameplayTagContainer resolvedTags = tags;
        return !resolvedTags.ContainsAll(in requiredAll) ||
               resolvedTags.Intersects(in blockedAny);
    }
}
