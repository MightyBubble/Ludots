using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CameraAcceptanceMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceHotpathLaneSystem : ISystem<float>
    {
        private const string CrowdRequestedKey = "CameraAcceptance.HotpathCrowdRequested";

        private static readonly QueryDescription DummyNameQuery = new QueryDescription()
            .WithAll<Name, MapEntity>();

        private static readonly QueryDescription DummyVisibleQuery = new QueryDescription()
            .WithAll<Name, CullState, MapEntity>();

        private static readonly QueryDescription UntaggedDummyQuery = new QueryDescription()
            .WithAll<Name, MapEntity>()
            .WithNone<CameraAcceptanceHotpathCrowdTag>();

        private static readonly QueryDescription TaggedCrowdQuery = new QueryDescription()
            .WithAll<CameraAcceptanceHotpathCrowdTag>();

        private static readonly QueryDescription TaggedCrowdVisibleQuery = new QueryDescription()
            .WithAll<CameraAcceptanceHotpathCrowdTag, CullState>();

        private static readonly QueryDescription TaggedCrowdVisualQuery = new QueryDescription()
            .WithAll<CameraAcceptanceHotpathCrowdTag, MapEntity, VisualTransform, CullState>();

        private static readonly QueryDescription TaggedCrowdDestroyQuery = new QueryDescription()
            .WithAll<CameraAcceptanceHotpathCrowdTag>();

        private static readonly Vector4 BarBackground = new(0.14f, 0.18f, 0.24f, 0.92f);
        private static readonly Vector4 BarForeground = new(0.12f, 0.84f, 0.62f, 0.96f);
        private static readonly Vector4 TextColor = new(0.96f, 0.92f, 0.68f, 1f);

        private readonly GameEngine _engine;
        private readonly List<Entity> _tagBuffer = new(CameraAcceptanceIds.HotpathCrowdTargetCount);
        private readonly List<Entity> _destroyBuffer = new(CameraAcceptanceIds.HotpathCrowdTargetCount);
        private int _cubeMeshAssetId;
        private int _sphereMeshAssetId;
        private bool _crowdRequested;

        public CameraAcceptanceHotpathLaneSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            MapId currentMapId = _engine.CurrentMapSession?.MapId ?? default;
            if (!string.Equals(currentMapId.Value, CameraAcceptanceIds.HotpathMapId, StringComparison.OrdinalIgnoreCase))
            {
                ResetHotpathState();
                return;
            }

            if (_engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is not CameraAcceptanceDiagnosticsState diagnostics)
            {
                return;
            }

            TagExistingCrowd(currentMapId);

            int crowdCount = CountDummyCrowd(currentMapId);
            int visibleCrowdCount = CountVisibleDummyCrowd(currentMapId);
            if (!diagnostics.HotpathCullCrowdEnabled)
            {
                if (crowdCount > 0)
                {
                    DestroyDummyCrowd(currentMapId);
                }

                SetCrowdRequested(false);
                diagnostics.ObserveHotpathBars(0d);
                diagnostics.ObserveHotpathHudText(0d);
                diagnostics.ObserveHotpathPrimitives(0d);
                diagnostics.PublishHotpathLaneCounts(0, 0, 0, 0, 0);
                return;
            }

            if (crowdCount > CameraAcceptanceIds.HotpathCrowdTargetCount)
            {
                crowdCount = TrimDummyCrowdToTarget(currentMapId);
                visibleCrowdCount = CountVisibleDummyCrowd(currentMapId);
                if (_engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is RuntimeEntitySpawnQueue spawnQueue)
                {
                    spawnQueue.Clear();
                }

                SetCrowdRequested(true);
            }
            else if (!IsCrowdRequested())
            {
                EnsureCrowdSpawned(currentMapId, crowdCount);
                SetCrowdRequested(true);
            }

            int barCount = EmitBars(diagnostics, currentMapId);
            int textCount = EmitHudText(diagnostics, currentMapId);
            int primitiveCount = EmitPrimitives(diagnostics, currentMapId);
            diagnostics.PublishHotpathLaneCounts(crowdCount, visibleCrowdCount, barCount, textCount, primitiveCount);
        }

        private void ResetHotpathState()
        {
            if (_engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is not CameraAcceptanceDiagnosticsState diagnostics)
            {
                return;
            }

            diagnostics.ObserveHotpathBars(0d);
            diagnostics.ObserveHotpathHudText(0d);
            diagnostics.ObserveHotpathPrimitives(0d);
            diagnostics.PublishHotpathSelectionLabelCount(0);
            diagnostics.ResetHotpathLaneCounts();
            SetCrowdRequested(false);
        }

        private void TagExistingCrowd(MapId currentMapId)
        {
            _tagBuffer.Clear();
            _engine.World.Query(in UntaggedDummyQuery, (Entity entity, ref Name name, ref MapEntity mapEntity) =>
            {
                if (MatchesMap(mapEntity, currentMapId) &&
                    string.Equals(name.Value, "Dummy", StringComparison.OrdinalIgnoreCase))
                {
                    _tagBuffer.Add(entity);
                }
            });

            for (int i = 0; i < _tagBuffer.Count; i++)
            {
                Entity entity = _tagBuffer[i];
                if (_engine.World.IsAlive(entity) && !_engine.World.Has<CameraAcceptanceHotpathCrowdTag>(entity))
                {
                    _engine.World.Add(entity, new CameraAcceptanceHotpathCrowdTag());
                }
            }
        }

        private int CountDummyCrowd(MapId currentMapId)
        {
            int crowdCount = 0;
            _engine.World.Query(in DummyNameQuery, (ref Name name, ref MapEntity mapEntity) =>
            {
                if (MatchesMap(mapEntity, currentMapId) &&
                    string.Equals(name.Value, "Dummy", StringComparison.OrdinalIgnoreCase))
                {
                    crowdCount++;
                }
            });

            return crowdCount;
        }

        private int CountVisibleDummyCrowd(MapId currentMapId)
        {
            int visibleCount = 0;
            _engine.World.Query(in DummyVisibleQuery, (ref Name name, ref CullState cull, ref MapEntity mapEntity) =>
            {
                if (MatchesMap(mapEntity, currentMapId) &&
                    cull.IsVisible &&
                    string.Equals(name.Value, "Dummy", StringComparison.OrdinalIgnoreCase))
                {
                    visibleCount++;
                }
            });

            return visibleCount;
        }

        private void DestroyDummyCrowd(MapId currentMapId)
        {
            _destroyBuffer.Clear();
            _engine.World.Query(in DummyNameQuery, (Entity entity, ref Name name, ref MapEntity mapEntity) =>
            {
                if (MatchesMap(mapEntity, currentMapId) &&
                    string.Equals(name.Value, "Dummy", StringComparison.OrdinalIgnoreCase))
                {
                    _destroyBuffer.Add(entity);
                }
            });

            for (int i = 0; i < _destroyBuffer.Count; i++)
            {
                Entity entity = _destroyBuffer[i];
                if (_engine.World.IsAlive(entity))
                {
                    _engine.World.Destroy(entity);
                }
            }
        }

        private int TrimDummyCrowdToTarget(MapId currentMapId)
        {
            _destroyBuffer.Clear();
            _engine.World.Query(in DummyNameQuery, (Entity entity, ref Name name, ref MapEntity mapEntity) =>
            {
                if (MatchesMap(mapEntity, currentMapId) &&
                    string.Equals(name.Value, "Dummy", StringComparison.OrdinalIgnoreCase))
                {
                    _destroyBuffer.Add(entity);
                }
            });

            for (int i = _destroyBuffer.Count - 1; i >= CameraAcceptanceIds.HotpathCrowdTargetCount; i--)
            {
                Entity entity = _destroyBuffer[i];
                if (_engine.World.IsAlive(entity))
                {
                    _engine.World.Destroy(entity);
                }
            }

            return Math.Min(_destroyBuffer.Count, CameraAcceptanceIds.HotpathCrowdTargetCount);
        }

        private void EnsureCrowdSpawned(MapId currentMapId, int crowdCount)
        {
            if (crowdCount >= CameraAcceptanceIds.HotpathCrowdTargetCount)
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                throw new InvalidOperationException("RuntimeEntitySpawnQueue is required for the presentation hotpath harness.");
            }

            int remaining = CameraAcceptanceIds.HotpathCrowdTargetCount - crowdCount;
            for (int i = 0; i < remaining; i++)
            {
                WorldCmInt2 spawnWorldCm = ResolveCrowdPosition(crowdCount + i);
                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = CameraAcceptanceIds.HotpathCrowdTemplateId,
                    WorldPositionCm = Fix64Vec2.FromInt(spawnWorldCm.X, spawnWorldCm.Y),
                    MapId = currentMapId,
                };

                if (!spawnQueue.TryEnqueue(request))
                {
                    break;
                }
            }
        }

        private bool IsCrowdRequested()
        {
            if (_crowdRequested)
            {
                return true;
            }

            return _engine.GlobalContext.TryGetValue(CrowdRequestedKey, out object? value) &&
                   value is bool requested &&
                   requested;
        }

        private void SetCrowdRequested(bool requested)
        {
            _crowdRequested = requested;
            _engine.GlobalContext[CrowdRequestedKey] = requested;
        }

        private int EmitBars(CameraAcceptanceDiagnosticsState diagnostics, MapId currentMapId)
        {
            long start = Stopwatch.GetTimestamp();
            int emitted = 0;
            if (diagnostics.HotpathBarsEnabled &&
                _engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is WorldHudBatchBuffer worldHud)
            {
                _engine.World.Query(in TaggedCrowdVisualQuery, (Entity entity, ref MapEntity mapEntity, ref VisualTransform transform, ref CullState cull) =>
                {
                    if (!MatchesMap(mapEntity, currentMapId) || !cull.IsVisible)
                    {
                        return;
                    }

                    float fill = 0.28f + ((entity.Id % 9) * 0.07f);
                    if (fill > 0.98f)
                    {
                        fill = 0.98f;
                    }

                    if (worldHud.TryAdd(new WorldHudItem
                    {
                        Kind = WorldHudItemKind.Bar,
                        WorldPosition = transform.Position + new Vector3(0f, 1.65f, 0f),
                        Width = 56f,
                        Height = 7f,
                        Value0 = fill,
                        Color0 = BarBackground,
                        Color1 = BarForeground,
                    }))
                    {
                        emitted++;
                    }
                });
            }

            diagnostics.ObserveHotpathBars((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return emitted;
        }

        private int EmitHudText(CameraAcceptanceDiagnosticsState diagnostics, MapId currentMapId)
        {
            long start = Stopwatch.GetTimestamp();
            int emitted = 0;
            if (diagnostics.HotpathHudTextEnabled &&
                _engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer) is WorldHudBatchBuffer worldHud)
            {
                _engine.World.Query(in TaggedCrowdVisualQuery, (Entity entity, ref MapEntity mapEntity, ref VisualTransform transform, ref CullState cull) =>
                {
                    if (!MatchesMap(mapEntity, currentMapId) || !cull.IsVisible)
                    {
                        return;
                    }

                    if (worldHud.TryAdd(new WorldHudItem
                    {
                        Kind = WorldHudItemKind.Text,
                        WorldPosition = transform.Position + new Vector3(0f, 2.15f, 0f),
                        FontSize = 14,
                        Color0 = TextColor,
                        Value0 = 100 + (entity.Id % 900),
                        Id1 = (int)WorldHudValueMode.Constant,
                    }))
                    {
                        emitted++;
                    }
                });
            }

            diagnostics.ObserveHotpathHudText((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return emitted;
        }

        private int EmitPrimitives(CameraAcceptanceDiagnosticsState diagnostics, MapId currentMapId)
        {
            long start = Stopwatch.GetTimestamp();
            int emitted = 0;
            if (_engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug &&
                renderDebug.DrawPrimitives &&
                _engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) is PrimitiveDrawBuffer primitives &&
                _engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry) is MeshAssetRegistry meshes)
            {
                ResolvePrimitiveMeshIds(meshes);

                _engine.World.Query(in TaggedCrowdVisualQuery, (Entity entity, ref MapEntity mapEntity, ref VisualTransform transform, ref CullState cull) =>
                {
                    if (!MatchesMap(mapEntity, currentMapId) || !cull.IsVisible)
                    {
                        return;
                    }

                    int meshAssetId = (entity.Id & 1) == 0 ? _cubeMeshAssetId : _sphereMeshAssetId;
                    if (meshAssetId == 0)
                    {
                        return;
                    }

                    float scale = 0.26f + ((entity.Id % 4) * 0.04f);
                    if (primitives.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = meshAssetId,
                        Position = transform.Position + new Vector3(0f, 0.35f + ((entity.Id % 3) * 0.05f), 0f),
                        Scale = new Vector3(scale),
                        Color = ResolvePrimitiveColor(entity.Id),
                        StableId = 500000 + entity.Id,
                        RenderPath = VisualRenderPath.InstancedStaticMesh,
                        Mobility = VisualMobility.Static,
                        Flags = VisualRuntimeFlags.Visible,
                    }))
                    {
                        emitted++;
                    }
                });
            }

            diagnostics.ObserveHotpathPrimitives((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            return emitted;
        }

        private static bool MatchesMap(in MapEntity mapEntity, in MapId currentMapId)
        {
            return string.Equals(mapEntity.MapId.Value, currentMapId.Value, StringComparison.OrdinalIgnoreCase);
        }

        private void ResolvePrimitiveMeshIds(MeshAssetRegistry meshes)
        {
            if (_cubeMeshAssetId == 0)
            {
                _cubeMeshAssetId = meshes.GetId(WellKnownMeshKeys.Cube);
            }

            if (_sphereMeshAssetId == 0)
            {
                _sphereMeshAssetId = meshes.GetId(WellKnownMeshKeys.Sphere);
            }
        }

        private static Vector4 ResolvePrimitiveColor(int entityId)
        {
            float tint = (entityId % 7) / 6f;
            return new Vector4(0.22f + (tint * 0.58f), 0.38f + (tint * 0.27f), 0.86f - (tint * 0.31f), 0.94f);
        }

        private static WorldCmInt2 ResolveCrowdPosition(int index)
        {
            const int columns = 16;
            const int baseX = 1920;
            const int baseY = 980;
            const int spacingX = 170;
            const int spacingY = 150;
            int row = index / columns;
            int column = index % columns;
            int x = baseX + (column * spacingX);
            int y = baseY + (row * spacingY) + ((column & 1) == 0 ? 0 : 40);
            return new WorldCmInt2(x, y);
        }
    }
}
