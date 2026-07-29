using System;
using Arch.Core;
using Arch.Core.Extensions;
using EntityInfoPanelsMod.Insight;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Spawning;

namespace EntityInfoPanelsMod;

public sealed partial class EntityInfoPanelService
{
    private const string ActionStateReadyToken = "entityinfo.actionstate.ready";
    private const string ActionStateBlockedToken = "entityinfo.actionstate.blocked";
    private const string ActionStateActiveToken = "entityinfo.actionstate.active";
    private const string ActionStateUnavailableToken = "entityinfo.actionstate.unavailable";

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
            !world.TryGet(entity, out EntityTemplateKeyRef templateKey) ||
            !_insightCatalog.TryGetProfileByTemplateKey(templateKey.TemplateKeyId, out profile))
        {
            return false;
        }

        return true;
    }

    public string ResolveTextTokenKey(string tokenKey) => _insightTextResolver.ResolveRequiredTokenKey(tokenKey);
    public string ResolveTextTokenId(int tokenId) => _insightTextResolver.ResolveRequiredTokenId(tokenId);
    public string BuildInsightPortraitIconUri(EntityInsightProfile profile) => _insightIconFactory.Build(profile.PortraitGlyph, profile.AccentColorHex, profile.SurfaceColorHex, emphatic: true);
    public string BuildInsightGenreIconUri(EntityInsightProfile profile) => _insightIconFactory.Build(profile.GenreGlyph, profile.AccentColorHex, profile.SurfaceColorHex);
    public string BuildInsightGlyphIconUri(string glyph, string accentHex, string surfaceHex, bool emphatic = false) => _insightIconFactory.Build(glyph, accentHex, surfaceHex, emphatic);

    public string GetInsightAccentColor(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? profile.AccentColorHex
            : "#58B7FF";
    }

    public string GetInsightSurfaceColor(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? profile.SurfaceColorHex
            : "#0F1721";
    }

    public string GetInsightGenreLabel(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? ResolveTextTokenId(profile.GenreLabelTokenId)
            : string.Empty;
    }

    public string GetInsightBody(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? ResolveTextTokenId(profile.BodyTokenId)
            : string.Empty;
    }

    public string GetInsightPortraitIconUri(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? BuildInsightPortraitIconUri(profile)
            : string.Empty;
    }

    public string GetInsightGenreIconUri(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? BuildInsightGenreIconUri(profile)
            : string.Empty;
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
        return TryGetInsightProfile(slot, out EntityInsightProfile profile) &&
               (uint)statIndex < (uint)profile.Stats.Length
            ? ResolveTextTokenId(profile.Stats[statIndex].LabelTokenId)
            : string.Empty;
    }

    public string GetInsightStatValueText(int slot, int statIndex)
    {
        if (!TryGetInsightProfile(slot, out EntityInsightProfile profile) ||
            (uint)statIndex >= (uint)profile.Stats.Length)
        {
            return string.Empty;
        }

        int index = InsightStatIndex(slot, statIndex);
        float currentValue = _insightStatCurrentValues[index];
        float baseValue = _insightStatBaseValues[index];
        return profile.Stats[statIndex].DisplayMode switch
        {
            EntityInsightValueDisplayMode.Current => FormatNumber(currentValue),
            EntityInsightValueDisplayMode.CurrentOverBase => $"{FormatNumber(currentValue)}/{FormatNumber(baseValue)}",
            _ => FormatNumber(currentValue)
        };
    }

    public string GetInsightStatIconUri(int slot, int statIndex)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile) &&
               (uint)statIndex < (uint)profile.Stats.Length
            ? BuildInsightGlyphIconUri(profile.Stats[statIndex].Glyph, profile.AccentColorHex, profile.SurfaceColorHex)
            : string.Empty;
    }

    public int GetInsightTipCount(int slot)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile)
            ? profile.Tips.Length
            : 0;
    }

    public string GetInsightTipText(int slot, int tipIndex)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile) &&
               (uint)tipIndex < (uint)profile.Tips.Length
            ? ResolveTextTokenId(profile.Tips[tipIndex].TextTokenId)
            : string.Empty;
    }

    public string GetInsightTipIconUri(int slot, int tipIndex)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile) &&
               (uint)tipIndex < (uint)profile.Tips.Length
            ? BuildInsightGlyphIconUri(profile.Tips[tipIndex].Glyph, profile.AccentColorHex, profile.SurfaceColorHex)
            : string.Empty;
    }

    public int GetInsightActionCount(int slot) => _insightActionCounts[slot];

    public string GetInsightActionTitle(int slot, int actionIndex)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile) &&
               (uint)actionIndex < (uint)profile.Actions.Length
            ? ResolveTextTokenId(profile.Actions[actionIndex].TitleTokenId)
            : string.Empty;
    }

    public string GetInsightActionBody(int slot, int actionIndex)
    {
        return TryGetInsightProfile(slot, out EntityInsightProfile profile) &&
               (uint)actionIndex < (uint)profile.Actions.Length
            ? ResolveTextTokenId(profile.Actions[actionIndex].BodyTokenId)
            : string.Empty;
    }

    public string GetInsightActionIconUri(int slot, int actionIndex)
    {
        if (!TryGetInsightProfile(slot, out EntityInsightProfile profile) ||
            (uint)actionIndex >= (uint)profile.Actions.Length)
        {
            return string.Empty;
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
        EntityInfoPanelTemplateDescriptor template = ResolvePanelTemplate(slot);

        if (entity == Entity.Null || !world.IsAlive(entity))
        {
            dirty |= SetString(_titles, slot, "Entity Insight");
            dirty |= SetString(_subtitles, slot, ResolveMissingSubtitle(_targets[slot]));
            dirty |= SetInsightProfileIndex(slot, 0);
            dirty |= SetInsightStatCount(slot, 0);
            dirty |= SetInsightActionCount(slot, 0);
            ClearInsightState(slot);
            return dirty;
        }

        string title = world.TryGet(entity, out Name name) && !string.IsNullOrWhiteSpace(name.Value)
            ? name.Value
            : $"Entity #{entity.Id}";
        dirty |= SetString(_titles, slot, title);

        if (!world.TryGet(entity, out EntityTemplateKeyRef templateKey) ||
            !_insightCatalog.TryGetProfileIndex(templateKey.TemplateKeyId, out int profileIndex) ||
            !_insightCatalog.TryGetProfileByIndex(profileIndex, out EntityInsightProfile profile))
        {
            _ = ResolveMissingTemplateProfile(template, entity, required: false);
            string templateSubtitle = world.TryGet(entity, out EntityTemplateKeyRef resolvedTemplateKey)
                ? $"Template `{resolvedTemplateKey.TemplateKeyId}` has no insight profile."
                : "Template key is unavailable for this entity.";
            dirty |= SetString(_subtitles, slot, templateSubtitle);
            dirty |= SetInsightProfileIndex(slot, 0);
            dirty |= SetInsightStatCount(slot, 0);
            dirty |= SetInsightActionCount(slot, 0);
            ClearInsightState(slot);
            return dirty;
        }

        string subtitle = (template.Sections & EntityInfoPanelTemplateSectionFlags.Subtitle) != 0
            ? ResolveTextTokenId(profile.SubtitleTokenId)
            : string.Empty;
        dirty |= SetString(_subtitles, slot, subtitle);
        dirty |= SetInsightProfileIndex(slot, profileIndex + 1);
        dirty |= (template.Sections & EntityInfoPanelTemplateSectionFlags.Stats) != 0
            ? SampleInsightStats(slot, world, entity, profile)
            : ClearInsightStats(slot);
        dirty |= (template.Sections & EntityInfoPanelTemplateSectionFlags.Actions) != 0
            ? SampleInsightActions(slot, world, entity, profile)
            : ClearInsightActions(slot);
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

    private bool ClearInsightStats(int slot)
    {
        bool dirty = false;
        for (int statIndex = 0; statIndex < MaxInsightStatsPerPanel; statIndex++)
        {
            int valueIndex = InsightStatIndex(slot, statIndex);
            dirty |= SetInsightStatValue(_insightStatCurrentValues, valueIndex, 0f);
            dirty |= SetInsightStatValue(_insightStatBaseValues, valueIndex, 0f);
        }

        dirty |= SetInsightStatCount(slot, 0);
        return dirty;
    }

    private bool SampleInsightActions(int slot, World world, Entity entity, EntityInsightProfile profile)
    {
        bool dirty = false;
        bool hasAbilities = world.TryGet(entity, out AbilityStateBuffer baseSlots);
        bool hasForm = world.TryGet(entity, out AbilityFormSlotBuffer formSlots);
        bool hasItemGranted = world.TryGet(entity, out ItemGrantedSlotBuffer itemGrantedSlots);
        bool hasGranted = world.TryGet(entity, out GrantedSlotBuffer grantedSlots);
        bool hasTags = world.TryGet(entity, out GameplayTagContainer tags);
        bool hasExec = world.TryGet(entity, out AbilityExecInstance activeExec);

        int count = Math.Min(profile.Actions.Length, MaxInsightActionsPerPanel);
        for (int actionIndex = 0; actionIndex < count; actionIndex++)
        {
            EntityInsightActionProfile action = profile.Actions[actionIndex];
            EntityInsightActionRuntimeFlags flags = EntityInsightActionRuntimeFlags.None;
            if (hasAbilities &&
                TryResolveAbilitySlot(
                    in baseSlots,
                    in formSlots,
                    hasForm,
                    in itemGrantedSlots,
                    hasItemGranted,
                    in grantedSlots,
                    hasGranted,
                    action.AbilityId,
                    out int slotIndex))
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

    private bool ClearInsightActions(int slot)
    {
        bool dirty = false;
        for (int actionIndex = 0; actionIndex < MaxInsightActionsPerPanel; actionIndex++)
        {
            dirty |= SetInsightActionFlags(slot, actionIndex, EntityInsightActionRuntimeFlags.None);
        }

        dirty |= SetInsightActionCount(slot, 0);
        return dirty;
    }

    private bool TryResolveAbilitySlot(
        in AbilityStateBuffer baseSlots,
        in AbilityFormSlotBuffer formSlots,
        bool hasForm,
        in ItemGrantedSlotBuffer itemGrantedSlots,
        bool hasItemGranted,
        in GrantedSlotBuffer grantedSlots,
        bool hasGranted,
        int abilityId,
        out int slotIndex)
    {
        for (int i = 0; i < AbilityStateBuffer.CAPACITY; i++)
        {
            if (!AbilitySlotResolver.TryResolve(
                in baseSlots,
                in formSlots,
                hasForm,
                in itemGrantedSlots,
                hasItemGranted,
                in grantedSlots,
                hasGranted,
                i,
                out AbilitySlotState slot))
            {
                continue;
            }
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
