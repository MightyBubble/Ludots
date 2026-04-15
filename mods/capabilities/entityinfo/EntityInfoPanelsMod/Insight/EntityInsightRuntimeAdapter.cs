using System;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;

namespace EntityInfoPanelsMod.Insight;

public sealed class EntityInsightRuntimeAdapter
{
    public readonly struct EntityInsightSemanticFieldRuntimeValue
    {
        public EntityInsightSemanticFieldRuntimeValue(string mappedValueKey, float numericValue)
        {
            MappedValueKey = mappedValueKey ?? string.Empty;
            NumericValue = numericValue;
        }

        public string MappedValueKey { get; }
        public float NumericValue { get; }
    }

    private readonly PresentationImageSourceResolver? _imageSourceResolver;
    private readonly PresentationImageBindingResolver? _imageBindingResolver;
    private readonly RelationshipRuntime? _relationshipRuntime;
    private readonly RelationshipTypeRegistry? _relationshipTypes;
    private readonly RelationshipMetricRegistry? _relationshipMetrics;
    private readonly RelationshipFlagRegistry? _relationshipFlags;

    public EntityInsightRuntimeAdapter(
        PresentationImageSourceResolver? imageSourceResolver,
        PresentationImageBindingResolver? imageBindingResolver = null,
        RelationshipRuntime? relationshipRuntime = null,
        RelationshipTypeRegistry? relationshipTypes = null,
        RelationshipMetricRegistry? relationshipMetrics = null,
        RelationshipFlagRegistry? relationshipFlags = null)
    {
        _imageSourceResolver = imageSourceResolver;
        _imageBindingResolver = imageBindingResolver;
        _relationshipRuntime = relationshipRuntime;
        _relationshipTypes = relationshipTypes;
        _relationshipMetrics = relationshipMetrics;
        _relationshipFlags = relationshipFlags;
    }

    public string ResolveImageSourceRequired(World world, Entity entity, EntityInsightProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.ImageSource.Scope switch
        {
            EntityInsightImageSourceScopeKind.Entity => ResolveEntityImageSourceRequired(world, entity, profile.ImageSource),
            EntityInsightImageSourceScopeKind.Profile => ResolveProfileImageSourceRequired(profile.ImageSource),
            _ => throw new InvalidOperationException($"Unsupported entity insight image source scope '{profile.ImageSource.Scope}'."),
        };
    }

    public EntityInsightSemanticFieldRuntimeValue ResolveSemanticFieldValueRequired(
        World world,
        Entity entity,
        EntityInsightSemanticFieldProfile field,
        Entity? selectionPrimary = null,
        Entity? selectionViewer = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        RelationshipRuntime runtime = _relationshipRuntime
            ?? throw new InvalidOperationException("Entity insight relationship resolution requires a configured RelationshipRuntime.");
        RelationshipTypeRegistry typeRegistry = _relationshipTypes
            ?? throw new InvalidOperationException("Entity insight relationship resolution requires a configured RelationshipTypeRegistry.");

        Entity source = ResolveRelationshipSubjectRequired(world, entity, field.SourceSubject, selectionPrimary, selectionViewer);
        Entity target = ResolveRelationshipSubjectRequired(world, entity, field.TargetSubject, selectionPrimary, selectionViewer);
        int typeId = typeRegistry.GetId(field.RelationshipTypeId);

        return field.SemanticValueSource switch
        {
            EntityInsightSemanticValueSourceKind.RelationshipMetric => ResolveRelationshipMetricValueRequired(runtime, typeId, field, source, target),
            EntityInsightSemanticValueSourceKind.RelationshipFlag => ResolveRelationshipFlagValueRequired(runtime, typeId, field, source, target),
            _ => throw new InvalidOperationException($"Unsupported semantic value source '{field.SemanticValueSource}'."),
        };
    }

    private string ResolveEntityImageSourceRequired(
        World world,
        Entity entity,
        EntityInsightImageSourceProfile imageSource)
    {
        if (_imageBindingResolver == null)
        {
            throw new InvalidOperationException("Entity insight entity-scoped image resolution requires a configured PresentationImageBindingResolver.");
        }

        return _imageBindingResolver.ResolveRequiredSource(world, entity, imageSource.Role, imageSource.State);
    }

    private string ResolveProfileImageSourceRequired(EntityInsightImageSourceProfile imageSource)
    {
        if (_imageSourceResolver == null)
        {
            throw new InvalidOperationException("Entity insight profile-scoped image resolution requires a configured runtime adapter image source resolver.");
        }

        return _imageSourceResolver.ResolveRequiredSource(imageSource.ProfileImageAssetId);
    }

    private EntityInsightSemanticFieldRuntimeValue ResolveRelationshipMetricValueRequired(
        RelationshipRuntime runtime,
        int typeId,
        EntityInsightSemanticFieldProfile field,
        Entity source,
        Entity target)
    {
        RelationshipMetricRegistry metricRegistry = _relationshipMetrics
            ?? throw new InvalidOperationException("Entity insight relationship metric resolution requires a configured RelationshipMetricRegistry.");
        int metricId = metricRegistry.GetId(field.RelationshipMetricId);
        short value = runtime.GetMetric(source, target, typeId, metricId);
        return field.RenderKind switch
        {
            EntityInsightSemanticFieldRenderKind.Numeric => new EntityInsightSemanticFieldRuntimeValue(string.Empty, value),
            EntityInsightSemanticFieldRenderKind.Mapping => new EntityInsightSemanticFieldRuntimeValue(
                value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                value),
            _ => throw new InvalidOperationException($"Unsupported semantic field render kind '{field.RenderKind}'."),
        };
    }

    private EntityInsightSemanticFieldRuntimeValue ResolveRelationshipFlagValueRequired(
        RelationshipRuntime runtime,
        int typeId,
        EntityInsightSemanticFieldProfile field,
        Entity source,
        Entity target)
    {
        RelationshipFlagRegistry flagRegistry = _relationshipFlags
            ?? throw new InvalidOperationException("Entity insight relationship flag resolution requires a configured RelationshipFlagRegistry.");
        int flagId = flagRegistry.GetId(field.RelationshipFlagId);
        bool enabled = runtime.HasFlag(source, target, typeId, flagId);
        string valueKey = enabled ? field.TrueValueKey : field.FalseValueKey;
        return new EntityInsightSemanticFieldRuntimeValue(valueKey, enabled ? 1f : 0f);
    }

    private static Entity ResolveRelationshipSubjectRequired(
        World world,
        Entity self,
        EntityInsightRelationshipSubjectKind subject,
        Entity? selectionPrimary,
        Entity? selectionViewer)
    {
        Entity resolved = subject switch
        {
            EntityInsightRelationshipSubjectKind.Self => self,
            EntityInsightRelationshipSubjectKind.SelectionPrimary => selectionPrimary ?? Entity.Null,
            EntityInsightRelationshipSubjectKind.SelectionViewer => selectionViewer ?? Entity.Null,
            _ => Entity.Null,
        };

        if (resolved == Entity.Null || !world.IsAlive(resolved))
        {
            throw new InvalidOperationException($"Entity insight relationship subject '{subject}' could not be resolved to a live entity.");
        }

        return resolved;
    }
}
