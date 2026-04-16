using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Emits presentation requests for active performer instances.
    /// Performer instances are the single runtime truth for persistent presentation.
    /// </summary>
    public delegate Vector4 EntityColorResolver(World world, Entity entity);

    public sealed class PerformerEmitSystem : BaseSystem<World, float>
    {
        private readonly PerformerInstanceBuffer _instances;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PresentationRequestBuffer _requests;
        private readonly GraphProgramRegistry _programs;
        private readonly IGraphRuntimeApi _graphApi;
        private readonly Dictionary<string, object> _globals;
        private readonly EntityColorResolver _entityColorResolver;

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
            PresentationRequestBuffer requests,
            GraphProgramRegistry programs,
            IGraphRuntimeApi graphApi,
            Dictionary<string, object> globals,
            EntityColorResolver entityColorResolver = null)
            : base(world)
        {
            _instances = instances;
            _definitions = definitions;
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _programs = programs;
            _graphApi = graphApi;
            _globals = globals;
            _entityColorResolver = entityColorResolver;
        }

        public override void Update(in float dt)
        {
            _instances.ProcessActive(dt, (int handle, ref PerformerInstance inst) =>
            {
                if (!_definitions.TryGet(inst.DefId, out var def))
                {
                    return;
                }

                if (inst.AnchorKind == PresentationAnchorKind.Entity && !World.IsAlive(inst.Owner))
                {
                    _instances.Release(handle);
                    return;
                }

                if (def.DefaultLifetime > 0f && inst.Elapsed >= def.DefaultLifetime)
                {
                    _instances.Release(handle);
                    return;
                }

                if (!EvaluateVisibility(def, inst.Owner))
                {
                    return;
                }

                Vector3 pos = ResolveAnchorPosition(in inst) + def.PositionOffset;
                pos.Y += def.PositionYDriftPerSecond * inst.Elapsed;

                float alphaMod = 1f;
                if (def.AlphaFadeOverLifetime && def.DefaultLifetime > 0f)
                {
                    alphaMod = Math.Clamp(1f - inst.Elapsed / def.DefaultLifetime, 0f, 1f);
                }

                EmitForVisualKind(handle, inst.DefId, def, inst.Owner, pos, alphaMod, ResolveOwnerLod(inst.Owner));
            });
        }

        private void EmitForVisualKind(int handle, int definitionId, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod, LODLevel lod)
        {
            switch (def.VisualKind)
            {
                case PerformerVisualKind.GroundOverlay:
                    EmitGroundOverlay(handle, def, owner, pos, alphaMod, lod);
                    break;
                case PerformerVisualKind.Marker3D:
                    EmitMarker3D(handle, def, owner, pos, alphaMod, lod);
                    break;
                case PerformerVisualKind.WorldBar:
                    EmitWorldBar(handle, definitionId, def, owner, pos, alphaMod, lod);
                    break;
                case PerformerVisualKind.WorldText:
                    EmitWorldText(handle, definitionId, def, owner, pos, alphaMod, lod);
                    break;
                case PerformerVisualKind.RoadSpline:
                    EmitRoadSpline(handle, definitionId, def, owner, pos, alphaMod, lod);
                    break;
            }
        }

        private bool EvaluateVisibility(PerformerDefinition def, Entity owner)
        {
            ref readonly var cond = ref def.VisibilityCondition;
            if (cond.Inline != InlineConditionKind.None)
            {
                return EvaluateInlineVisibility(cond.Inline, owner);
            }

            if (cond.GraphProgramId > 0)
            {
                return EvaluateGraphBool(cond.GraphProgramId, owner, owner);
            }

            return true;
        }

        private bool EvaluateInlineVisibility(InlineConditionKind kind, Entity owner)
        {
            switch (kind)
            {
                case InlineConditionKind.None:
                    return true;
                case InlineConditionKind.SourceIsLocalPlayer:
                case InlineConditionKind.TargetIsLocalPlayer:
                    return IsLocalPlayer(owner);
                case InlineConditionKind.SourceIsAlive:
                case InlineConditionKind.TargetIsAlive:
                    return World.IsAlive(owner);
                case InlineConditionKind.OwnerCullVisible:
                    if (!World.IsAlive(owner))
                    {
                        return false;
                    }

                    if (!World.Has<CullState>(owner))
                    {
                        return true;
                    }

                    return World.Get<CullState>(owner).IsVisible;
                default:
                    return true;
            }
        }

        private float ResolveParam(int handle, PerformerDefinition def, Entity owner, int paramKey, float defaultValue)
        {
            if (handle >= 0 && _instances.TryGetParamOverride(handle, paramKey, out float ov))
            {
                return ov;
            }

            var idx = def.BindingIndex;
            if (paramKey >= 0 && paramKey < idx.Length)
            {
                int bi = idx[paramKey];
                if (bi >= 0)
                {
                    return ResolveValueRef(in def.Bindings[bi].Value, owner);
                }
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
                    {
                        return attrVal;
                    }

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
            if (_entityColorResolver == null)
            {
                return 1f;
            }

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
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner))
            {
                return 1f;
            }

            ref var attr = ref World.Get<AttributeBuffer>(owner);
            float current = attr.GetCurrent(attributeId);
            float max = attr.GetBase(attributeId);
            if (max <= 0f)
            {
                max = 1f;
            }

            return Math.Clamp(current / max, 0f, 1f);
        }

        private float ResolveAttributeBase(Entity owner, int attributeId)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner))
            {
                return 0f;
            }

            ref var attr = ref World.Get<AttributeBuffer>(owner);
            float max = attr.GetBase(attributeId);
            return max <= 0f ? 1f : max;
        }

        private void EmitGroundOverlay(int handle, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod, LODLevel lod)
        {
            var fc = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            fc.W *= alphaMod;

            var item = new GroundOverlayItem
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
            };
            _requests.Add(PresentationRequest.FromGroundOverlay(owner, item, lod));
        }

        private void EmitMarker3D(int handle, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod, LODLevel lod)
        {
            float scaleUniform = ResolveParam(handle, def, owner, 0, def.DefaultScale);
            float sx = ResolveParam(handle, def, owner, 1, scaleUniform);
            float sy = ResolveParam(handle, def, owner, 2, scaleUniform);
            float sz = ResolveParam(handle, def, owner, 3, scaleUniform);
            var color = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            color.W *= alphaMod;
            int stableId = ResolveMarkerStableId(handle, def.Id, owner);

            var proxy = new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                MeshAssetId = def.MeshOrShapeId,
                Position = pos,
                Rotation = Quaternion.Identity,
                Scale = new Vector3(sx, sy, sz),
                Color = color,
                StableId = stableId,
                RenderPath = VisualRenderPath.StaticMesh,
                Mobility = VisualMobility.Movable,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = lod == LODLevel.Culled ? VisualVisibility.Culled : VisualVisibility.Visible,
                LOD = lod,
            };
            _requests.Add(PresentationRequest.FromVisualProxy(owner, proxy));
        }

        private void EmitRoadSpline(int handle, int definitionId, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod, LODLevel lod)
        {
            Vector3 control0 = pos + new Vector3(
                ResolveParam(handle, def, owner, 0, 0f),
                ResolveParam(handle, def, owner, 1, 0f),
                ResolveParam(handle, def, owner, 2, 0f));
            Vector3 control1 = pos + new Vector3(
                ResolveParam(handle, def, owner, 3, 0f),
                ResolveParam(handle, def, owner, 4, 0f),
                ResolveParam(handle, def, owner, 5, 0f));
            Vector3 end = pos + new Vector3(
                ResolveParam(handle, def, owner, 6, 0f),
                ResolveParam(handle, def, owner, 7, 0f),
                ResolveParam(handle, def, owner, 8, 0f));

            float width = ResolveParam(handle, def, owner, 12, def.DefaultScale > 0f ? def.DefaultScale : 0.25f);
            float borderWidth = ResolveParam(handle, def, owner, 13, 0.03f);
            byte style = (byte)Math.Clamp((int)ResolveParam(handle, def, owner, 14, 0f), 0, byte.MaxValue);

            Vector4 fill = ResolveColor(handle, def, owner, 20, 21, 22, 23, def.DefaultColor);
            fill.W *= alphaMod;
            Vector4 border = ResolveColor(handle, def, owner, 24, 25, 26, 27, new Vector4(1f, 1f, 1f, 1f));
            border.W *= alphaMod;

            int stableId = ResolveRoadSplineStableId(handle, definitionId, owner);
            _requests.Add(PresentationRequest.FromRoadSpline(owner, new RoadSplineRequest
            {
                StableId = stableId,
                P0 = pos,
                P1 = control0,
                P2 = control1,
                P3 = end,
                Width = width,
                FillColor = fill,
                BorderColor = border,
                BorderWidth = borderWidth,
                Style = style,
            }, lod));
        }

        private int ResolveMarkerStableId(int handle, int definitionId, Entity owner)
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
                    return PerformerVisualIdentity.ComposeStableId(ownerStableId, PerformerVisualKind.Marker3D, definitionId);
                }
            }

            throw new InvalidOperationException(
                $"Performer '{_definitions.GetName(definitionId)}' requires a positive PresentationStableId on its owner.");
        }

        private int ResolveRoadSplineStableId(int handle, int definitionId, Entity owner)
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
                    return PerformerVisualIdentity.ComposeStableId(ownerStableId, PerformerVisualKind.RoadSpline, definitionId);
                }
            }

            throw new InvalidOperationException(
                $"Performer '{_definitions.GetName(definitionId)}' requires a positive PresentationStableId on its owner.");
        }

        private void EmitWorldBar(int handle, int definitionId, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod, LODLevel lod)
        {
            if (TryGetRenderDebugState(out var debug) && !debug.DrawWorldHudBars)
            {
                return;
            }

            var fg = ResolveColor(handle, def, owner, 4, 5, 6, 7, def.DefaultColor);
            fg.W *= alphaMod;
            var bg = ResolveColor(handle, def, owner, 8, 9, 10, 11, new Vector4(0.2f, 0.2f, 0.2f, 1f));
            bg.W *= alphaMod;
            float value = ResolveParam(handle, def, owner, 0, 1f);
            float width = ResolveParam(handle, def, owner, 1, 40f);
            float height = ResolveParam(handle, def, owner, 2, 6f);
            int stableId = ResolveHudStableId(handle, definitionId, owner, WorldHudItemKind.Bar);
            int dirtySerial = HudItemIdentity.ComposeBarDirtySerial(width, height, value, bg, fg);

            var item = new WorldHudItem
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
            };
            _requests.Add(PresentationRequest.FromWorldHud(owner, item, lod));
        }

        private void EmitWorldText(int handle, int definitionId, PerformerDefinition def, Entity owner, Vector3 pos, float alphaMod, LODLevel lod)
        {
            if (TryGetRenderDebugState(out var debug) && !debug.DrawWorldHudText)
            {
                return;
            }

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

            var item = new WorldHudItem
            {
                StableId = stableId,
                DirtySerial = dirtySerial,
                Kind = WorldHudItemKind.Text,
                WorldPosition = pos,
                Value0 = value0,
                Value1 = value1,
                Id0 = legacyStringId,
                Id1 = (int)legacyMode,
                FontSize = (int)ResolveParam(handle, def, owner, 3, def.DefaultFontSize),
                Color0 = color,
                Text = packet,
            };
            _requests.Add(PresentationRequest.FromWorldHud(owner, item, lod));
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
            {
                return World.Get<VisualTransform>(owner).Position;
            }

            return Vector3.Zero;
        }

        private Vector3 ResolveAnchorPosition(in PerformerInstance instance)
        {
            return instance.AnchorKind == PresentationAnchorKind.WorldPosition
                ? instance.WorldPosition
                : ResolveOwnerPosition(instance.Owner);
        }

        private LODLevel ResolveOwnerLod(Entity owner)
        {
            if (!World.IsAlive(owner) || !World.Has<CullState>(owner))
            {
                return LODLevel.High;
            }

            return World.Get<CullState>(owner).LOD;
        }

        private bool IsLocalPlayer(Entity entity)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var obj))
            {
                return false;
            }

            return obj is Entity lp && lp == entity;
        }

        private bool TryGetRenderDebugState(out RenderDebugState debug)
        {
            if (_globals != null &&
                _globals.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out var obj) &&
                obj is RenderDebugState state)
            {
                debug = state;
                return true;
            }

            debug = null;
            return false;
        }

        private bool EvaluateGraphBool(int graphProgramId, Entity source, Entity target)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program) || program.Length == 0)
            {
                return false;
            }

            ExecuteGraph(source, target, program);
            return _boolRegs[0] != 0;
        }

        private float EvaluateGraphFloat(int graphProgramId, Entity owner)
        {
            if (!_programs.TryGetProgram(graphProgramId, out var program) || program.Length == 0)
            {
                return 0f;
            }

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
