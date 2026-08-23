using Ludots.Platform.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Numerics;
using Arch.Buffer;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Knowledge;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.EntityCollections;
using Ludots.Core.ParticipantVisibility;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Vision;
using RtsMultiplayerFrontlineMod.Systems;

namespace RtsMultiplayerFrontlineMod.Runtime;

internal enum FrontlineReplicationKind : byte
{
    Core = 0,
    Harvester = 1,
    Infantry = 2,
    CrystalNode = 3,
    MatchState = 4,
}

internal readonly record struct FrontlineReplicationSpec(
    FrontlineReplicationKind Kind,
    int SchemaId,
    bool HasHealth,
    bool HasCrystals,
    bool HasOwner)
{
    public long SupportedValidBits =>
        FrontlineReplicationPayload.PositionValid |
        (HasHealth ? FrontlineReplicationPayload.HealthValid : 0L) |
        (HasCrystals ? FrontlineReplicationPayload.CrystalsValid : 0L) |
        (HasOwner ? FrontlineReplicationPayload.TeamValid | FrontlineReplicationPayload.PlayerValid : 0L);
}

internal static class FrontlineReplicationPayload
{
    internal const long PositionValid = 1L << 0;
    internal const long HealthValid = 1L << 1;
    internal const long CrystalsValid = 1L << 2;
    internal const long TeamValid = 1L << 3;
    internal const long PlayerValid = 1L << 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Has(long validBits, long flag) => (validBits & flag) != 0;

    internal static long PackInts(int low, int high) =>
        (long)(uint)low | ((long)(uint)high << 32);

    internal static int UnpackLowInt(long packed) => unchecked((int)(uint)packed);

    internal static int UnpackHighInt(long packed) => unchecked((int)(uint)(packed >> 32));

    internal static long PackFloats(float low, float high) =>
        PackInts(BitConverter.SingleToInt32Bits(low), BitConverter.SingleToInt32Bits(high));

    internal static float UnpackLowFloat(long packed) =>
        BitConverter.Int32BitsToSingle(UnpackLowInt(packed));

    internal static float UnpackHighFloat(long packed) =>
        BitConverter.Int32BitsToSingle(UnpackHighInt(packed));

    internal static uint ComputeRevision(in ReplicationStateVector values, uint disclosureRevision)
    {
        uint hash = 2166136261u;
        Mix(ref hash, (ulong)values.Value0);
        Mix(ref hash, (ulong)values.Value1);
        Mix(ref hash, (ulong)values.Value2);
        Mix(ref hash, (ulong)values.Value3);
        hash = (hash ^ disclosureRevision) * 16777619u;
        return hash;
    }

    private static void Mix(ref uint hash, ulong value)
    {
        hash = (hash ^ (uint)value) * 16777619u;
        hash = (hash ^ (uint)(value >> 32)) * 16777619u;
    }
}

internal abstract class FrontlineReplicationProjector : IReplicationSchemaProjector
{
    private readonly FrontlineReplicationSpec _spec;
    private readonly int _healthAttributeId;
    private readonly int _crystalAttributeId;

    protected FrontlineReplicationProjector(
        in FrontlineReplicationSpec spec,
        int healthAttributeId,
        int crystalAttributeId)
    {
        _spec = spec;
        _healthAttributeId = healthAttributeId;
        _crystalAttributeId = crystalAttributeId;
    }

    public bool TryProject(
        World world,
        Entity entity,
        in KnowledgeDisclosureRecord disclosure,
        out ReplicationProjectedState state)
    {
        state = default;
        if (world == null ||
            !world.IsAlive(entity) ||
            disclosure.Presence != KnowledgePresence.LiveVisible ||
            !MatchesSchemaEntity(world, entity, in _spec) ||
            !world.TryGet(entity, out WorldPositionCm position))
        {
            return false;
        }

        AttributeBuffer attributes = default;
        if ((_spec.HasHealth || _spec.HasCrystals) &&
            (!world.TryGet(entity, out attributes) ||
             (_spec.HasHealth && !attributes.HasAttribute(_healthAttributeId)) ||
             (_spec.HasCrystals && !attributes.HasAttribute(_crystalAttributeId))))
        {
            return false;
        }

        Team team = default;
        PlayerOwner owner = default;
        if (_spec.HasOwner &&
            (!world.TryGet(entity, out team) ||
             !world.TryGet(entity, out owner) ||
             team.Id <= 0 ||
             owner.PlayerId <= 0))
        {
            return false;
        }

        long validBits = 0;
        long positionValue = 0;
        if (disclosure.Position == KnowledgePositionAccess.Live)
        {
            var positionCm = position.ToWorldCmInt2();
            positionValue = FrontlineReplicationPayload.PackInts(positionCm.X, positionCm.Y);
            validBits |= FrontlineReplicationPayload.PositionValid;
        }

        float health = 0f;
        if (_spec.HasHealth && disclosure.AttributeMask.ContainsId(_healthAttributeId))
        {
            health = attributes.GetCurrent(_healthAttributeId);
            if (!float.IsFinite(health))
            {
                return false;
            }
            validBits |= FrontlineReplicationPayload.HealthValid;
        }

        float crystals = 0f;
        if (_spec.HasCrystals && disclosure.AttributeMask.ContainsId(_crystalAttributeId))
        {
            crystals = attributes.GetCurrent(_crystalAttributeId);
            if (!float.IsFinite(crystals))
            {
                return false;
            }
            validBits |= FrontlineReplicationPayload.CrystalsValid;
        }

        long ownerValue = 0;
        if (_spec.HasOwner)
        {
            ownerValue = FrontlineReplicationPayload.PackInts(team.Id, owner.PlayerId);
            validBits |= FrontlineReplicationPayload.TeamValid | FrontlineReplicationPayload.PlayerValid;
        }

        var values = new ReplicationStateVector(
            positionValue,
            FrontlineReplicationPayload.PackFloats(health, crystals),
            ownerValue,
            validBits);
        state = new ReplicationProjectedState(
            FrontlineReplicationPayload.ComputeRevision(in values, disclosure.Revision),
            in values);
        return true;
    }

    internal static bool MatchesSchemaEntity(
        World world,
        Entity entity,
        in FrontlineReplicationSpec spec)
    {
        if (!world.TryGet(entity, out ReplicationSchemaRef schema) || schema.SchemaId != spec.SchemaId)
        {
            return false;
        }

        return spec.Kind switch
        {
            FrontlineReplicationKind.Core => world.Has<FrontlineCore>(entity),
            FrontlineReplicationKind.Harvester => world.Has<FrontlineHarvester>(entity),
            FrontlineReplicationKind.Infantry => world.Has<FrontlineInfantry>(entity),
            FrontlineReplicationKind.CrystalNode => world.Has<FrontlineCrystalNode>(entity),
            _ => false,
        };
    }
}

internal sealed class FrontlineCoreReplicationProjector : FrontlineReplicationProjector
{
    public FrontlineCoreReplicationProjector(in FrontlineReplicationSpec spec, int healthId, int crystalId)
        : base(in spec, healthId, crystalId) { }
}

internal sealed class FrontlineHarvesterReplicationProjector : FrontlineReplicationProjector
{
    public FrontlineHarvesterReplicationProjector(in FrontlineReplicationSpec spec, int healthId, int crystalId)
        : base(in spec, healthId, crystalId) { }
}

internal sealed class FrontlineInfantryReplicationProjector : FrontlineReplicationProjector
{
    public FrontlineInfantryReplicationProjector(in FrontlineReplicationSpec spec, int healthId, int crystalId)
        : base(in spec, healthId, crystalId) { }
}

internal sealed class FrontlineCrystalNodeReplicationProjector : FrontlineReplicationProjector
{
    public FrontlineCrystalNodeReplicationProjector(in FrontlineReplicationSpec spec, int healthId, int crystalId)
        : base(in spec, healthId, crystalId) { }
}

internal sealed class FrontlineClientTemplateFactory
{
    private const string ReplicationSchemaComponent = "ReplicationSchemaRef";
    private const string ReplicationSchemaIdProperty = "SchemaId";

    private readonly Dictionary<string, EntityTemplate> _templates = new(StringComparer.Ordinal);
    private readonly string[] _templateIds = new string[5];
    private readonly int[] _templateKeyIds = new int[5];
    private readonly string[] _entityContexts = new string[5];
    private readonly World _world;
    private readonly EntityBuilder _builder;
    private readonly PresentationStableIdAllocator _stableIds;

    public FrontlineClientTemplateFactory(
        World world,
        IEnumerable<EntityTemplate> templates,
        ReadOnlySpan<FrontlineReplicationSpec> specs,
        int matchStateSchemaId,
        EntityTemplateKeyRegistry templateKeys,
        ComponentAuthoringContext authoringContext,
        PresentationStableIdAllocator stableIds)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(templateKeys);
        ArgumentNullException.ThrowIfNull(authoringContext);
        if (ReferenceEquals(authoringContext, ComponentAuthoringContext.Empty))
        {
            throw new InvalidOperationException(
                "RTS Frontline client template factory requires the engine ComponentAuthoringContext; Empty is not allowed.");
        }
        _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
        ArgumentNullException.ThrowIfNull(templates);
        if (specs.Length != 4)
        {
            throw new InvalidOperationException("RTS Frontline requires exactly four replication schema specifications.");
        }
        if (matchStateSchemaId <= 0)
        {
            throw new InvalidOperationException("RTS Frontline requires a positive match-state replication schema id.");
        }

        foreach (EntityTemplate template in templates)
        {
            if (!TryReadSchemaId(template, out int schemaId))
            {
                continue;
            }

            int kindIndex;
            if (schemaId == matchStateSchemaId)
            {
                kindIndex = (int)FrontlineReplicationKind.MatchState;
                ValidateMatchStateTemplate(template);
            }
            else
            {
                int specIndex = FindSpec(specs, schemaId);
                if (specIndex < 0)
                {
                    continue;
                }

                FrontlineReplicationSpec spec = specs[specIndex];
                kindIndex = (int)spec.Kind;
                ValidateTemplate(template, in spec);
            }
            if (!string.IsNullOrEmpty(_templateIds[kindIndex]))
            {
                throw new InvalidOperationException(
                    $"RTS Frontline schema {schemaId} resolves to more than one entity template.");
            }

            _templates.Add(template.Id, template);
            _templateIds[kindIndex] = template.Id;
            int templateKeyId = templateKeys.GetId(template.Id);
            if (templateKeyId <= 0)
            {
                throw new InvalidOperationException(
                    $"RTS Frontline replicated template '{template.Id}' is missing from the formal template key registry.");
            }
            _templateKeyIds[kindIndex] = templateKeyId;
            _entityContexts[kindIndex] = $"RTS Frontline replicated {(FrontlineReplicationKind)kindIndex}";
        }

        for (int i = 0; i < _templateIds.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(_templateIds[i]))
            {
                throw new InvalidOperationException(
                    $"RTS Frontline replication kind {(FrontlineReplicationKind)i} has no formal entity template.");
            }
        }
        _builder = new EntityBuilder(_world, _templates, authoringContext);
    }

    public Entity Create(World world, FrontlineReplicationKind kind)
    {
        if (!ReferenceEquals(world, _world))
        {
            throw new InvalidOperationException("RTS Frontline client template factory cannot create into a different world.");
        }

        int index = (int)kind;
        if ((uint)index >= (uint)_templateIds.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Entity entity = _builder
            .UseTemplate(_templateIds[index])
            .WithEntityContext(_entityContexts[index])
            .Build();
        world.Add(entity, new EntityTemplateKeyRef { TemplateKeyId = _templateKeyIds[index] });
        if (kind != FrontlineReplicationKind.MatchState)
        {
            if (world.Has<PresentationStableId>(entity))
            {
                throw new InvalidOperationException("RTS Frontline replicated templates must not author shared presentation stable ids.");
            }

            world.Add(entity, new PresentationStableId { Value = _stableIds.Allocate() });
        }

        return entity;
    }

    private static int FindSpec(ReadOnlySpan<FrontlineReplicationSpec> specs, int schemaId)
    {
        for (int i = 0; i < specs.Length; i++)
        {
            if (specs[i].SchemaId == schemaId)
            {
                return i;
            }
        }
        return -1;
    }

    private static bool TryReadSchemaId(EntityTemplate template, out int schemaId)
    {
        schemaId = 0;
        if (template == null ||
            string.IsNullOrWhiteSpace(template.Id) ||
            !template.Components.TryGetValue(ReplicationSchemaComponent, out JsonNode? schemaNode) ||
            schemaNode is not JsonObject schemaObject ||
            schemaObject[ReplicationSchemaIdProperty] is not JsonValue schemaValue ||
            !schemaValue.TryGetValue(out schemaId))
        {
            return false;
        }

        if (schemaId <= 0)
        {
            throw new InvalidOperationException(
                $"Entity template '{template.Id}' declares a non-positive replication schema id.");
        }
        return true;
    }

    private static void ValidateTemplate(EntityTemplate template, in FrontlineReplicationSpec spec)
    {
        RequireComponent(template, "WorldPositionCm");
        ForbidSpatialExclusionComponents(template);
        ForbidComponent(template, "SpatialCellRef");
        RequireComponent(template, "VisualTransform");
        RequireComponent(template, "CullState");
        RequireComponent(template, "CommandSourceSelectableTag");
        RequireComponent(template, "CommandSourceSelectableState");
        RequireBox3DPlayerClickBounds(template);
        RequireComponent(template, ReplicationSchemaComponent);
        if (spec.HasHealth || spec.HasCrystals)
        {
            RequireComponent(template, "AttributeBuffer");
        }
        if (spec.HasOwner)
        {
            RequireComponent(template, "Team");
            RequireComponent(template, "PlayerOwner");
            RequireComponent(template, "FrontlineParticipant");
            RequireComponent(template, "VisionEmitterCm");
        }

        RequireComponent(template, spec.Kind switch
        {
            FrontlineReplicationKind.Core => "FrontlineCore",
            FrontlineReplicationKind.Harvester => "FrontlineHarvester",
            FrontlineReplicationKind.Infantry => "FrontlineInfantry",
            FrontlineReplicationKind.CrystalNode => "FrontlineCrystalNode",
            _ => throw new InvalidOperationException("Unknown RTS Frontline replication kind."),
        });
    }

    private static void ValidateMatchStateTemplate(EntityTemplate template)
    {
        RequireComponent(template, ReplicationSchemaComponent);
        RequireComponent(template, "FrontlineMatchStateEntity");
        RequireComponent(template, "FrontlineMatchStateProjection");
        ForbidComponent(template, "WorldPositionCm");
        ForbidComponent(template, "SpatialCellRef");
    }

    private static void ForbidSpatialExclusionComponents(EntityTemplate template)
    {
        ForbidComponent(template, "PresentationStaticTransform");
        ForbidComponent(template, "SpatialPartitionExcluded");
        ForbidComponent(template, "PresentationDestroyPending");
        ForbidComponent(template, "SuspendedTag");
    }

    private static void RequireComponent(EntityTemplate template, string componentName)
    {
        if (!template.Components.ContainsKey(componentName))
        {
            throw new InvalidOperationException(
                $"RTS Frontline replicated template '{template.Id}' requires component '{componentName}'.");
        }
    }

    private static void RequireBox3DPlayerClickBounds(EntityTemplate template)
    {
        RequireComponent(template, "SpatialBounds");
        RequireComponent(template, "SpatialBox3D");
        if (template.Components["SpatialBounds"] is not JsonObject bounds ||
            bounds["kind"] is not JsonValue kindValue ||
            !kindValue.TryGetValue(out string? kind) ||
            !string.Equals(kind, nameof(SpatialBoundsKind.Box3D), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"RTS Frontline replicated template '{template.Id}' requires SpatialBounds kind Box3D.");
        }
        if (template.Components["SpatialBox3D"] is not JsonObject box)
        {
            throw new InvalidOperationException(
                $"RTS Frontline replicated template '{template.Id}' requires a SpatialBox3D object.");
        }

        RequirePositiveHalfSize(template, box, "halfSizeXCm");
        RequirePositiveHalfSize(template, box, "halfSizeYCm");
        RequirePositiveHalfSize(template, box, "halfSizeZCm");
    }

    private static void RequirePositiveHalfSize(EntityTemplate template, JsonObject box, string propertyName)
    {
        if (box[propertyName] is not JsonValue value ||
            !value.TryGetValue(out int halfSizeCm) ||
            halfSizeCm <= 0)
        {
            throw new InvalidOperationException(
                $"RTS Frontline replicated template '{template.Id}' requires a positive SpatialBox3D.{propertyName}.");
        }
    }

    private static void ForbidComponent(EntityTemplate template, string componentName)
    {
        if (template.Components.ContainsKey(componentName))
        {
            throw new InvalidOperationException(
                $"RTS Frontline replicated template '{template.Id}' must not declare component '{componentName}'.");
        }
    }
}

internal abstract class FrontlineReplicationApplier : IClientReplicationSchemaApplier
{
    private readonly FrontlineReplicationSpec _spec;
    private readonly FrontlineClientTemplateFactory _templates;
    private readonly FrontlineSideConfig[] _sides;
    private readonly int _healthAttributeId;
    private readonly int _crystalAttributeId;
    private readonly FrontlineTagBinder _tagBinder;
    private readonly OwnershipResolver _ownership;
    private readonly PlayerEntityLookup _players;
    private readonly int[] _sideVisionScopeKeyIds;

    protected FrontlineReplicationApplier(
        in FrontlineReplicationSpec spec,
        FrontlineClientTemplateFactory templates,
        FrontlineSideConfig[] sides,
        int[] sideVisionScopeKeyIds,
        int healthAttributeId,
        int crystalAttributeId,
        FrontlineTagBinder tagBinder,
        OwnershipResolver ownership,
        PlayerEntityLookup players)
    {
        _spec = spec;
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _sides = sides ?? throw new ArgumentNullException(nameof(sides));
        _sideVisionScopeKeyIds = sideVisionScopeKeyIds ?? throw new ArgumentNullException(nameof(sideVisionScopeKeyIds));
        _healthAttributeId = healthAttributeId;
        _crystalAttributeId = crystalAttributeId;
        _tagBinder = tagBinder ?? throw new ArgumentNullException(nameof(tagBinder));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _players = players ?? throw new ArgumentNullException(nameof(players));
    }

    public bool CanCreate(World world, in ReplicatedEntityState state) =>
        world != null &&
        ValidatePayload(state.Values, state.SchemaId) &&
        CanResolveOwner(world, state.Values);

    public bool CanApply(World world, Entity entity, in ReplicatedEntityState state) =>
        world != null &&
        world.IsAlive(entity) &&
        ValidatePayload(state.Values, state.SchemaId) &&
        CanResolveOwner(world, state.Values) &&
        MatchesFormalEntity(world, entity);

    public bool CanConceal(World world, Entity entity) =>
        world != null && world.IsAlive(entity) && MatchesFormalEntity(world, entity);

    public bool TryPreviewSpatialMembership(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        out SpatialMembershipTarget target)
    {
        target = default;
        if (world == null || !ValidatePayload(state.Values, state.SchemaId))
        {
            return false;
        }

        WorldPositionCm position = DecodePosition(state.Values);
        WorldCmInt2 positionCm = position.Value.ToWorldCmInt2();
        target = SpatialMembershipTarget.At(in positionCm);
        return true;
    }

    public Entity Create(
        World world,
        in ReplicationMirrorIdentity identity,
        in ReplicationMirrorState state)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }
        if (!ValidatePayload(state.Values, state.SchemaId) ||
            !CanResolveOwner(world, state.Values))
        {
            throw new InvalidOperationException(
                $"RTS Frontline schema {_spec.SchemaId} rejected replicated create payload.");
        }

        Entity entity = _templates.Create(world, _spec.Kind);
        try
        {
            if (!MatchesFormalEntity(world, entity))
            {
                bool hasSchema = world.TryGet(entity, out ReplicationSchemaRef authoredSchema);
                throw new InvalidOperationException(
                    $"RTS Frontline schema {_spec.SchemaId} formal template violated its component contract: " +
                    $"schemaRef={hasSchema}, schemaId={(hasSchema ? authoredSchema.SchemaId : 0)}, " +
                    $"kind={MatchesKindComponent(world, entity)}, " +
                    $"position={world.Has<WorldPositionCm>(entity)}, " +
                    $"attributes={world.Has<AttributeBuffer>(entity)}, " +
                    $"team={world.Has<Team>(entity)}, owner={world.Has<PlayerOwner>(entity)}, " +
                    $"participant={world.Has<FrontlineParticipant>(entity)}, vision={world.Has<VisionEmitterCm>(entity)}.");
            }

            world.Add(entity, in identity, in state);
            ApplyPayload(world, entity, state.Values, isCreate: true);
            return entity;
        }
        catch
        {
            if (world.IsAlive(entity))
            {
                world.Destroy(entity);
            }
            throw;
        }
    }

    public void Apply(World world, Entity entity, in ReplicatedEntityState state)
    {
        if (!CanApply(world, entity, in state))
        {
            throw new InvalidOperationException(
                $"RTS Frontline schema {_spec.SchemaId} rejected replicated update payload.");
        }
        ApplyPayload(world, entity, state.Values, isCreate: false);
    }

    public void Conceal(World world, Entity entity)
    {
        if (!CanConceal(world, entity))
        {
            throw new InvalidOperationException(
                $"RTS Frontline schema {_spec.SchemaId} cannot conceal an entity outside its formal template contract.");
        }
        ApplyPayload(world, entity, default, isCreate: false);
        SetDisclosureVisibility(world, entity, isVisible: false);
    }

    private bool ValidatePayload(in ReplicationStateVector values, int schemaId)
    {
        if (schemaId != _spec.SchemaId ||
            (values.Value3 & ~_spec.SupportedValidBits) != 0)
        {
            return false;
        }

        long valid = values.Value3;
        if (!FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.PositionValid) && values.Value0 != 0)
        {
            return false;
        }

        bool healthValid = FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.HealthValid);
        bool crystalsValid = FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.CrystalsValid);
        if ((!healthValid && FrontlineReplicationPayload.UnpackLowInt(values.Value1) != 0) ||
            (!crystalsValid && FrontlineReplicationPayload.UnpackHighInt(values.Value1) != 0) ||
            (healthValid && !float.IsFinite(FrontlineReplicationPayload.UnpackLowFloat(values.Value1))) ||
            (crystalsValid && !float.IsFinite(FrontlineReplicationPayload.UnpackHighFloat(values.Value1))))
        {
            return false;
        }

        bool teamValid = FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.TeamValid);
        bool playerValid = FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.PlayerValid);
        if (teamValid != playerValid ||
            _spec.HasOwner != teamValid ||
            (!teamValid && values.Value2 != 0))
        {
            return false;
        }

        return !_spec.HasOwner || TryResolveSide(
            FrontlineReplicationPayload.UnpackLowInt(values.Value2),
            FrontlineReplicationPayload.UnpackHighInt(values.Value2),
            out _);
    }

    private bool CanResolveOwner(World world, ReplicationStateVector values)
    {
        if (!_spec.HasOwner)
        {
            return true;
        }

        int playerId = FrontlineReplicationPayload.UnpackHighInt(values.Value2);
        return _players.TryGet(playerId, out Entity representative) &&
            representative != Entity.Null &&
            world.IsAlive(representative) &&
            world.TryGet(representative, out PlayerIdentity identity) &&
            identity.PlayerId == playerId;
    }

    private bool MatchesFormalEntity(World world, Entity entity)
    {
        if (!FrontlineReplicationProjector.MatchesSchemaEntity(world, entity, in _spec) ||
            !world.Has<WorldPositionCm>(entity) ||
            !world.Has<PreviousWorldPositionCm>(entity) ||
            !world.Has<VisualTransform>(entity) ||
            !world.Has<CullState>(entity) ||
            !world.Has<CommandSourceSelectableTag>(entity) ||
            !world.Has<CommandSourceSelectableState>(entity) ||
            !world.TryGet(entity, out SpatialBounds spatialBounds) ||
            spatialBounds.Kind != SpatialBoundsKind.Box3D ||
            !world.Has<SpatialBox3D>(entity) ||
            ((_spec.HasHealth || _spec.HasCrystals) && !world.Has<AttributeBuffer>(entity)) ||
            (_spec.HasOwner &&
             (!world.Has<Team>(entity) ||
              !world.Has<PlayerOwner>(entity) ||
              !world.Has<FrontlineParticipant>(entity) ||
              !world.Has<VisionEmitterCm>(entity))))
        {
            return false;
        }
        return true;
    }

    private bool MatchesKindComponent(World world, Entity entity) => _spec.Kind switch
    {
        FrontlineReplicationKind.Core => world.Has<FrontlineCore>(entity),
        FrontlineReplicationKind.Harvester => world.Has<FrontlineHarvester>(entity),
        FrontlineReplicationKind.Infantry => world.Has<FrontlineInfantry>(entity),
        FrontlineReplicationKind.CrystalNode => world.Has<FrontlineCrystalNode>(entity),
        _ => false,
    };

    private void ApplyPayload(
        World world,
        Entity entity,
        in ReplicationStateVector values,
        bool isCreate)
    {
        SetDisclosureVisibility(world, entity, isVisible: true);
        long valid = values.Value3;
        WorldPositionCm position = DecodePosition(values);
        WorldPositionCm previous = world.Get<WorldPositionCm>(entity);
        world.Set(entity, in position);
        var previousPosition = new PreviousWorldPositionCm
        {
            Value = isCreate ? position.Value : previous.Value,
        };
        world.Set(entity, in previousPosition);
        ref VisualTransform visual = ref world.Get<VisualTransform>(entity);
        visual.Position = new Vector3(
            position.Value.X.ToFloat() * 0.01f,
            visual.Position.Y,
            position.Value.Y.ToFloat() * 0.01f);

        if (_spec.HasHealth || _spec.HasCrystals)
        {
            if (_spec.HasHealth)
            {
                float health = FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.HealthValid)
                    ? FrontlineReplicationPayload.UnpackLowFloat(values.Value1)
                    : 0f;
                AttributeMutationOps.SetCurrent(world, entity, _healthAttributeId, health, _tagBinder.TagOps);
            }
            if (_spec.HasCrystals)
            {
                float crystals = FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.CrystalsValid)
                    ? FrontlineReplicationPayload.UnpackHighFloat(values.Value1)
                    : 0f;
                AttributeMutationOps.SetCurrent(world, entity, _crystalAttributeId, crystals, _tagBinder.TagOps);
            }
        }

        if (!_spec.HasOwner)
        {
            _tagBinder.BindReplicatedEntity(world, entity);
            return;
        }

        bool hasOwner = FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.TeamValid) &&
            FrontlineReplicationPayload.Has(valid, FrontlineReplicationPayload.PlayerValid);
        int teamId = hasOwner ? FrontlineReplicationPayload.UnpackLowInt(values.Value2) : 0;
        int playerId = hasOwner ? FrontlineReplicationPayload.UnpackHighInt(values.Value2) : 0;
        int sideIndex = hasOwner && TryResolveSide(teamId, playerId, out int resolvedSide) ? resolvedSide : -1;

        var team = new Team { Id = teamId };
        var owner = new PlayerOwner { PlayerId = playerId };
        var participant = new FrontlineParticipant { SideIndex = sideIndex };
        world.Set(entity, in team);
        world.Set(entity, in owner);
        world.Set(entity, in participant);

        ref VisionEmitterCm emitter = ref world.Get<VisionEmitterCm>(entity);
        emitter.ScopeKeyId = sideIndex >= 0 ? _sideVisionScopeKeyIds[sideIndex] : 0;
        if (hasOwner)
        {
            if (!OwnershipEdgeBuilder.TryLinkSpawnedEntity(world, _ownership, _players, entity))
            {
                throw new InvalidOperationException(
                    $"RTS Frontline replicated entity could not bind PlayerOwner {playerId} to a live formal player representative.");
            }
        }
        else
        {
            _ownership.ClearOwnership(entity);
        }
        _tagBinder.BindReplicatedEntity(world, entity);
    }

    private static WorldPositionCm DecodePosition(in ReplicationStateVector values) =>
        FrontlineReplicationPayload.Has(values.Value3, FrontlineReplicationPayload.PositionValid)
            ? WorldPositionCm.FromCm(
                FrontlineReplicationPayload.UnpackLowInt(values.Value0),
                FrontlineReplicationPayload.UnpackHighInt(values.Value0))
            : WorldPositionCm.FromCm(0, 0);

    private static void SetDisclosureVisibility(World world, Entity entity, bool isVisible)
    {
        ref CullState cull = ref world.Get<CullState>(entity);
        cull.IsVisible = isVisible;
        ref CommandSourceSelectableState selectable = ref world.Get<CommandSourceSelectableState>(entity);
        selectable.IsEnabled = isVisible ? (byte)1 : (byte)0;
    }

    private bool TryResolveSide(int teamId, int playerId, out int sideIndex)
    {
        for (int i = 0; i < _sides.Length; i++)
        {
            if (_sides[i].TeamId == teamId && _sides[i].PlayerId == playerId)
            {
                sideIndex = i;
                return true;
            }
        }
        sideIndex = -1;
        return false;
    }
}

internal sealed class FrontlineCoreReplicationApplier : FrontlineReplicationApplier
{
    public FrontlineCoreReplicationApplier(in FrontlineReplicationSpec spec, FrontlineClientTemplateFactory templates, FrontlineSideConfig[] sides, int[] sideVisionScopeKeyIds, int healthId, int crystalId, FrontlineTagBinder tagBinder, OwnershipResolver ownership, PlayerEntityLookup players)
        : base(in spec, templates, sides, sideVisionScopeKeyIds, healthId, crystalId, tagBinder, ownership, players) { }
}

internal sealed class FrontlineHarvesterReplicationApplier : FrontlineReplicationApplier
{
    public FrontlineHarvesterReplicationApplier(in FrontlineReplicationSpec spec, FrontlineClientTemplateFactory templates, FrontlineSideConfig[] sides, int[] sideVisionScopeKeyIds, int healthId, int crystalId, FrontlineTagBinder tagBinder, OwnershipResolver ownership, PlayerEntityLookup players)
        : base(in spec, templates, sides, sideVisionScopeKeyIds, healthId, crystalId, tagBinder, ownership, players) { }
}

internal sealed class FrontlineInfantryReplicationApplier : FrontlineReplicationApplier
{
    public FrontlineInfantryReplicationApplier(in FrontlineReplicationSpec spec, FrontlineClientTemplateFactory templates, FrontlineSideConfig[] sides, int[] sideVisionScopeKeyIds, int healthId, int crystalId, FrontlineTagBinder tagBinder, OwnershipResolver ownership, PlayerEntityLookup players)
        : base(in spec, templates, sides, sideVisionScopeKeyIds, healthId, crystalId, tagBinder, ownership, players) { }
}

internal sealed class FrontlineCrystalNodeReplicationApplier : FrontlineReplicationApplier
{
    public FrontlineCrystalNodeReplicationApplier(in FrontlineReplicationSpec spec, FrontlineClientTemplateFactory templates, FrontlineSideConfig[] sides, int[] sideVisionScopeKeyIds, int healthId, int crystalId, FrontlineTagBinder tagBinder, OwnershipResolver ownership, PlayerEntityLookup players)
        : base(in spec, templates, sides, sideVisionScopeKeyIds, healthId, crystalId, tagBinder, ownership, players) { }
}

internal static class FrontlineMatchStatePayload
{
    internal const long Version = 1;

    internal static ReplicationStateVector Encode(in FrontlineMatchSnapshot snapshot)
    {
        int lobbyFlags =
            (snapshot.SideOneReady ? 1 : 0) |
            (snapshot.SideTwoReady ? 1 << 1 : 0) |
            (snapshot.SideOneConnected ? 1 << 2 : 0) |
            (snapshot.SideTwoConnected ? 1 << 3 : 0);
        return new ReplicationStateVector(
            FrontlineReplicationPayload.PackInts((int)snapshot.Phase, snapshot.CountdownRemainingTicks),
            FrontlineReplicationPayload.PackInts(snapshot.CommittedTick, (int)snapshot.Outcome),
            FrontlineReplicationPayload.PackInts(snapshot.WinningSideIndex, lobbyFlags),
            Version);
    }

    internal static bool TryDecode(
        in ReplicationStateVector values,
        int readyCountdownTicks,
        int maxCommittedTick,
        out FrontlineMatchStateProjection projection)
    {
        projection = default;
        int phaseValue = FrontlineReplicationPayload.UnpackLowInt(values.Value0);
        int countdownTicks = FrontlineReplicationPayload.UnpackHighInt(values.Value0);
        int committedTick = FrontlineReplicationPayload.UnpackLowInt(values.Value1);
        int outcomeValue = FrontlineReplicationPayload.UnpackHighInt(values.Value1);
        int winningSideIndex = FrontlineReplicationPayload.UnpackLowInt(values.Value2);
        int lobbyFlags = FrontlineReplicationPayload.UnpackHighInt(values.Value2);
        if (values.Value3 != Version ||
            (uint)phaseValue > (uint)FrontlineMatchPhase.Completed ||
            (uint)outcomeValue > (uint)FrontlineMatchOutcome.Draw ||
            countdownTicks < 0 || countdownTicks > readyCountdownTicks ||
            committedTick < 0 || committedTick > maxCommittedTick ||
            (lobbyFlags & ~0x0f) != 0)
        {
            return false;
        }

        var phase = (FrontlineMatchPhase)phaseValue;
        var outcome = (FrontlineMatchOutcome)outcomeValue;
        bool sideOneReady = (lobbyFlags & 1) != 0;
        bool sideTwoReady = (lobbyFlags & (1 << 1)) != 0;
        bool sideOneConnected = (lobbyFlags & (1 << 2)) != 0;
        bool sideTwoConnected = (lobbyFlags & (1 << 3)) != 0;
        if ((!sideOneConnected && sideOneReady) ||
            (!sideTwoConnected && sideTwoReady) ||
            (phase != FrontlineMatchPhase.Countdown && countdownTicks != 0) ||
            (phase == FrontlineMatchPhase.Countdown &&
             (!sideOneReady || !sideTwoReady || !sideOneConnected || !sideTwoConnected)) ||
            (outcome == FrontlineMatchOutcome.InProgress &&
             (phase == FrontlineMatchPhase.Completed || winningSideIndex != -1)) ||
            (outcome != FrontlineMatchOutcome.InProgress && phase != FrontlineMatchPhase.Completed) ||
            (outcome == FrontlineMatchOutcome.SideOneVictory && winningSideIndex != 0) ||
            (outcome == FrontlineMatchOutcome.SideTwoVictory && winningSideIndex != 1) ||
            (outcome == FrontlineMatchOutcome.Draw && winningSideIndex != -1))
        {
            return false;
        }

        projection = new FrontlineMatchStateProjection
        {
            CommittedTick = committedTick,
            Phase = phase,
            CountdownRemainingTicks = countdownTicks,
            Outcome = outcome,
            WinningSideIndex = winningSideIndex,
            SideOneReady = sideOneReady ? (byte)1 : (byte)0,
            SideTwoReady = sideTwoReady ? (byte)1 : (byte)0,
            SideOneConnected = sideOneConnected ? (byte)1 : (byte)0,
            SideTwoConnected = sideTwoConnected ? (byte)1 : (byte)0,
        };
        return true;
    }
}

internal sealed class FrontlineMatchStateReplicationProjector : IReplicationSchemaProjector
{
    private readonly FrontlineRuntime _runtime;
    private readonly int _schemaId;

    public FrontlineMatchStateReplicationProjector(FrontlineRuntime runtime, int schemaId)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _schemaId = schemaId > 0 ? schemaId : throw new ArgumentOutOfRangeException(nameof(schemaId));
    }

    public bool TryProject(
        World world,
        Entity entity,
        in KnowledgeDisclosureRecord disclosure,
        out ReplicationProjectedState state)
    {
        state = default;
        if (world == null ||
            !world.IsAlive(entity) ||
            disclosure.Presence != KnowledgePresence.LiveVisible ||
            !world.Has<FrontlineMatchStateEntity>(entity) ||
            !world.TryGet(entity, out ReplicationSchemaRef schema) ||
            schema.SchemaId != _schemaId)
        {
            return false;
        }

        FrontlineMatchSnapshot snapshot = _runtime.Snapshot;
        ReplicationStateVector values = FrontlineMatchStatePayload.Encode(in snapshot);
        state = new ReplicationProjectedState(
            FrontlineReplicationPayload.ComputeRevision(in values, disclosure.Revision),
            in values);
        return true;
    }
}

internal sealed class FrontlineMatchStateReplicationApplier : IClientReplicationSchemaApplier
{
    private readonly int _schemaId;
    private readonly int _readyCountdownTicks;
    private readonly int _maxCommittedTick;
    private readonly FrontlineClientTemplateFactory _templates;

    public FrontlineMatchStateReplicationApplier(
        int schemaId,
        int readyCountdownTicks,
        int matchDurationTicks,
        int disconnectGraceTicks,
        FrontlineClientTemplateFactory templates)
    {
        _schemaId = schemaId > 0 ? schemaId : throw new ArgumentOutOfRangeException(nameof(schemaId));
        _readyCountdownTicks = readyCountdownTicks > 0
            ? readyCountdownTicks
            : throw new ArgumentOutOfRangeException(nameof(readyCountdownTicks));
        if (matchDurationTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(matchDurationTicks));
        }
        if (disconnectGraceTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(disconnectGraceTicks));
        }
        _maxCommittedTick = checked(matchDurationTicks + disconnectGraceTicks);
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    public bool CanCreate(World world, in ReplicatedEntityState state) =>
        world != null && Validate(state.SchemaId, state.Values, out _);

    public bool CanApply(World world, Entity entity, in ReplicatedEntityState state) =>
        world != null &&
        world.IsAlive(entity) &&
        MatchesFormalEntity(world, entity) &&
        Validate(state.SchemaId, state.Values, out _);

    public bool CanConceal(World world, Entity entity) =>
        world != null && world.IsAlive(entity) && MatchesFormalEntity(world, entity);

    public bool TryPreviewSpatialMembership(
        World world,
        Entity entity,
        in ReplicatedEntityState state,
        out SpatialMembershipTarget target)
    {
        target = SpatialMembershipTarget.NoMembership;
        return world != null && Validate(state.SchemaId, state.Values, out _);
    }

    public Entity Create(
        World world,
        in ReplicationMirrorIdentity identity,
        in ReplicationMirrorState state)
    {
        if (world == null)
        {
            throw new ArgumentNullException(nameof(world));
        }
        if (!Validate(state.SchemaId, state.Values, out FrontlineMatchStateProjection projection))
        {
            throw new InvalidOperationException("RTS Frontline rejected a replicated match-state create payload.");
        }

        Entity entity = _templates.Create(world, FrontlineReplicationKind.MatchState);
        try
        {
            if (!MatchesFormalEntity(world, entity))
            {
                throw new InvalidOperationException("RTS Frontline match-state template violated its formal component contract.");
            }
            world.Add(entity, in identity, in state);
            world.Set(entity, in projection);
            return entity;
        }
        catch
        {
            if (world.IsAlive(entity))
            {
                world.Destroy(entity);
            }
            throw;
        }
    }

    public void Apply(World world, Entity entity, in ReplicatedEntityState state)
    {
        if (!CanApply(world, entity, in state) ||
            !Validate(state.SchemaId, state.Values, out FrontlineMatchStateProjection projection))
        {
            throw new InvalidOperationException("RTS Frontline rejected a replicated match-state update payload.");
        }
        world.Set(entity, in projection);
    }

    public void Conceal(World world, Entity entity)
    {
        if (!CanConceal(world, entity))
        {
            throw new InvalidOperationException("RTS Frontline cannot conceal an invalid match-state mirror.");
        }
        var projection = default(FrontlineMatchStateProjection);
        projection.WinningSideIndex = -1;
        world.Set(entity, in projection);
    }

    private bool Validate(
        int schemaId,
        in ReplicationStateVector values,
        out FrontlineMatchStateProjection projection)
    {
        projection = default;
        return schemaId == _schemaId &&
            FrontlineMatchStatePayload.TryDecode(
                in values,
                _readyCountdownTicks,
                _maxCommittedTick,
                out projection);
    }

    private bool MatchesFormalEntity(World world, Entity entity) =>
        world.Has<FrontlineMatchStateEntity, FrontlineMatchStateProjection>(entity) &&
        world.TryGet(entity, out ReplicationSchemaRef schema) &&
        schema.SchemaId == _schemaId;
}

internal sealed class FrontlineNetworkEntityBindingSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription ReplicatedQuery = new QueryDescription()
        .WithAll<ReplicationSchemaRef>();
    private static readonly QueryDescription PendingDestroyQuery = new QueryDescription()
        .WithAll<ReplicationSchemaRef, PresentationDestroyPending>();

    private readonly NetworkEntityTable _entities;
    private readonly GameEngine _engine;
    private readonly FrontlineReplicationEntityScope _scope;
    private readonly CommandBuffer _commandBuffer = new();

    public FrontlineNetworkEntityBindingSystem(
        GameEngine engine,
        NetworkEntityTable entities,
        FrontlineConfig config)
        : base((engine ?? throw new ArgumentNullException(nameof(engine))).World)
    {
        _engine = engine;
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _scope = new FrontlineReplicationEntityScope(config ?? throw new ArgumentNullException(nameof(config)));
    }

    public override void Update(in float dt)
    {
        MapSession? session = _engine.CurrentMapSession;
        if (session == null || !_scope.IsConfiguredMap(session.MapId))
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in ReplicatedQuery))
        {
            ReadOnlySpan<ReplicationSchemaRef> schemas = chunk.GetSpan<ReplicationSchemaRef>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref first, index);
                if (!_scope.IsFrontlineEntity(World, entity, schemas[index].SchemaId, session.MapId))
                {
                    continue;
                }
                if (schemas[index].SchemaId <= 0)
                {
                    throw new InvalidOperationException("RTS Frontline found a replicated entity with a non-positive schema id.");
                }
                if (!_entities.TryResolve(entity, out _) && !_entities.TryAllocate(entity, out _))
                {
                    throw new InvalidOperationException(
                        $"RTS Frontline could not allocate a network entity handle for schema {schemas[index].SchemaId}; " +
                        $"capacity {_entities.Capacity} is exhausted or the reverse index is inconsistent.");
                }
            }
        }

        foreach (ref Chunk chunk in World.Query(in PendingDestroyQuery))
        {
            ReadOnlySpan<ReplicationSchemaRef> schemas = chunk.GetSpan<ReplicationSchemaRef>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref first, index);
                if (!_scope.IsFrontlineEntity(World, entity, schemas[index].SchemaId, session.MapId))
                {
                    continue;
                }
                if (!_entities.TryResolve(entity, out NetworkEntityHandle handle) ||
                    !_entities.TryRelease(handle))
                {
                    throw new InvalidOperationException(
                        "RTS Frontline could not release a pending replicated entity from the authoritative network table.");
                }
                _commandBuffer.Destroy(in entity);
            }
        }

        if (_commandBuffer.Size > 0)
        {
            _commandBuffer.Playback(World);
        }
    }

    public override void Dispose()
    {
        _commandBuffer.Dispose();
        base.Dispose();
    }
}

internal static class FrontlineVisionScopes
{
    public static int Resolve(GameEngine engine, string scopeKey)
    {
        var registry = engine.GetService(CoreServiceKeys.ScopeKeyRegistry)
            ?? throw new InvalidOperationException(
                $"RTS Frontline vision scope '{scopeKey}' requires the ScopeKeyRegistry service.");
        if (!registry.TryGetId(scopeKey, out int id) || id <= 0)
        {
            throw new InvalidOperationException(
                $"RTS Frontline vision scope '{scopeKey}' is not declared in Progression/scopes.json.");
        }

            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ludots-scope-resolve.txt"),
                "resolve " + scopeKey + " -> " + id + System.Environment.NewLine);
        return id;
    }
}

internal sealed class FrontlineVisionScopeAuthoringSystem : BaseSystem<World, float>
{
    private static readonly QueryDescription Query = new QueryDescription()
        .WithAll<FrontlineParticipant, Team, PlayerOwner, VisionEmitterCm, ReplicationSchemaRef>()
        .WithNone<ReplicationMirrorIdentity>();

    private readonly GameEngine _engine;
    private readonly FrontlineSideConfig[] _sides;
    private readonly FrontlineReplicationEntityScope _scope;

    public FrontlineVisionScopeAuthoringSystem(GameEngine engine, FrontlineConfig config)
        : base((engine ?? throw new ArgumentNullException(nameof(engine))).World)
    {
        ArgumentNullException.ThrowIfNull(config);
        _engine = engine;
        _sides = config.Sides;
        _scope = new FrontlineReplicationEntityScope(config);
    }

    public override void Update(in float dt)
    {
        MapSession? session = _engine.CurrentMapSession;
        if (session == null || !_scope.IsConfiguredMap(session.MapId))
        {
            return;
        }

        foreach (ref Chunk chunk in World.Query(in Query))
        {
            ReadOnlySpan<FrontlineParticipant> participants = chunk.GetSpan<FrontlineParticipant>();
            ReadOnlySpan<Team> teams = chunk.GetSpan<Team>();
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ReadOnlySpan<ReplicationSchemaRef> schemas = chunk.GetSpan<ReplicationSchemaRef>();
            Span<VisionEmitterCm> emitters = chunk.GetSpan<VisionEmitterCm>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref first, index);
                if (!_scope.IsFrontlineEntity(World, entity, schemas[index].SchemaId, session.MapId))
                {
                    continue;
                }
                int sideIndex = participants[index].SideIndex;
                if ((uint)sideIndex >= (uint)_sides.Length ||
                    teams[index].Id != _sides[sideIndex].TeamId ||
                    owners[index].PlayerId != _sides[sideIndex].PlayerId)
                {
                    throw new InvalidOperationException(
                        "RTS Frontline participant ownership does not match its data-authored side configuration.");
                }

                emitters[index].ScopeKeyId = FrontlineVisionScopes.Resolve(_engine, _sides[sideIndex].VisionScopeKey);
            }
        }
    }
}

internal static class FrontlineReplicatedClientMapBoundary
{
    private static readonly QueryDescription AuthoredGameplayQuery = new QueryDescription()
        .WithAll<ReplicationSchemaRef>()
        .WithNone<ReplicationMirrorIdentity>();

    internal static int RemoveAuthoredGameplayEntities(GameEngine engine, FrontlineConfig config)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(config);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("RTS Frontline replicated client requires a focused map session.");
        if (!string.Equals(session.MapId.Value, config.MapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"RTS Frontline replicated client cleanup expected map '{config.MapId}' but focused '{session.MapId.Value}'.");
        }

        ValidateRepresentatives(engine.World, session, config.Sides);
        var scope = new FrontlineReplicationEntityScope(config);

        int removed = 0;
        using (var commands = new CommandBuffer())
        {
            foreach (ref Chunk chunk in engine.World.Query(in AuthoredGameplayQuery))
            {
                ReadOnlySpan<ReplicationSchemaRef> schemas = chunk.GetSpan<ReplicationSchemaRef>();
                ref Entity first = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref first, index);
                    if (!scope.IsFrontlineEntity(engine.World, entity, schemas[index].SchemaId, session.MapId))
                    {
                        continue;
                    }
                    commands.Destroy(in entity);
                    removed++;
                }
            }

            if (commands.Size > 0)
            {
                commands.Playback(engine.World);
            }
        }

        int remaining = 0;
        foreach (ref Chunk chunk in engine.World.Query(in AuthoredGameplayQuery))
        {
            ReadOnlySpan<ReplicationSchemaRef> schemas = chunk.GetSpan<ReplicationSchemaRef>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity entity = Unsafe.Add(ref first, index);
                if (scope.IsFrontlineEntity(engine.World, entity, schemas[index].SchemaId, session.MapId))
                {
                    remaining++;
                }
            }
        }
        if (remaining != 0)
        {
            throw new InvalidOperationException(
                $"RTS Frontline replicated client retained {remaining} map-authored authoritative gameplay entities after cleanup.");
        }

        ValidateRepresentatives(engine.World, session, config.Sides);
        return removed;
    }

    internal static void ValidateRepresentatives(
        World world,
        MapSession session,
        ReadOnlySpan<FrontlineSideConfig> sides)
    {
        if (sides.Length != 2)
        {
            throw new InvalidOperationException("RTS Frontline representative validation requires exactly two sides.");
        }

        Span<Entity> representatives = stackalloc Entity[sides.Length * 2];
        for (int i = 0; i < sides.Length; i++)
        {
            FrontlineSideConfig side = sides[i];
            if (!session.PlayerEntityLookup.TryGet(side.PlayerId, out Entity player) ||
                !world.IsAlive(player) ||
                !world.TryGet(player, out PlayerIdentity playerIdentity) ||
                playerIdentity.PlayerId != side.PlayerId ||
                !world.TryGet(player, out PlayerOwner owner) ||
                owner.PlayerId != side.PlayerId ||
                !world.TryGet(player, out Team playerTeam) ||
                playerTeam.Id != side.TeamId ||
                world.Has<ReplicationSchemaRef>(player))
            {
                throw new InvalidOperationException(
                    $"RTS Frontline requires a live non-replicated player representative for player {side.PlayerId} on team {side.TeamId}.");
            }

            if (!session.TeamEntityLookup.TryGet(side.TeamId, out Entity team) ||
                !world.IsAlive(team) ||
                !world.TryGet(team, out TeamIdentity teamIdentity) ||
                teamIdentity.TeamId != side.TeamId ||
                world.Has<ReplicationSchemaRef>(team))
            {
                throw new InvalidOperationException(
                    $"RTS Frontline requires a live non-replicated team representative for team {side.TeamId}.");
            }

            representatives[i * 2] = player;
            representatives[(i * 2) + 1] = team;
        }

        for (int i = 0; i < representatives.Length; i++)
        {
            for (int j = i + 1; j < representatives.Length; j++)
            {
                if (representatives[i] == representatives[j])
                {
                    throw new InvalidOperationException(
                        "RTS Frontline player and team representatives must be four distinct map-authored entities.");
                }
            }
        }
    }
}

internal static class FrontlineAuthoritativeVisibility
{
    private static readonly QueryDescription MatchStateQuery = new QueryDescription()
        .WithAll<ReplicationSchemaRef, FrontlineMatchStateEntity, MapEntity>();
    private static readonly QueryDescription ResourceQuery = new QueryDescription()
        .WithAll<ReplicationSchemaRef, FrontlineCrystalNode, MapEntity>();
    private static readonly QueryDescription OwnedGameplayQuery = new QueryDescription()
        .WithAll<ReplicationSchemaRef, PlayerOwner, MapEntity>();

    internal static DynamicParticipantVisibilityPublisher Install(GameEngine engine, FrontlineConfig config)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(config);
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("RTS Frontline visibility requires a focused map session.");
        if (!string.Equals(session.MapId.Value, config.MapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"RTS Frontline visibility expected map '{config.MapId}' but focused '{session.MapId.Value}'.");
        }

        EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
            ?? throw new InvalidOperationException("RTS Frontline visibility requires EntityCollectionStore.");
        KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
            ?? throw new InvalidOperationException("RTS Frontline visibility requires KnowledgeProjectionStore.");
        RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("RTS Frontline visibility requires RelationshipTypeRegistry.");

        DynamicParticipantQuerySpec[] specs = CreateSpecs(config);
        DynamicParticipantVisibilityBinding[] bindings = DynamicParticipantVisibilityCompiler.Compile(
            session,
            specs,
            relationshipTypes);
        if (bindings.Length != config.Sides.Length * 3)
        {
            throw new InvalidOperationException(
                $"RTS Frontline visibility compiled {bindings.Length} bindings; expected {config.Sides.Length * 3}.");
        }

        var publisher = new DynamicParticipantVisibilityPublisher(
            engine.World,
            collections,
            knowledge,
            bindings,
            engine.GetService(CoreServiceKeys.TagOps));
        int currentTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
        publisher.Publish(currentTick);
        ValidatePublishedKnowledge(engine.World, session, knowledge, config, currentTick);
        return publisher;
    }

    internal static DynamicParticipantQuerySpec[] CreateSpecs(FrontlineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var specs = new DynamicParticipantQuerySpec[config.Sides.Length * 3];
        for (int i = 0; i < config.Sides.Length; i++)
        {
            string viewerRef = config.Sides[i].PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int offset = i * 3;
            specs[offset] = DynamicParticipantQuerySpec.Create(
                DynamicParticipantViewerKind.Player,
                viewerRef,
                config.Replication.OwnedUnitsCollectionKey,
                DynamicParticipantQueryClause.Create(
                    ["ReplicationSchemaRef", "PlayerOwner", "MapEntity"],
                    ["PlayerIdentity", "TeamIdentity"]),
                DynamicParticipantQueryFlags.RequireMapMatch |
                    DynamicParticipantQueryFlags.ExcludePlayerIdentity |
                    DynamicParticipantQueryFlags.ExcludeTeamIdentity,
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                attributes: [config.HealthAttribute, config.CrystalAttribute],
                ownerMatchPolicy: DynamicParticipantOwnerMatchPolicy.MatchViewer);
            specs[offset + 1] = DynamicParticipantQuerySpec.Create(
                DynamicParticipantViewerKind.Player,
                viewerRef,
                config.Replication.PublicResourcesCollectionKey,
                DynamicParticipantQueryClause.Create(
                    ["ReplicationSchemaRef", "FrontlineCrystalNode", "MapEntity"]),
                DynamicParticipantQueryFlags.RequireMapMatch,
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                ownerMatchPolicy: DynamicParticipantOwnerMatchPolicy.Public);
            specs[offset + 2] = DynamicParticipantQuerySpec.Create(
                DynamicParticipantViewerKind.Player,
                viewerRef,
                config.Replication.PublicMatchStateCollectionKey,
                DynamicParticipantQueryClause.Create(
                    ["ReplicationSchemaRef", "FrontlineMatchStateEntity", "MapEntity"]),
                DynamicParticipantQueryFlags.RequireMapMatch,
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.None,
                ownerMatchPolicy: DynamicParticipantOwnerMatchPolicy.Public);
        }
        return specs;
    }

    private static void ValidatePublishedKnowledge(
        World world,
        MapSession session,
        KnowledgeProjectionStore knowledge,
        FrontlineConfig config,
        int currentTick)
    {
        Span<Entity> viewers = stackalloc Entity[config.Sides.Length];
        for (int i = 0; i < config.Sides.Length; i++)
        {
            if (!session.PlayerEntityLookup.TryGet(config.Sides[i].PlayerId, out viewers[i]) ||
                !world.IsAlive(viewers[i]))
            {
                throw new InvalidOperationException(
                    $"RTS Frontline visibility cannot resolve player {config.Sides[i].PlayerId}.");
            }
        }

        int matchStateCount = 0;
        foreach (ref Chunk chunk in world.Query(in MatchStateQuery))
        {
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity target = Unsafe.Add(ref first, index);
                matchStateCount++;
                for (int viewerIndex = 0; viewerIndex < viewers.Length; viewerIndex++)
                {
                    if (!knowledge.TryGet(viewers[viewerIndex], target, currentTick, out KnowledgeDisclosureRecord record) ||
                        record.Presence != KnowledgePresence.LiveVisible ||
                        record.Position != KnowledgePositionAccess.None)
                    {
                        throw new InvalidOperationException(
                            "RTS Frontline match state must be live-visible without position data to every player.");
                    }
                }
            }
        }
        if (matchStateCount != 1)
        {
            throw new InvalidOperationException(
                $"RTS Frontline requires exactly one authoritative match-state entity; found {matchStateCount}.");
        }

        int resourceCount = 0;
        foreach (ref Chunk chunk in world.Query(in ResourceQuery))
        {
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                Entity target = Unsafe.Add(ref first, index);
                resourceCount++;
                for (int viewerIndex = 0; viewerIndex < viewers.Length; viewerIndex++)
                {
                    if (!knowledge.TryGet(viewers[viewerIndex], target, currentTick, out KnowledgeDisclosureRecord record) ||
                        record.Presence != KnowledgePresence.LiveVisible ||
                        record.Position != KnowledgePositionAccess.Live)
                    {
                        throw new InvalidOperationException(
                            "RTS Frontline crystal nodes must be live-visible to every player.");
                    }
                }
            }
        }
        if (resourceCount == 0)
        {
            throw new InvalidOperationException("RTS Frontline requires at least one public crystal node.");
        }

        Span<int> ownedCounts = stackalloc int[config.Sides.Length];
        foreach (ref Chunk chunk in world.Query(in OwnedGameplayQuery))
        {
            ReadOnlySpan<PlayerOwner> owners = chunk.GetSpan<PlayerOwner>();
            ref Entity first = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                int sideIndex = config.ResolveSideIndex(world.TryGet(Unsafe.Add(ref first, index), out Team team)
                    ? team.Id
                    : throw new InvalidOperationException("RTS Frontline owned replicated entity requires Team."));
                if (owners[index].PlayerId != config.Sides[sideIndex].PlayerId ||
                    !knowledge.TryGet(viewers[sideIndex], Unsafe.Add(ref first, index), currentTick, out _))
                {
                    throw new InvalidOperationException(
                        "RTS Frontline owned replicated entity is not disclosed to its configured player.");
                }
                ownedCounts[sideIndex]++;
            }
        }
        for (int i = 0; i < ownedCounts.Length; i++)
        {
            if (ownedCounts[i] == 0)
            {
                throw new InvalidOperationException(
                    $"RTS Frontline player {config.Sides[i].PlayerId} has no owned replicated gameplay entities.");
            }
        }
    }
}

internal sealed class FrontlineReplicationLifecycle
{
    private readonly FrontlineRuntime _runtime;
    private DynamicParticipantVisibilityPublisher? _visibilityPublisher;
    private bool _visibilitySystemInstalled;

    public FrontlineReplicationLifecycle(FrontlineRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public Task HandleGameStartAsync(ScriptContext context) => Execute(() =>
    {
        GameEngine engine = RequireEngine(context, "GameStart");
        FrontlineReplication.Install(engine, _runtime);
        if (engine.GetService(CoreServiceKeys.NetworkProcessRole) == NetworkProcessRole.AuthoritativeServer)
        {
            if (_visibilitySystemInstalled)
            {
                throw new InvalidOperationException("RTS Frontline visibility system was registered more than once.");
            }
            IClock clock = engine.GetService(CoreServiceKeys.Clock)
                ?? throw new InvalidOperationException("RTS Frontline visibility requires Clock.");
            // capabilityId: rts-frontline.dynamic-participant-visibility
            engine.RegisterSystem(
                new DynamicParticipantVisibilitySystem(engine.World, () => _visibilityPublisher, clock),
                SystemGroup.RuntimeEntityBinding);
            _visibilitySystemInstalled = true;
        }
    });

    public Task HandleMapLoadedAsync(ScriptContext context) => Execute(() =>
    {
        GameEngine engine = RequireEngine(context, "MapLoaded");
        if (!string.Equals(engine.CurrentMapSession?.MapId.Value, _runtime.Config.MapId, StringComparison.Ordinal))
        {
            return;
        }

        NetworkProcessRole role = engine.GetService(CoreServiceKeys.NetworkProcessRole);
        if (role == NetworkProcessRole.ReplicatedClient)
        {
            int removed = FrontlineReplicatedClientMapBoundary.RemoveAuthoredGameplayEntities(engine, _runtime.Config);
            if (removed <= 0)
            {
                throw new InvalidOperationException(
                    "RTS Frontline replicated client MapLoaded found no map-authored authoritative gameplay entities to remove.");
            }
        }
        else if (role == NetworkProcessRole.AuthoritativeServer)
        {
            ReplaceVisibilityPublisher(engine);
        }
    });

    public Task HandleMapResumedAsync(ScriptContext context) => Execute(() =>
    {
        GameEngine engine = RequireEngine(context, "MapResumed");
        if (!string.Equals(engine.CurrentMapSession?.MapId.Value, _runtime.Config.MapId, StringComparison.Ordinal))
        {
            return;
        }

        NetworkProcessRole role = engine.GetService(CoreServiceKeys.NetworkProcessRole);
        if (role == NetworkProcessRole.ReplicatedClient)
        {
            FrontlineReplicatedClientMapBoundary.RemoveAuthoredGameplayEntities(engine, _runtime.Config);
        }
        else if (role == NetworkProcessRole.AuthoritativeServer)
        {
            ReplaceVisibilityPublisher(engine);
        }
    });

    public Task HandleMapUnloadedAsync(ScriptContext context) => Execute(() =>
    {
        if (!string.Equals(context.Get(CoreServiceKeys.MapId).Value, _runtime.Config.MapId, StringComparison.Ordinal))
        {
            return;
        }

        _visibilityPublisher?.Clear();
        _visibilityPublisher = null;
    });

    private void ReplaceVisibilityPublisher(GameEngine engine)
    {
        if (!_visibilitySystemInstalled)
        {
            throw new InvalidOperationException(
                "RTS Frontline authoritative visibility system must be installed before map focus.");
        }
        _visibilityPublisher?.Clear();
        _visibilityPublisher = FrontlineAuthoritativeVisibility.Install(engine, _runtime.Config);
    }

    private static GameEngine RequireEngine(ScriptContext context, string eventName)
    {
        return context.Get(CoreServiceKeys.Engine) as GameEngine
            ?? throw new InvalidOperationException($"RTS Frontline replication requires GameEngine on {eventName}.");
    }

    private static Task Execute(Action action)
    {
        try
        {
            action();
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }
}

internal static class FrontlineReplication
{
    internal static void Install(GameEngine engine, FrontlineRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(runtime);
        FrontlineConfig config = runtime.Config;

        // capabilityId: rts-frontline.vision-scope-authoring
        engine.RegisterSystem(
            new FrontlineVisionScopeAuthoringSystem(engine, config),
            SystemGroup.SchemaUpdate);

        ConfigureFogProjectionPolicy(engine, config);

        NetworkProcessRole role = engine.GetService(CoreServiceKeys.NetworkProcessRole);
        if (role == NetworkProcessRole.Standalone)
        {
            return;
        }

        ReplicationSchemaProjectorRegistry projectors = engine.GetService(CoreServiceKeys.ReplicationSchemaProjectors)
            ?? throw new InvalidOperationException("RTS Frontline network launch requires the replication projector registry.");
        ClientReplicationSchemaApplierRegistry appliers = engine.GetService(CoreServiceKeys.ClientReplicationSchemaAppliers)
            ?? throw new InvalidOperationException("RTS Frontline network launch requires the client replication applier registry.");

        int healthId = AttributeRegistry.GetId(config.HealthAttribute);
        int crystalId = AttributeRegistry.GetId(config.CrystalAttribute);
        if (healthId == AttributeRegistry.InvalidId || crystalId == AttributeRegistry.InvalidId)
        {
            throw new InvalidOperationException(
                "RTS Frontline replication requires registered Health and Crystals attributes before GameStart registration.");
        }

        FrontlineReplicationSpec[] specs = CreateSpecs(config.Replication);
        PresentationStableIdAllocator stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
            ?? throw new InvalidOperationException("RTS Frontline client replication requires presentation stable id allocation.");
        var templates = new FrontlineClientTemplateFactory(
            engine.World,
            engine.MapLoader.TemplateRegistry.GetAll(),
            specs,
            config.Replication.MatchStateSchemaId,
            engine.MapLoader.EntityTemplateKeys,
            engine.MapLoader.RequireComponentAuthoringContext(),
            stableIds);
        OwnershipResolver ownership = engine.GetService(CoreServiceKeys.OwnershipResolver)
            ?? throw new InvalidOperationException("RTS Frontline client replication requires OwnershipResolver.");
        PlayerEntityLookup players = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("RTS Frontline client replication requires PlayerEntityLookup.");
        RegisterHandlers(
            engine,
            projectors,
            appliers,
            templates,
            runtime,
            ownership,
            players,
            config.Sides,
            config.Replication.MatchStateSchemaId,
            specs,
            healthId,
            crystalId);

        if (role == NetworkProcessRole.AuthoritativeServer)
        {
            NetworkEntityTable entities = engine.GetService(CoreServiceKeys.NetworkEntityTable)
                ?? throw new InvalidOperationException("RTS Frontline authoritative launch requires NetworkEntityTable.");
            // capabilityId: rts-frontline.network-entity-binding
            engine.RegisterSystem(
                new FrontlineNetworkEntityBindingSystem(engine, entities, config),
                SystemGroup.Cleanup);
        }
    }

    internal static FogProjectionPolicy CreateFogProjectionPolicy(FrontlineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        KnowledgeIdMask256 attributes = KnowledgeIdMask256.Empty;
        for (int i = 0; i < config.Replication.VisibleEnemyAttributes.Length; i++)
        {
            string attribute = config.Replication.VisibleEnemyAttributes[i];
            int attributeId = AttributeRegistry.GetId(attribute);
            if (attributeId == AttributeRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"RTS Frontline visible enemy attribute '{attribute}' is not registered.");
            }

            attributes = attributes.WithId(attributeId);
        }

        return new FogProjectionPolicy(
            new FogDisclosurePolicy(
                attributes,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                ttlTicks: 0,
                trueSightRevealsConcealment: true),
            memoryTtlTicks: 0);
    }

    private static void ConfigureFogProjectionPolicy(GameEngine engine, FrontlineConfig config)
    {
        FogKnowledgeProjector projector = engine.GetService(CoreServiceKeys.FogKnowledgeProjector)
            ?? throw new InvalidOperationException(
                "RTS Frontline requires the Core fog Knowledge projector before GameStart registration.");
        FogProjectionPolicy policy = CreateFogProjectionPolicy(config);
        projector.ConfigureProjectionPolicy(in policy);
    }

    internal static FrontlineReplicationSpec[] CreateSpecs(FrontlineReplicationConfig config)
    {
        return new[]
        {
            new FrontlineReplicationSpec(FrontlineReplicationKind.Core, config.CoreSchemaId, HasHealth: true, HasCrystals: true, HasOwner: true),
            new FrontlineReplicationSpec(FrontlineReplicationKind.Harvester, config.HarvesterSchemaId, HasHealth: true, HasCrystals: false, HasOwner: true),
            new FrontlineReplicationSpec(FrontlineReplicationKind.Infantry, config.InfantrySchemaId, HasHealth: true, HasCrystals: false, HasOwner: true),
            new FrontlineReplicationSpec(FrontlineReplicationKind.CrystalNode, config.CrystalNodeSchemaId, HasHealth: false, HasCrystals: false, HasOwner: false),
        };
    }

    internal static void RegisterHandlers(
        GameEngine engine,
        ReplicationSchemaProjectorRegistry projectors,
        ClientReplicationSchemaApplierRegistry appliers,
        FrontlineClientTemplateFactory templates,
        FrontlineRuntime runtime,
        OwnershipResolver ownership,
        PlayerEntityLookup players,
        FrontlineSideConfig[] sides,
        int matchStateSchemaId,
        ReadOnlySpan<FrontlineReplicationSpec> specs,
        int healthId,
        int crystalId)
    {
        if (specs.Length != 4)
        {
            throw new InvalidOperationException("RTS Frontline requires exactly four replication schemas.");
        }

        RegisterOrThrow(projectors, specs[0].SchemaId, new FrontlineCoreReplicationProjector(in specs[0], healthId, crystalId));
        RegisterOrThrow(projectors, specs[1].SchemaId, new FrontlineHarvesterReplicationProjector(in specs[1], healthId, crystalId));
        RegisterOrThrow(projectors, specs[2].SchemaId, new FrontlineInfantryReplicationProjector(in specs[2], healthId, crystalId));
        RegisterOrThrow(projectors, specs[3].SchemaId, new FrontlineCrystalNodeReplicationProjector(in specs[3], healthId, crystalId));
        RegisterOrThrow(projectors, matchStateSchemaId, new FrontlineMatchStateReplicationProjector(runtime, matchStateSchemaId));

        FrontlineTagBinder tagBinder = runtime.TagBinder;
        var sideVisionScopeKeyIds = new int[sides.Length];
        for (int i = 0; i < sides.Length; i++)
        {
            sideVisionScopeKeyIds[i] = FrontlineVisionScopes.Resolve(engine, sides[i].VisionScopeKey);
        }

        RegisterOrThrow(appliers, specs[0].SchemaId, new FrontlineCoreReplicationApplier(in specs[0], templates, sides, sideVisionScopeKeyIds, healthId, crystalId, tagBinder, ownership, players));
        RegisterOrThrow(appliers, specs[1].SchemaId, new FrontlineHarvesterReplicationApplier(in specs[1], templates, sides, sideVisionScopeKeyIds, healthId, crystalId, tagBinder, ownership, players));
        RegisterOrThrow(appliers, specs[2].SchemaId, new FrontlineInfantryReplicationApplier(in specs[2], templates, sides, sideVisionScopeKeyIds, healthId, crystalId, tagBinder, ownership, players));
        RegisterOrThrow(appliers, specs[3].SchemaId, new FrontlineCrystalNodeReplicationApplier(in specs[3], templates, sides, sideVisionScopeKeyIds, healthId, crystalId, tagBinder, ownership, players));
        RegisterOrThrow(
            appliers,
            matchStateSchemaId,
            new FrontlineMatchStateReplicationApplier(
                matchStateSchemaId,
                runtime.Config.ReadyCountdownTicks,
                runtime.Config.MatchDurationTicks,
                runtime.Config.DisconnectGraceTicks,
                templates));
    }

    private static void RegisterOrThrow(
        ReplicationSchemaProjectorRegistry registry,
        int schemaId,
        IReplicationSchemaProjector projector)
    {
        ReplicationSchemaRegistrationResult result = registry.Register(schemaId, projector);
        if (result != ReplicationSchemaRegistrationResult.Success)
        {
            throw new InvalidOperationException(
                $"RTS Frontline projector registration failed for schema {schemaId}: {result}.");
        }
    }

    private static void RegisterOrThrow(
        ClientReplicationSchemaApplierRegistry registry,
        int schemaId,
        IClientReplicationSchemaApplier applier)
    {
        ReplicationSchemaRegistrationResult result = registry.Register(schemaId, applier);
        if (result != ReplicationSchemaRegistrationResult.Success)
        {
            throw new InvalidOperationException(
                $"RTS Frontline client applier registration failed for schema {schemaId}: {result}.");
        }
    }
}

internal readonly struct FrontlineReplicationEntityScope
{
    private readonly MapId _mapId;
    private readonly int _coreSchemaId;
    private readonly int _harvesterSchemaId;
    private readonly int _infantrySchemaId;
    private readonly int _crystalNodeSchemaId;
    private readonly int _matchStateSchemaId;

    internal FrontlineReplicationEntityScope(FrontlineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _mapId = new MapId(config.MapId);
        _coreSchemaId = config.Replication.CoreSchemaId;
        _harvesterSchemaId = config.Replication.HarvesterSchemaId;
        _infantrySchemaId = config.Replication.InfantrySchemaId;
        _crystalNodeSchemaId = config.Replication.CrystalNodeSchemaId;
        _matchStateSchemaId = config.Replication.MatchStateSchemaId;
    }

    internal bool IsConfiguredMap(MapId mapId) => mapId == _mapId;

    internal bool IsFrontlineEntity(World world, Entity entity, int schemaId, MapId focusedMapId)
    {
        FrontlineReplicationKind kind;
        if (schemaId == _coreSchemaId)
        {
            kind = FrontlineReplicationKind.Core;
        }
        else if (schemaId == _harvesterSchemaId)
        {
            kind = FrontlineReplicationKind.Harvester;
        }
        else if (schemaId == _infantrySchemaId)
        {
            kind = FrontlineReplicationKind.Infantry;
        }
        else if (schemaId == _crystalNodeSchemaId)
        {
            kind = FrontlineReplicationKind.CrystalNode;
        }
        else if (schemaId == _matchStateSchemaId)
        {
            kind = FrontlineReplicationKind.MatchState;
        }
        else
        {
            return false;
        }

        if (!world.TryGet(entity, out MapEntity mapEntity))
        {
            throw new InvalidOperationException(
                $"RTS Frontline schema {schemaId} requires explicit MapEntity ownership.");
        }
        if (mapEntity.MapId != focusedMapId)
        {
            return false;
        }

        int roleCount =
            (world.Has<FrontlineCore>(entity) ? 1 : 0) +
            (world.Has<FrontlineHarvester>(entity) ? 1 : 0) +
            (world.Has<FrontlineInfantry>(entity) ? 1 : 0) +
            (world.Has<FrontlineCrystalNode>(entity) ? 1 : 0) +
            (world.Has<FrontlineMatchStateEntity>(entity) ? 1 : 0);
        bool matchesRole = kind switch
        {
            FrontlineReplicationKind.Core => world.Has<FrontlineCore>(entity),
            FrontlineReplicationKind.Harvester => world.Has<FrontlineHarvester>(entity),
            FrontlineReplicationKind.Infantry => world.Has<FrontlineInfantry>(entity),
            FrontlineReplicationKind.CrystalNode => world.Has<FrontlineCrystalNode>(entity),
            FrontlineReplicationKind.MatchState => world.Has<FrontlineMatchStateEntity>(entity),
            _ => false,
        };
        if (roleCount != 1 || !matchesRole)
        {
            throw new InvalidOperationException(
                $"RTS Frontline schema {schemaId} does not match exactly one configured replication role.");
        }

        return true;
    }
}
