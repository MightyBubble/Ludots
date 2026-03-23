using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Unified performer output system. Handles two modes:
    ///
    /// 1. Instance-scoped: iterate PerformerInstanceBuffer, tick elapsed,
    ///    evaluate visibility, resolve bindings, apply time modulation, emit.
    ///
    /// 2. Entity-scoped: iterate ECS entities matching EntityScopeFilter,
    ///    evaluate visibility, resolve bindings, emit. No instances needed.
    ///
    /// Resolution priority: Override > Binding > Default.
    /// Parameters are NEVER cached — always resolved fresh for data sync safety.
    /// </summary>
    /// <summary>
    /// Delegate for resolving a per-entity color (e.g. team color, faction color).
    /// Injected by the composition root. When null, <see cref="ValueSourceKind.EntityColor"/> returns white.
    /// </summary>
    public delegate Vector4 EntityColorResolver(World world, Entity entity);

    public sealed class PerformerEmitSystem : BaseSystem<World, float>
    {
        private readonly PerformerInstanceBuffer _instances;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly GroundOverlayBuffer _groundOverlays;
        private readonly PresentationVisualProxyEmitter _proxyEmitter;
        private readonly WorldHudBatchBuffer _worldHud;
        private readonly GraphProgramRegistry _programs;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly Dictionary<string, object> _globals;
        private readonly EntityColorResolver _entityColorResolver;
        private int _entityHealthBarDefinitionId = int.MinValue;
        private int _entityWorldTextDefinitionId = int.MinValue;

        // Entity-scoped queries
        private readonly QueryDescription _attrNoCullQuery = new QueryDescription()
            .WithAll<VisualTransform, AttributeBuffer>()
            .WithNone<CullState>();
        private readonly QueryDescription _attrWithCullQuery = new QueryDescription()
            .WithAll<VisualTransform, AttributeBuffer, CullState>();
        private readonly QueryDescription _vtNoCullQuery = new QueryDescription()
            .WithAll<VisualTransform>()
            .WithNone<CullState>();
        private readonly QueryDescription _vtWithCullQuery = new QueryDescription()
            .WithAll<VisualTransform, CullState>();

        // Pre-allocated Graph VM registers
        private readonly float[] _floatRegs = new float[GraphVmLimits.MaxFloatRegisters];
        private readonly int[] _intRegs = new int[GraphVmLimits.MaxIntRegisters];
        private readonly byte[] _boolRegs = new byte[GraphVmLimits.MaxBoolRegisters];
        private readonly Entity[] _entityRegs = new Entity[GraphVmLimits.MaxEntityRegisters];
        private readonly Entity[] _targets = new Entity[GraphVmLimits.MaxTargets];
        private readonly GasGraphOpHandlerTable _handlers = GasGraphOpHandlerTable.Instance;

        public PerformerEmitSystem(
            World world,
            PerformerInstanceBuffer instances,
            PerformerDefinitionRegistry definitions,
            GroundOverlayBuffer groundOverlays,
            PrimitiveDrawBuffer primitives,
            WorldHudBatchBuffer worldHud,
            GraphProgramRegistry programs,
            IGraphRuntimeApi graphApi,
            Dictionary<string, object> globals,
            EntityColorResolver entityColorResolver = null,
            PrimitiveDrawBuffer? snapshotBuffer = null,
            PresentationVisualProxyBuffer? proxyBuffer = null,
            SkinnedVisualBatchBuffer? skinnedBatchBuffer = null)
            : base(world)
        {
            _instances = instances;
            _definitions = definitions;
            _groundOverlays = groundOverlays;
            _proxyEmitter = new PresentationVisualProxyEmitter(primitives, snapshotBuffer, proxyBuffer, skinnedBatchBuffer);
            _worldHud = worldHud;
            _programs = programs;
            _graphApi = graphApi;
            _globals = globals;
            _entityColorResolver = entityColorResolver;
        }

        public override void Update(in float dt)
        {
            // ── Part 1: Instance-scoped performers ──
            _instances.ProcessActive(dt, (int handle, ref PerformerInstance inst) =>
            {
                if (!_definitions.TryGet(inst.DefId, out var def)) return;
                if (def.EntityScope != EntityScopeFilter.None) return; // skip entity-scoped

                if (inst.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(inst.Owner))
                {
                    _instances.Release(handle);
                    return;
                }

                // Auto-expire duration-based performers
                if (def.DefaultLifetime > 0f && inst.Elapsed >= def.DefaultLifetime)
                {
                    _instances.Release(handle);
                    return;
                }

                // Evaluate visibility
                if (!EvaluateVisibility(def, inst.Owner)) return;

                // Compute position with offset and Y-drift
                Vector3 pos = ResolveAnchorPosition(in inst) + def.PositionOffset;
                pos.Y += def.PositionYDriftPerSecond * inst.Elapsed;

                // Compute alpha modulation
                float alphaMod = 1f;
                if (def.AlphaFadeOverLifetime && def.DefaultLifetime > 0f)
                    alphaMod = Math.Clamp(1f - inst.Elapsed / def.DefaultLifetime, 0f, 1f);

                EmitForVisualKind(handle, inst.DefId, def, inst.Owner, pos, alphaMod);
            });

            // ── Part 2: Entity-scoped performers ──
            var ids = _definitions.RegisteredIds;
            for (int di = 0; di < ids.Count; di++)
            {
                if (!_definitions.TryGet(ids[di], out var def)) continue;
                if (def.EntityScope == EntityScopeFilter.None) continue;
                if (ShouldSkipEntityScopedDefinition(ids[di], def)) continue;

                EmitEntityScoped(ids[di], def);
            }
        }

        // ── Entity-Scoped Emission ──

        private void EmitEntityScoped(int definitionId, PerformerDefinition def)
        {
            switch (def.EntityScope)
            {
                case EntityScopeFilter.AllWithAttributes:
                    EmitEntityScopedWithAttr(definitionId, def, _attrNoCullQuery, requireCullCheck: false);
                    EmitEntityScopedWithAttr(definitionId, def, _attrWithCullQuery, requireCullCheck: true);
                    break;

                case EntityScopeFilter.AllWithVisualTransform:
                    EmitEntityScopedVT(definitionId, def, _vtNoCullQuery, requireCullCheck: false);
                    EmitEntityScopedVT(definitionId, def, _vtWithCullQuery, requireCullCheck: true);
                    break;
            }
        }

        private bool ShouldSkipEntityScopedDefinition(int definitionId, PerformerDefinition def)
        {
            if (!TryGetRenderDebugState(out RenderDebugState renderDebug))
            {
                return false;
            }

            if (def.VisualKind == PerformerVisualKind.WorldBar &&
                definitionId == ResolveWellKnownDefinitionId(ref _entityHealthBarDefinitionId, WellKnownPerformerKeys.EntityHealthBar))
            {
                return !renderDebug.DrawWorldHudBars;
            }

            if (def.VisualKind == PerformerVisualKind.WorldText &&
                definitionId == ResolveWellKnownDefinitionId(ref _entityWorldTextDefinitionId, WellKnownPerformerKeys.EntityWorldText))
            {
                return !renderDebug.DrawWorldHudText;
            }

            return false;
        }

        private int ResolveWellKnownDefinitionId(ref int cache, string key)
        {
            if (cache != int.MinValue)
            {
                return cache;
            }

            cache = _definitions.GetId(key);
            return cache;
        }

        private bool TryGetRenderDebugState(out RenderDebugState state)
        {
            if (_globals.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out object? value) &&
                value is RenderDebugState renderDebug)
            {
                state = renderDebug;
                return true;
            }

            state = null!;
            return false;
        }

        private void EmitEntityScopedWithAttr(int definitionId, PerformerDefinition def, QueryDescription query, bool requireCullCheck)
        {
            // When requireCullCheck is true, the chunk already contains only entities WITH CullState.
            // If the definition's visibility condition is OwnerCullVisible, the chunk-level cull check
            // is semantically equivalent — skip the redundant per-entity ECS random-access lookup.
            bool skipVisibilityEval = requireCullCheck
                && def.VisibilityCondition.Inline == InlineConditionKind.OwnerCullVisible;

            bool hasTemplateFilter = def.RequiredTemplateId > 0;

            var q = World.Query(in query);
            foreach (var chunk in q)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var culls = requireCullCheck ? chunk.GetArray<CullState>() : null;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (requireCullCheck && culls != null && !culls[i].IsVisible) continue;

                    // Template filter — runtime check via World.Has/Get
                    if (hasTemplateFilter)
                    {
                        var entity = chunk.Entity(i);
                        if (!World.Has<VisualTemplateRef>(entity) ||
                            World.Get<VisualTemplateRef>(entity).TemplateId != def.RequiredTemplateId)
                            continue;
                    }

                    if (!skipVisibilityEval)
                    {
                        var entity = chunk.Entity(i);
                        if (!EvaluateVisibility(def, entity)) continue;
                    }

                    Vector3 pos = transforms[i].Position + def.PositionOffset;
                    EmitForVisualKind(-1, definitionId, def, chunk.Entity(i), pos, 1f);
                }
            }
        }

        private void EmitEntityScopedVT(int definitionId, PerformerDefinition def, QueryDescription query, bool requireCullCheck)
        {
            bool skipVisibilityEval = requireCullCheck
                && def.VisibilityCondition.Inline == InlineConditionKind.OwnerCullVisible;

            bool hasTemplateFilter = def.RequiredTemplateId > 0;

            var q = World.Query(in query);
            foreach (var chunk in q)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var culls = requireCullCheck ? chunk.GetArray<CullState>() : null;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (requireCullCheck && culls != null && !culls[i].IsVisible) continue;

                    // Template filter
                    if (hasTemplateFilter)
                    {
                        var entity = chunk.Entity(i);
                        if (!World.Has<VisualTemplateRef>(entity) ||
                            World.Get<VisualTemplateRef>(entity).TemplateId != def.RequiredTemplateId)
                            continue;
                    }

                    if (!skipVisibilityEval)
                    {
                        var entity = chunk.Entity(i);
                        if (!EvaluateVisibility(def, entity)) continue;
                    }

                    Vector3 pos = transforms[i].Position + def.PositionOffset;
                    EmitForVisualKind(-1, definitionId, def, chunk.Entity(i), pos, 1f);
                }
            }
        }

        // ── Unified Emit by VisualKind ──

        private void EmitForVisualKind(int handle, int definitionId, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
        {
            switch (def.VisualKind)
            {
                case PerformerVisualKind.GroundOverlay:
                    EmitGroundOverlay(handle, def, owner, pos, alphaMod);
                    break;
                case PerformerVisualKind.Marker3D:
                    EmitMarker3D(handle, def, owner, pos, alphaMod);
                    break;
                case PerformerVisualKind.WorldBar:
                    EmitWorldBar(handle, definitionId, def, owner, pos, alphaMod);
                    break;
                case PerformerVisualKind.WorldText:
                    EmitWorldText(handle, definitionId, def, owner, pos, alphaMod);
                    break;
                case PerformerVisualKind.ManifestationPrimitive:
                    EmitManifestationPrimitive(handle, definitionId, def, owner, pos, alphaMod);
                    break;
            }
        }

        // ── Visibility ──

        private bool EvaluateVisibility(PerformerDefinition def, Entity owner)
        {
            ref readonly var cond = ref def.VisibilityCondition;
            if (cond.Inline != InlineConditionKind.None)
                return EvaluateInlineVisibility(cond.Inline, owner);
            if (cond.GraphProgramId > 0)
                return EvaluateGraphBool(cond.GraphProgramId, owner, owner);
            return true;
        }

        private bool EvaluateInlineVisibility(InlineConditionKind kind, Entity owner)
        {
            switch (kind)
            {
                case InlineConditionKind.None: return true;
                case InlineConditionKind.SourceIsLocalPlayer:
                case InlineConditionKind.TargetIsLocalPlayer:
                    return IsLocalPlayer(owner);
                case InlineConditionKind.SourceIsAlive:
                case InlineConditionKind.TargetIsAlive:
                    return World.IsAlive(owner);
                case InlineConditionKind.OwnerCullVisible:
                    if (!World.IsAlive(owner)) return false;
                    if (!World.Has<CullState>(owner)) return true;
                    return World.Get<CullState>(owner).IsVisible;
                default: return true;
            }
        }

        // ── Parameter Resolution ──

        private float ResolveParam(int handle, PerformerDefinition def, Entity owner, int paramKey, float defaultValue)
        {
            // 1. Imperative override (highest priority)
            if (handle >= 0 && _instances.TryGetParamOverride(handle, paramKey, out float ov))
                return ov;

            // 2. Declarative binding — O(1) indexed lookup
            var idx = def.BindingIndex;
            if (paramKey >= 0 && paramKey < idx.Length)
            {
                int bi = idx[paramKey];
                if (bi >= 0)
                    return ResolveValueRef(in def.Bindings[bi].Value, owner);
            }

            return defaultValue;
        }

        private float ResolveValueRef(in ValueRef vr, Entity owner)
        {
            switch (vr.Source)
            {
                case ValueSourceKind.Constant:
                    return vr.ConstantValue;
                case ValueSourceKind.Attribute:
                    if (_graphApi != null && _graphApi.TryGetAttributeCurrent(owner, vr.SourceId, out float attrVal))
                        return attrVal;
                    return 0f;
                case ValueSourceKind.AttributeRatio:
                    return ResolveAttributeRatio(owner, vr.SourceId);
                case ValueSourceKind.AttributeBase:
                    return ResolveAttributeBase(owner, vr.SourceId);
                case ValueSourceKind.Graph:
                    return EvaluateGraphFloat(vr.SourceId, owner);
                case ValueSourceKind.EntityColor:
                    return ResolveEntityColorChannel(owner, vr.SourceId);
                case ValueSourceKind.FacingRadians:
                    return ResolveFacingRadians(owner);
                case ValueSourceKind.FacingDegrees:
                    return ResolveFacingDegrees(owner);
                default:
                    return 0f;
            }
        }

        private float ResolveEntityColorChannel(Entity owner, int channelIndex)
        {
            if (_entityColorResolver == null) return 1f;
            var c = _entityColorResolver(World, owner);
            return channelIndex switch
            {
                0 => c.X,
                1 => c.Y,
                2 => c.Z,
                3 => c.W,
                _ => 1f,
            };
        }

        private float ResolveFacingRadians(Entity owner)
        {
            if (!World.IsAlive(owner) || !World.Has<Ludots.Core.Components.FacingDirection>(owner))
            {
                return 0f;
            }

            return World.Get<Ludots.Core.Components.FacingDirection>(owner).AngleRad;
        }

        private float ResolveFacingDegrees(Entity owner)
        {
            return ResolveFacingRadians(owner) * (180f / MathF.PI);
        }

        private float ResolveAttributeRatio(Entity owner, int attributeId)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner)) return 1f;
            ref var attr = ref World.Get<AttributeBuffer>(owner);
            float current = attr.GetCurrent(attributeId);
            float max = attr.GetBase(attributeId);
            if (max <= 0f) max = 1f;
            return Math.Clamp(current / max, 0f, 1f);
        }

        private float ResolveAttributeBase(Entity owner, int attributeId)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner)) return 0f;
            ref var attr = ref World.Get<AttributeBuffer>(owner);
            float max = attr.GetBase(attributeId);
            return max <= 0f ? 1f : max;
        }

        // ── Emit per VisualKind ──

        private void EmitGroundOverlay(int handle, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
        {
            var fc = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            fc.W *= alphaMod;

            _groundOverlays.TryAdd(new GroundOverlayItem
            {
                Shape = (GroundOverlayShape)def.MeshOrShapeId,
                Center = pos,
                Radius = ResolveParam(handle, def, owner, 0, def.DefaultScale),
                InnerRadius = ResolveParam(handle, def, owner, 1, 0f),
                Angle = ResolveParam(handle, def, owner, 2, 0f),
                Rotation = ResolveParam(handle, def, owner, 3, 0f),
                FillColor = fc,
                BorderColor = ResolveColor(handle, def, owner, 8, 9, 10, 11, new Vector4(1f, 1f, 1f, 1f)),
                BorderWidth = ResolveParam(handle, def, owner, 12, 0.02f),
                Length = ResolveParam(handle, def, owner, 13, 0f),
                Width = ResolveParam(handle, def, owner, 14, 0f),
            });
        }

        private void EmitMarker3D(int handle, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
        {
            float scaleUniform = ResolveParam(handle, def, owner, 0, def.DefaultScale);
            // ParamKey 1/2/3: per-axis scale override. Falls back to uniform scale.
            float sx = ResolveParam(handle, def, owner, 1, scaleUniform);
            float sy = ResolveParam(handle, def, owner, 2, scaleUniform);
            float sz = ResolveParam(handle, def, owner, 3, scaleUniform);
            var color = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            color.W *= alphaMod;
            int stableId = ResolveMarkerStableId(handle, def.Id, owner);

            _proxyEmitter.Emit(new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                PrimitiveKind = PrimitiveDrawKind.MeshAsset,
                MeshAssetId = def.MeshOrShapeId,
                Position = pos,
                Rotation = Quaternion.Identity,
                Scale = new Vector3(sx, sy, sz),
                Color = color,
                StableId = stableId,
                RenderPath = VisualRenderPath.StaticMesh,
                Mobility = VisualMobility.Movable,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
            });
        }

        private void EmitManifestationPrimitive(int handle, int definitionId, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
        {
            var color = ResolveColor(
                handle,
                def,
                owner,
                WellKnownPerformerParamKeys.PrimitiveColorR,
                WellKnownPerformerParamKeys.PrimitiveColorG,
                WellKnownPerformerParamKeys.PrimitiveColorB,
                WellKnownPerformerParamKeys.PrimitiveColorA,
                def.DefaultColor);
            color.W *= alphaMod;

            bool hasGeometry = World.IsAlive(owner) && World.Has<ManifestationGeometry2D>(owner);
            ManifestationGeometry2D geometry = hasGeometry ? World.Get<ManifestationGeometry2D>(owner) : default;
            PrimitiveDrawKind primitiveKind = ResolvePrimitiveKind(def.MeshOrShapeId, hasGeometry ? geometry.PrimitiveKind : ManifestationPrimitiveKind.Beam);

            _proxyEmitter.Emit(new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                PrimitiveKind = primitiveKind,
                MeshAssetId = 0,
                Position = pos,
                Rotation = ResolveOwnerRotation(owner),
                Scale = Vector3.One,
                Color = color,
                StableId = ResolvePerformerStableId(handle, definitionId, owner, PerformerVisualKind.ManifestationPrimitive),
                RenderPath = VisualRenderPath.None,
                Mobility = VisualMobility.Movable,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = VisualVisibility.Visible,
                PrimitiveLength = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveLength, ToMeters(geometry.LengthCm)),
                PrimitiveWidth = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveWidth, ToMeters(geometry.WidthCm)),
                PrimitiveEndWidth = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveEndWidth, ToMeters(geometry.EndWidthCm)),
                PrimitiveInnerRadius = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveInnerRadius, ToMeters(geometry.InnerRadiusCm)),
                PrimitiveOuterRadius = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveOuterRadius, ToMeters(geometry.OuterRadiusCm)),
                PrimitiveSweepAngleDeg = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveSweepAngleDeg, geometry.SweepAngleDeg),
                PrimitiveSegmentCount = Math.Max(
                    0,
                    (int)ResolveParam(
                        handle,
                        def,
                        owner,
                        WellKnownPerformerParamKeys.PrimitiveSegmentCount,
                        geometry.SegmentCount > 0 ? geometry.SegmentCount : 0)),
                PrimitiveArcHeight = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveArcHeight, ToMeters(geometry.ArcHeightCm)),
                PrimitiveControlPoint0 = new Vector2(
                    ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveControl0X, ToMeters(geometry.ControlPoint0XCm)),
                    ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveControl0Y, ToMeters(geometry.ControlPoint0YCm))),
                PrimitiveControlPoint1 = new Vector2(
                    ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveControl1X, ToMeters(geometry.ControlPoint1XCm)),
                    ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitiveControl1Y, ToMeters(geometry.ControlPoint1YCm))),
                PrimitivePulseSpeed = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitivePulseSpeed, geometry.PulseSpeed),
                PrimitivePulseAmplitude = ResolveParam(handle, def, owner, WellKnownPerformerParamKeys.PrimitivePulseAmplitude, ToMeters(geometry.PulseAmplitudeCm)),
            });
        }

        private int ResolveMarkerStableId(int handle, int definitionId, Entity owner)
        {
            return ResolvePerformerStableId(handle, definitionId, owner, PerformerVisualKind.Marker3D);
        }

        private int ResolvePerformerStableId(int handle, int definitionId, Entity owner, PerformerVisualKind visualKind)
        {
            if (handle >= 0 && _instances.IsActive(handle))
            {
                return _instances.Get(handle).StableId;
            }

            if (World.IsAlive(owner) && World.Has<PresentationStableId>(owner))
            {
                int ownerStableId = World.Get<PresentationStableId>(owner).Value;
                if (ownerStableId > 0)
                {
                    return PerformerVisualIdentity.ComposeStableId(ownerStableId, visualKind, definitionId);
                }
            }

            throw new InvalidOperationException(
                $"Entity-scoped performer '{_definitions.GetName(definitionId)}' requires a positive PresentationStableId on its owner.");
        }

        private void EmitWorldBar(int handle, int definitionId, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
        {
            var fg = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            fg.W *= alphaMod;
            var bg = ResolveColor(handle, def, owner, 8, 9, 10, 11, new Vector4(0.2f, 0.2f, 0.2f, 1f));
            bg.W *= alphaMod;
            float value = ResolveParam(handle, def, owner, 0, 1f);
            float width = ResolveParam(handle, def, owner, 1, 40f);
            float height = ResolveParam(handle, def, owner, 2, 6f);
            int stableId = ResolveHudStableId(handle, definitionId, owner, WorldHudItemKind.Bar);
            int dirtySerial = HudItemIdentity.ComposeBarDirtySerial(width, height, value, bg, fg);

            _worldHud.TryAdd(new WorldHudItem
            {
                StableId = stableId,
                DirtySerial = dirtySerial,
                Kind = WorldHudItemKind.Bar,
                WorldPosition = pos,
                Value0 = value,
                Width = width,
                Height = height,
                Color0 = bg,
                Color1 = fg,
            });
        }

        private void EmitWorldText(int handle, int definitionId, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
        {
            var color = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            color.W *= alphaMod;
            float value0 = ResolveParam(handle, def, owner, 0, 0f);
            float value1 = ResolveParam(handle, def, owner, 1, 0f);
            int textTokenId = (int)ResolveParam(handle, def, owner, 15, def.DefaultTextId);
            var legacyMode = (WorldHudValueMode)(int)ResolveParam(handle, def, owner, 16, (int)def.LegacyWorldTextMode);
            int legacyStringId = legacyMode == WorldHudValueMode.None ? textTokenId : 0;
            PresentationTextPacket packet = PresentationTextPacket.FromLegacyWorldHud(
                textTokenId,
                legacyMode,
                value0,
                value1);
            int stableId = ResolveHudStableId(handle, definitionId, owner, WorldHudItemKind.Text);
            int dirtySerial = HudItemIdentity.ComposeTextDirtySerial(
                (int)ResolveParam(handle, def, owner, 3, def.DefaultFontSize),
                legacyStringId,
                (int)legacyMode,
                value0,
                value1,
                color,
                packet);

            _worldHud.TryAdd(new WorldHudItem
            {
                StableId = stableId,
                DirtySerial = dirtySerial,
                Kind = WorldHudItemKind.Text,
                WorldPosition = pos,
                Value0 = value0,
                Value1 = value1,
                Id0 = legacyStringId,
                Id1 = (int)legacyMode, // WorldHudValueMode legacy adapter contract
                FontSize = (int)ResolveParam(handle, def, owner, 3, def.DefaultFontSize),
                Color0 = color,
                Text = packet,
            });
        }

        private int ResolveHudStableId(int handle, int definitionId, Entity owner, WorldHudItemKind kind)
        {
            int ownerStableId = 0;
            if (handle >= 0 && _instances.IsActive(handle))
            {
                ownerStableId = _instances.Get(handle).StableId;
            }
            else if (World.IsAlive(owner) && World.Has<PresentationStableId>(owner))
            {
                ownerStableId = World.Get<PresentationStableId>(owner).Value;
            }

            return ownerStableId > 0
                ? HudItemIdentity.ComposeStableId(ownerStableId, kind, definitionId)
                : 0;
        }

        // ── Helpers ──

        private Vector4 ResolveColor(int handle, PerformerDefinition def, Entity owner, int rKey, int gKey, int bKey, int aKey, Vector4 defaultColor)
        {
            return new Vector4(
                ResolveParam(handle, def, owner, rKey, defaultColor.X),
                ResolveParam(handle, def, owner, gKey, defaultColor.Y),
                ResolveParam(handle, def, owner, bKey, defaultColor.Z),
                ResolveParam(handle, def, owner, aKey, defaultColor.W));
        }

        private Vector3 ResolveOwnerPosition(Entity owner)
        {
            if (World.IsAlive(owner) && World.Has<VisualTransform>(owner))
                return World.Get<VisualTransform>(owner).Position;
            return Vector3.Zero;
        }

        private Quaternion ResolveOwnerRotation(Entity owner)
        {
            if (World.IsAlive(owner) && World.Has<VisualTransform>(owner))
            {
                return World.Get<VisualTransform>(owner).Rotation;
            }

            if (World.IsAlive(owner) && World.Has<Ludots.Core.Components.FacingDirection>(owner))
            {
                float angle = World.Get<Ludots.Core.Components.FacingDirection>(owner).AngleRad;
                return Quaternion.CreateFromAxisAngle(Vector3.UnitY, -angle);
            }

            return Quaternion.Identity;
        }

        private Vector3 ResolveAnchorPosition(in PerformerInstance instance)
        {
            return instance.AnchorKind == PresentationAnchorKind.WorldPosition
                ? instance.WorldPosition
                : ResolveOwnerPosition(instance.Owner);
        }

        private bool IsLocalPlayer(Entity entity)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var obj)) return false;
            return obj is Entity lp && lp == entity;
        }

        private bool EvaluateGraphBool(int graphProgramId, Entity source, Entity target)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program)) return false;
            if (program.Length == 0) return false;
            ExecuteGraph(source, target, program);
            return _boolRegs[0] != 0;
        }

        private float EvaluateGraphFloat(int graphProgramId, Entity owner)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program)) return 0f;
            if (program.Length == 0) return 0f;
            ExecuteGraph(owner, owner, program);
            return _floatRegs[0];
        }

        private void ExecuteGraph(Entity source, Entity target, ReadOnlySpan<GraphInstruction> program)
        {
            Array.Clear(_floatRegs, 0, _floatRegs.Length);
            Array.Clear(_intRegs, 0, _intRegs.Length);
            Array.Clear(_boolRegs, 0, _boolRegs.Length);
            Array.Clear(_entityRegs, 0, _entityRegs.Length);
            _entityRegs[0] = source;
            _entityRegs[1] = target;

            var targetList = new GraphTargetList(_targets);
            var state = new GraphExecutionState
            {
                World = World,
                Caster = source,
                ExplicitTarget = target,
                TargetPos = IntVector2.Zero,
                Api = _graphApi,
                F = _floatRegs,
                I = _intRegs,
                B = _boolRegs,
                E = _entityRegs,
                Targets = _targets,
                TargetList = targetList,
            };
            GasGraphOpHandlerTable.Execute(ref state, program, _handlers);
        }

        private static float ToMeters(int centimeters)
        {
            return centimeters * 0.01f;
        }

        private static PrimitiveDrawKind ResolvePrimitiveKind(int meshOrShapeId, ManifestationPrimitiveKind fallbackKind)
        {
            if (meshOrShapeId > 0 &&
                meshOrShapeId <= byte.MaxValue &&
                Enum.IsDefined(typeof(PrimitiveDrawKind), (byte)meshOrShapeId))
            {
                return (PrimitiveDrawKind)meshOrShapeId;
            }

            return fallbackKind switch
            {
                ManifestationPrimitiveKind.SplineBeam => PrimitiveDrawKind.SplineBeam,
                ManifestationPrimitiveKind.RingPulse => PrimitiveDrawKind.RingPulse,
                ManifestationPrimitiveKind.DiskWave => PrimitiveDrawKind.DiskWave,
                _ => PrimitiveDrawKind.Beam,
            };
        }
    }
}
