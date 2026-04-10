using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using MassFlowNavPlaygroundMod.Components;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundPrimitiveRenderSystem : ISystem<float>
    {
        private static readonly QueryDescription UnitQuery = new QueryDescription()
            .WithAll<MassFlowNavPlaygroundEntityTag, NavAgent2D, Team, VisualTransform, CullState>()
            .WithNone<NavObstacle2D>();

        private static readonly QueryDescription ObstacleQuery = new QueryDescription()
            .WithAll<MassFlowNavPlaygroundEntityTag, NavObstacle2D, NavKinematics2D, VisualTransform, CullState>();

        private static readonly Vector4 FriendlyColor = new(0.24f, 0.86f, 0.34f, 1f);
        private static readonly Vector4 EnemyColor = new(0.90f, 0.24f, 0.24f, 1f);
        private static readonly Vector4 SelectedColor = new(0.98f, 0.84f, 0.32f, 1f);
        private static readonly Vector4 ManualColor = new(0.33f, 0.78f, 0.98f, 1f);
        private static readonly Vector4 ObstacleColor = new(0.66f, 0.72f, 0.80f, 1f);

        private readonly GameEngine _engine;
        private readonly World _world;
        private Entity[] _selectedScratch = Array.Empty<Entity>();
        private int _selectedCount;
        private int _sphereMeshAssetId;

        public MassFlowNavPlaygroundPrimitiveRenderSystem(GameEngine engine)
        {
            _engine = engine;
            _world = engine.World;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (_engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive ||
                !string.Equals(_engine.CurrentMapSession?.MapId.Value, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase) ||
                _engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) is not PrimitiveDrawBuffer primitives ||
                _engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) is not MeshAssetRegistry meshRegistry)
            {
                return;
            }

            if (_sphereMeshAssetId <= 0)
            {
                _sphereMeshAssetId = meshRegistry.GetId(WellKnownMeshKeys.Sphere);
            }

            if (_sphereMeshAssetId <= 0)
            {
                return;
            }

            CacheSelection();
            EmitUnits(primitives);
            EmitSelectedUnits(primitives);
            EmitObstacles(primitives);
        }

        private void CacheSelection()
        {
            _selectedCount = SelectionContextRuntime.GetCurrentCount(_world, _engine.GlobalContext);
            if (_selectedCount <= 0)
            {
                return;
            }

            EnsureSelectedCapacity(_selectedCount);
            _selectedCount = SelectionContextRuntime.CopyCurrentSelection(_world, _engine.GlobalContext, _selectedScratch);
        }

        private void EmitUnits(PrimitiveDrawBuffer primitives)
        {
            foreach (ref var chunk in _world.Query(in UnitQuery))
            {
                var teams = chunk.GetSpan<Team>();
                var transforms = chunk.GetSpan<VisualTransform>();
                var culls = chunk.GetSpan<CullState>();
                bool hasManualGoal = chunk.Has<MassFlowNavManualGoalTag>();
                ref Entity entityFirst = ref chunk.Entity(0);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!culls[i].IsVisible)
                    {
                        continue;
                    }

                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, i);
                    Vector4 color = ResolveBaseUnitColor(hasManualGoal, teams[i].Id);
                    primitives.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = _sphereMeshAssetId,
                        Position = transforms[i].Position,
                        Rotation = Quaternion.Identity,
                        Scale = new Vector3(MassFlowNavPlaygroundIds.UnitPrimitiveScale),
                        Color = color,
                        StableId = 0,
                        MaterialId = 0,
                        TemplateId = 0,
                        AnimationProfileId = 0,
                        RenderPath = VisualRenderPath.None,
                        Mobility = VisualMobility.Movable,
                        Flags = VisualRuntimeFlags.Visible,
                        Visibility = VisualVisibility.Visible,
                    });
                }
            }
        }

        private void EmitSelectedUnits(PrimitiveDrawBuffer primitives)
        {
            for (int i = 0; i < _selectedCount; i++)
            {
                Entity entity = _selectedScratch[i];
                if (!_world.IsAlive(entity) ||
                    !_world.TryGet(entity, out VisualTransform transform) ||
                    !_world.TryGet(entity, out CullState cull) ||
                    !cull.IsVisible)
                {
                    continue;
                }

                primitives.TryAdd(new PrimitiveDrawItem
                {
                    MeshAssetId = _sphereMeshAssetId,
                    Position = transform.Position,
                    Rotation = Quaternion.Identity,
                    Scale = new Vector3(MassFlowNavPlaygroundIds.UnitPrimitiveScale * 1.12f),
                    Color = SelectedColor,
                    StableId = 0,
                    MaterialId = 0,
                    TemplateId = 0,
                    AnimationProfileId = 0,
                    RenderPath = VisualRenderPath.None,
                    Mobility = VisualMobility.Movable,
                    Flags = VisualRuntimeFlags.Visible,
                    Visibility = VisualVisibility.Visible,
                });
            }
        }

        private void EmitObstacles(PrimitiveDrawBuffer primitives)
        {
            foreach (ref var chunk in _world.Query(in ObstacleQuery))
            {
                var kinematics = chunk.GetSpan<NavKinematics2D>();
                var transforms = chunk.GetSpan<VisualTransform>();
                var culls = chunk.GetSpan<CullState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!culls[i].IsVisible)
                    {
                        continue;
                    }

                    float scale = MathF.Max(MassFlowNavPlaygroundIds.ObstaclePrimitiveScaleMin, (kinematics[i].RadiusCm.ToFloat() * 2f) / 100f);
                    primitives.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = _sphereMeshAssetId,
                        Position = transforms[i].Position,
                        Rotation = Quaternion.Identity,
                        Scale = new Vector3(scale),
                        Color = ObstacleColor,
                        StableId = 0,
                        MaterialId = 0,
                        TemplateId = 0,
                        AnimationProfileId = 0,
                        RenderPath = VisualRenderPath.None,
                        Mobility = VisualMobility.Static,
                        Flags = VisualRuntimeFlags.Visible,
                        Visibility = VisualVisibility.Visible,
                    });
                }
            }
        }

        private static Vector4 ResolveBaseUnitColor(bool hasManualGoal, int teamId)
        {
            if (hasManualGoal)
            {
                return ManualColor;
            }

            return teamId == MassFlowNavPlaygroundIds.FriendlyTeamId ? FriendlyColor : EnemyColor;
        }

        private void EnsureSelectedCapacity(int required)
        {
            if (required <= _selectedScratch.Length)
            {
                return;
            }

            int nextSize = _selectedScratch.Length == 0 ? 16 : _selectedScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _selectedScratch, nextSize);
        }
    }
}
