using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;
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
        private readonly PrimitiveDrawBuffer _primitives;
        private readonly WorldHudBatchBuffer _worldHud;
        private readonly GraphProgramRegistry _programs;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly Dictionary<string, object> _globals;
        private readonly EntityColorResolver _entityColorResolver;

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
            EntityColorResolver entityColorResolver = null)
            : base(world)
        {
            _instances = instances;
            _definitions = definitions;
            _groundOverlays = groundOverlays;
            _primitives = primitives;
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

                // Auto-expire duration-based performers
                if (def.DefaultLifetime > 0f && inst.Elapsed >= def.DefaultLifetime)
                {
                    _instances.Release(handle);
                    return;
                }

                // Evaluate visibility
                if (!EvaluateVisibility(def, inst.Owner)) return;

                // Compute position with offset and Y-drift
                Vector3 pos = ResolveOwnerPosition(inst.Owner) + def.PositionOffset;
                pos.Y += def.PositionYDriftPerSecond * inst.Elapsed;

                // Compute alpha modulation
                float alphaMod = 1f;
                if (def.AlphaFadeOverLifetime && def.DefaultLifetime > 0f)
                    alphaMod = Math.Clamp(1f - inst.Elapsed / def.DefaultLifetime, 0f, 1f);

                EmitForVisualKind(handle, def, inst.Owner, pos, alphaMod);
            });

            // ── Part 2: Entity-scoped performers ──
            var ids = _definitions.RegisteredIds;
            for (int di = 0; di < ids.Count; di++)
            {
                if (!_definitions.TryGet(ids[di], out var def)) continue;
                if (def.EntityScope == EntityScopeFilter.None) continue;

                EmitEntityScoped(def);
            }
        }

        // ── Entity-Scoped Emission ──

        private void EmitEntityScoped(PerformerDefinition def)
        {
            switch (def.EntityScope)
            {
                case EntityScopeFilter.AllWithAttributes:
                    EmitEntityScopedWithAttr(def, _attrNoCullQuery, requireCullCheck: false);
                    EmitEntityScopedWithAttr(def, _attrWithCullQuery, requireCullCheck: true);
                    break;

                case EntityScopeFilter.AllWithVisualTransform:
                    EmitEntityScopedVT(def, _vtNoCullQuery, requireCullCheck: false);
                    EmitEntityScopedVT(def, _vtWithCullQuery, requireCullCheck: true);
                    break;
            }
        }

        private void EmitEntityScopedWithAttr(PerformerDefinition def, QueryDescription query, bool requireCullCheck)
        {
            // When requireCullCheck is true, the chunk already contains only entities WITH CullState.
            // If the definition's visibility condition is OwnerCullVisible, the chunk-level cull check
            // is semantically equivalent — skip the redundant per-entity ECS random-access lookup.
            bool skipVisibilityEval = requireCullCheck
                && def.VisibilityCondition.Inline == InlineConditionKind.OwnerCullVisible;

            var q = World.Query(in query);
            foreach (var chunk in q)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var culls = requireCullCheck ? chunk.GetArray<CullState>() : null;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (requireCullCheck && culls != null && !culls[i].IsVisible) continue;

                    if (!skipVisibilityEval)
                    {
                        var entity = chunk.Entity(i);
                        if (!EvaluateVisibility(def, entity)) continue;
                    }

                    Vector3 pos = transforms[i].Position + def.PositionOffset;
                    EmitForVisualKind(-1, def, chunk.Entity(i), pos, 1f);
                }
            }
        }

        private void EmitEntityScopedVT(PerformerDefinition def, QueryDescription query, bool requireCullCheck)
        {
            bool skipVisibilityEval = requireCullCheck
                && def.VisibilityCondition.Inline == InlineConditionKind.OwnerCullVisible;

            var q = World.Query(in query);
            foreach (var chunk in q)
            {
                var transforms = chunk.GetArray<VisualTransform>();
                var culls = requireCullCheck ? chunk.GetArray<CullState>() : null;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (requireCullCheck && culls != null && !culls[i].IsVisible) continue;

                    if (!skipVisibilityEval)
                    {
                        var entity = chunk.Entity(i);
                        if (!EvaluateVisibility(def, entity)) continue;
                    }

                    Vector3 pos = transforms[i].Position + def.PositionOffset;
                    EmitForVisualKind(-1, def, chunk.Entity(i), pos, 1f);
                }
            }
        }

        // ── Unified Emit by VisualKind ──

        private void EmitForVisualKind(int handle, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
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
                    EmitWorldBar(handle, def, owner, pos, alphaMod);
                    break;
                case PerformerVisualKind.WorldText:
                    EmitWorldText(handle, def, owner, pos, alphaMod);
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

            _primitives.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = def.MeshOrShapeId,
                Position = pos,
                Scale = new Vector3(sx, sy, sz),
                Color = color,
            });
        }

        private void EmitWorldBar(int handle, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
        {
            var fg = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            fg.W *= alphaMod;
            var bg = ResolveColor(handle, def, owner, 8, 9, 10, 11, new Vector4(0.2f, 0.2f, 0.2f, 1f));
            bg.W *= alphaMod;

            _worldHud.TryAdd(new WorldHudItem
            {
                Kind = WorldHudItemKind.Bar,
                WorldPosition = pos,
                Value0 = ResolveParam(handle, def, owner, 0, 1f),
                Width = ResolveParam(handle, def, owner, 1, 40f),
                Height = ResolveParam(handle, def, owner, 2, 6f),
                Color0 = bg,
                Color1 = fg,
            });
        }

        private void EmitWorldText(int handle, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod)
        {
            var color = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            color.W *= alphaMod;

            _worldHud.TryAdd(new WorldHudItem
            {
                Kind = WorldHudItemKind.Text,
                WorldPosition = pos,
                Value0 = ResolveParam(handle, def, owner, 0, 0f),
                Value1 = ResolveParam(handle, def, owner, 1, 0f),
                Id0 = (int)ResolveParam(handle, def, owner, 15, 0f), // string table id
                Id1 = (int)ResolveParam(handle, def, owner, 16, 0f), // WorldHudValueMode
                FontSize = (int)ResolveParam(handle, def, owner, 3, def.DefaultFontSize),
                Color0 = color,
            });
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

        private bool IsLocalPlayer(Entity entity)
        {
            if (!_globals.TryGetValue(ContextKeys.LocalPlayerEntity, out var obj)) return false;
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
    }
}
