using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;

namespace EntityInfoPanelsMod.Insight;

public sealed class EntityInsightProfileLoader
{
    private const string ProfilePath = "EntityInfo/insight_profiles.json";

    private readonly ConfigPipeline _configs;

    public EntityInsightProfileLoader(ConfigPipeline configs)
    {
        _configs = configs ?? throw new ArgumentNullException(nameof(configs));
    }

    public EntityInsightProfileCatalog Load(
        ConfigCatalog? catalog,
        ConfigConflictReport? report,
        EntityTemplateKeyRegistry templateKeys,
        PresentationTextCatalog textCatalog,
        PresentationImageRegistry imageRegistry)
    {
        ArgumentNullException.ThrowIfNull(templateKeys);
        ArgumentNullException.ThrowIfNull(textCatalog);
        ArgumentNullException.ThrowIfNull(imageRegistry);

        var entry = ConfigPipeline.GetEntryOrDefault(catalog, ProfilePath, ConfigMergePolicy.ArrayById, "id");
        IReadOnlyList<MergedConfigEntry> nodes = _configs.MergeArrayByIdFromCatalog(in entry, report);
        if (nodes.Count == 0)
        {
            return EntityInsightProfileCatalog.Empty;
        }

        var profiles = new EntityInsightProfile[nodes.Count];
        var profileIndexByTemplateKey = new Dictionary<int, int>();
        for (int profileIndex = 0; profileIndex < nodes.Count; profileIndex++)
        {
            JsonObject node = nodes[profileIndex].Node;
            string profileId = ReadRequiredString(node, "id");
            int[] templateKeyIds = ReadTemplateKeyIds(node, templateKeys, profileId);
            for (int i = 0; i < templateKeyIds.Length; i++)
            {
                int templateKeyId = templateKeyIds[i];
                if (profileIndexByTemplateKey.TryGetValue(templateKeyId, out int existingIndex))
                {
                    throw new InvalidOperationException(
                        $"Entity insight profile '{profileId}' reuses template key '{templateKeys.GetName(templateKeyId)}' already owned by profile index {existingIndex}.");
                }

                profileIndexByTemplateKey[templateKeyId] = profileIndex;
            }

            profiles[profileIndex] = new EntityInsightProfile
            {
                Id = profileId,
                TemplateKeyIds = templateKeyIds,
                AccentColorHex = ReadRequiredString(node, "accentColorHex"),
                SurfaceColorHex = ReadRequiredString(node, "surfaceColorHex"),
                GenreGlyph = ReadRequiredString(node, "genreGlyph"),
                PortraitImageAssetId = ResolveRequiredImageAssetId(imageRegistry, node, "portraitImageAsset", profileId),
                GenreLabelTokenId = ResolveRequiredTokenId(textCatalog, node, "genreLabelToken", profileId),
                SubtitleTokenId = ResolveRequiredTokenId(textCatalog, node, "subtitleToken", profileId),
                BodyTokenId = ResolveRequiredTokenId(textCatalog, node, "bodyToken", profileId),
                Badges = ReadBadges(textCatalog, node, profileId),
                Stats = ReadStats(textCatalog, node, profileId),
                SemanticFields = ReadSemanticFields(node, profileId),
                Tips = ReadTips(textCatalog, node, profileId),
                Actions = ReadActions(textCatalog, node, profileId)
            };
        }

        return new EntityInsightProfileCatalog(profiles, profileIndexByTemplateKey);
    }

    private static int[] ReadTemplateKeyIds(JsonObject node, EntityTemplateKeyRegistry templateKeys, string profileId)
    {
        if (node["templateIds"] is not JsonArray templateIdsNode || templateIdsNode.Count == 0)
        {
            throw new InvalidOperationException($"Entity insight profile '{profileId}' must define a non-empty 'templateIds' array.");
        }

        var templateKeyIds = new int[templateIdsNode.Count];
        for (int i = 0; i < templateIdsNode.Count; i++)
        {
            string templateId = templateIdsNode[i]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(templateId))
            {
                throw new InvalidOperationException($"Entity insight profile '{profileId}' contains an empty template id.");
            }

            if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
            {
                throw new InvalidOperationException($"Entity insight profile '{profileId}' references unknown template '{templateId}'.");
            }

            templateKeyIds[i] = templateKeyId;
        }

        return templateKeyIds;
    }

    private static EntityInsightBadgeProfile[] ReadBadges(PresentationTextCatalog textCatalog, JsonObject node, string profileId)
    {
        if (node["badges"] is not JsonArray badgeNodes || badgeNodes.Count == 0)
        {
            return Array.Empty<EntityInsightBadgeProfile>();
        }

        var badges = new EntityInsightBadgeProfile[badgeNodes.Count];
        for (int i = 0; i < badgeNodes.Count; i++)
        {
            if (badgeNodes[i] is not JsonObject badgeNode)
            {
                throw new InvalidOperationException($"Entity insight profile '{profileId}' badge[{i}] must be an object.");
            }

            badges[i] = new EntityInsightBadgeProfile
            {
                Glyph = ReadRequiredString(badgeNode, "glyph"),
                TextTokenId = ResolveRequiredTokenId(textCatalog, badgeNode, "textToken", $"{profileId}.badges[{i}]")
            };
        }

        return badges;
    }

    private static EntityInsightStatProfile[] ReadStats(PresentationTextCatalog textCatalog, JsonObject node, string profileId)
    {
        if (node["stats"] is not JsonArray statNodes || statNodes.Count == 0)
        {
            throw new InvalidOperationException($"Entity insight profile '{profileId}' must define at least one stat.");
        }

        var stats = new EntityInsightStatProfile[statNodes.Count];
        for (int i = 0; i < statNodes.Count; i++)
        {
            if (statNodes[i] is not JsonObject statNode)
            {
                throw new InvalidOperationException($"Entity insight profile '{profileId}' stat[{i}] must be an object.");
            }

            string source = ReadRequiredString(statNode, "source");
            EntityInsightStatSourceKind sourceKind = source switch
            {
                "attribute" => EntityInsightStatSourceKind.Attribute,
                "constant" => EntityInsightStatSourceKind.Constant,
                _ => throw new InvalidOperationException($"Entity insight profile '{profileId}' stat[{i}] uses unsupported source '{source}'.")
            };

            string display = ReadRequiredString(statNode, "display");
            EntityInsightValueDisplayMode displayMode = display switch
            {
                "current" => EntityInsightValueDisplayMode.Current,
                "currentOverBase" => EntityInsightValueDisplayMode.CurrentOverBase,
                "constant" => EntityInsightValueDisplayMode.Constant,
                _ => throw new InvalidOperationException($"Entity insight profile '{profileId}' stat[{i}] uses unsupported display '{display}'.")
            };

            int attributeId = AttributeRegistry.InvalidId;
            float constantValue = 0f;
            if (sourceKind == EntityInsightStatSourceKind.Attribute)
            {
                string attributeName = ReadRequiredString(statNode, "attribute");
                attributeId = AttributeRegistry.GetId(attributeName);
                if (attributeId == AttributeRegistry.InvalidId)
                {
                    throw new InvalidOperationException($"Entity insight profile '{profileId}' stat[{i}] references unknown attribute '{attributeName}'.");
                }
            }
            else
            {
                constantValue = statNode["value"]?.GetValue<float>()
                    ?? throw new InvalidOperationException($"Entity insight profile '{profileId}' stat[{i}] must define numeric 'value'.");
            }

            stats[i] = new EntityInsightStatProfile
            {
                SemanticKey = ReadRequiredString(statNode, "semanticKey"),
                Glyph = ReadRequiredString(statNode, "glyph"),
                SourceKind = sourceKind,
                DisplayMode = displayMode,
                AttributeId = attributeId,
                ConstantValue = constantValue
            };
        }

        return stats;
    }

    private static EntityInsightSemanticFieldProfile[] ReadSemanticFields(JsonObject node, string profileId)
    {
        if (node["semanticFields"] is not JsonArray fieldNodes || fieldNodes.Count == 0)
        {
            return Array.Empty<EntityInsightSemanticFieldProfile>();
        }

        var fields = new EntityInsightSemanticFieldProfile[fieldNodes.Count];
        for (int i = 0; i < fieldNodes.Count; i++)
        {
            if (fieldNodes[i] is not JsonObject fieldNode)
            {
                throw new InvalidOperationException($"Entity insight profile '{profileId}' semanticFields[{i}] must be an object.");
            }

            string source = ReadRequiredString(fieldNode, "semanticSource");
            EntityInsightSemanticValueSourceKind semanticValueSource = source switch
            {
                "team.relationship.self" => EntityInsightSemanticValueSourceKind.TeamRelationshipSelf,
                _ => throw new InvalidOperationException($"Entity insight profile '{profileId}' semanticFields[{i}] uses unsupported semanticSource '{source}'.")
            };

            fields[i] = new EntityInsightSemanticFieldProfile
            {
                Glyph = ReadRequiredString(fieldNode, "glyph"),
                MappingId = ReadRequiredString(fieldNode, "mappingId"),
                SemanticValueSource = semanticValueSource,
            };
        }

        return fields;
    }

    private static EntityInsightTipProfile[] ReadTips(PresentationTextCatalog textCatalog, JsonObject node, string profileId)
    {
        if (node["tips"] is not JsonArray tipNodes || tipNodes.Count == 0)
        {
            throw new InvalidOperationException($"Entity insight profile '{profileId}' must define at least one tip.");
        }

        var tips = new EntityInsightTipProfile[tipNodes.Count];
        for (int i = 0; i < tipNodes.Count; i++)
        {
            if (tipNodes[i] is not JsonObject tipNode)
            {
                throw new InvalidOperationException($"Entity insight profile '{profileId}' tip[{i}] must be an object.");
            }

            tips[i] = new EntityInsightTipProfile
            {
                Glyph = ReadRequiredString(tipNode, "glyph"),
                TextTokenId = ResolveRequiredTokenId(textCatalog, tipNode, "textToken", $"{profileId}.tips[{i}]")
            };
        }

        return tips;
    }

    private static EntityInsightActionProfile[] ReadActions(PresentationTextCatalog textCatalog, JsonObject node, string profileId)
    {
        if (node["actions"] is not JsonArray actionNodes || actionNodes.Count == 0)
        {
            throw new InvalidOperationException($"Entity insight profile '{profileId}' must define at least one action.");
        }

        var actions = new EntityInsightActionProfile[actionNodes.Count];
        for (int i = 0; i < actionNodes.Count; i++)
        {
            if (actionNodes[i] is not JsonObject actionNode)
            {
                throw new InvalidOperationException($"Entity insight profile '{profileId}' action[{i}] must be an object.");
            }

            string abilityKey = ReadRequiredString(actionNode, "ability");
            int abilityId = AbilityIdRegistry.GetId(abilityKey);
            if (abilityId <= 0)
            {
                throw new InvalidOperationException($"Entity insight profile '{profileId}' action[{i}] references unknown ability '{abilityKey}'.");
            }

            actions[i] = new EntityInsightActionProfile
            {
                AbilityId = abilityId,
                Glyph = ReadRequiredString(actionNode, "glyph"),
                TitleTokenId = ResolveRequiredTokenId(textCatalog, actionNode, "titleToken", $"{profileId}.actions[{i}]"),
                BodyTokenId = ResolveRequiredTokenId(textCatalog, actionNode, "bodyToken", $"{profileId}.actions[{i}]")
            };
        }

        return actions;
    }

    private static int ResolveRequiredTokenId(PresentationTextCatalog textCatalog, JsonObject node, string propertyName, string scope)
    {
        string tokenKey = ReadRequiredString(node, propertyName);
        int tokenId = textCatalog.GetTokenId(tokenKey);
        if (tokenId <= 0)
        {
            throw new InvalidOperationException($"Entity insight scope '{scope}' references unknown text token '{tokenKey}'.");
        }

        return tokenId;
    }

    private static int ResolveRequiredImageAssetId(
        PresentationImageRegistry imageRegistry,
        JsonObject node,
        string propertyName,
        string scope)
    {
        RejectLegacyPortraitGlyph(node, scope);
        string assetKey = ReadRequiredString(node, propertyName);
        int imageAssetId = imageRegistry.GetId(assetKey);
        if (imageAssetId <= 0)
        {
            throw new InvalidOperationException($"Entity insight scope '{scope}' references unknown image asset '{assetKey}'.");
        }

        return imageAssetId;
    }

    private static void RejectLegacyPortraitGlyph(JsonObject node, string scope)
    {
        if (node.ContainsKey("portraitGlyph"))
        {
            throw new InvalidOperationException(
                $"Entity insight scope '{scope}' uses legacy 'portraitGlyph'. Define 'portraitImageAsset' only.");
        }
    }

    private static string ReadRequiredString(JsonObject node, string propertyName)
    {
        string value = node[propertyName]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Entity insight config property '{propertyName}' must not be empty.");
        }

        return value;
    }

    private static string ReadOptionalString(JsonObject node, string propertyName)
    {
        string value = node[propertyName]?.GetValue<string>() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
