using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Physics.Broadphase;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Platform.Abstractions;
using Physics2DPlaygroundMod.Input;

namespace Physics2DPlaygroundMod.Systems
{
    public sealed class Physics2DPlaygroundPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly Physics2DSimulationSystem _sim;
        private readonly DebugDrawCommandBuffer _buffer;
        private readonly Physics2DDebugDrawSystem _physicsDebugDraw;

        private readonly List<int> _queryResults = new List<int>(1024);
        private readonly List<Entity> _selectedEntities = new List<Entity>(1024);
        private readonly List<ImpulseViz> _impulses = new List<ImpulseViz>(256);

        private readonly QueryDescription _selectedQuery = new QueryDescription().WithAll<SelectedTag>();
        private readonly QueryDescription _allSelectableQuery = new QueryDescription().WithAll<Position2D, Collider2D>();
        private readonly QueryDescription _allBodiesQuery = new QueryDescription().WithAll<Position2D, Collider2D, Mass2D>();

        private bool _isDragging;
        private Vector2 _dragStart;
        private Vector2 _dragCurrent;

        private bool _prevLeft;
        private bool _prevRight;
        private bool _prevChainDemo;
        private Entity _chainDemoEntity;
        private bool _chainDemoInited;

        private int _boxShapeIndex = -1;
        private int _spawnCount = 10;
        private float _impulseMagnitude = 10f;

        public Physics2DPlaygroundPresentationSystem(GameEngine engine, Physics2DSimulationSystem sim, DebugDrawCommandBuffer buffer)
        {
            _engine = engine;
            _world = engine.World;
            _sim = sim;
            _buffer = buffer;
            _physicsDebugDraw = new Physics2DDebugDrawSystem(_world, _buffer);
        }

        private struct ImpulseViz
        {
            public Vector2 From;
            public Vector2 To;
            public float TimeLeft;
        }

        public void Initialize()
        {
            _physicsDebugDraw.Initialize();
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float deltaTime)
        {
            if (!Physics2DPlaygroundState.Enabled) return;

            if (_boxShapeIndex < 0)
            {
                // Box 尺寸：50cm x 50cm (半宽 = 50cm)
                _boxShapeIndex = ShapeDataStorage2D.RegisterBox(50f, 50f);
            }

            var rayProvider = TryGetScreenRayProvider();
            var input = TryGetInput();
            if (input != null && rayProvider != null)
            {
                HandleSpawnCountHotkeys(input);
                HandleInteraction(input, rayProvider);
                HandleChainDemo(input);
            }

            for (int i = _impulses.Count - 1; i >= 0; i--)
            {
                var v = _impulses[i];
                v.TimeLeft -= deltaTime;
                if (v.TimeLeft <= 0f)
                {
                    _impulses.RemoveAt(i);
                    continue;
                }
                _impulses[i] = v;
            }

            // 正式链路：插值由 Physics2DVisualSyncSystem 完成并写入 VisualTransform
            // DebugDraw 现在直接从 VisualTransform 读取位置
            _physicsDebugDraw.Update(deltaTime);

            AppendInteractionDebugDraw(_buffer);
            AppendSolidPrimitives();
        }

        public void AfterUpdate(in float t)
        {
        }

        private void AppendSolidPrimitives()
        {
            if (!_engine.GlobalContext.TryGetValue(ContextKeys.PresentationPrimitiveDrawBuffer, out var drawObj)) return;
            if (drawObj is not PrimitiveDrawBuffer draw) return;

            // 直接从 VisualTransform 读取插值后的位置（统一数据流）
            var visualQuery = new QueryDescription().WithAll<VisualTransform, Collider2D, Mass2D>();
            var chunks = _world.Query(in visualQuery);
            foreach (var chunk in chunks)
            {
                var visuals = chunk.GetArray<VisualTransform>();
                var colliders = chunk.GetArray<Collider2D>();
                var masses = chunk.GetArray<Mass2D>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    var entity = chunk.Entity(i);
                    bool isSleeping = _world.Has<SleepingTag>(entity);
                    bool isSelected = _world.Has<SelectedTag>(entity);

                    Vector4 col;
                    if (isSleeping) col = new Vector4(0.55f, 0.55f, 0.55f, 1f);
                    else if (isSelected) col = new Vector4(1f, 1f, 0f, 1f);
                    else col = masses[i].IsStatic ? new Vector4(0f, 0.47f, 1f, 1f) : new Vector4(0f, 0.86f, 0.47f, 1f);

                    ref var collider = ref colliders[i];
                    ref var visual = ref visuals[i];
                    
                    if (collider.Type == ColliderType2D.Box)
                    {
                        if (!ShapeDataStorage2D.TryGetBox(collider.ShapeDataIndex, out var box)) continue;

                        // VisualTransform.Position 已经是米 (由 WorldToVisualSyncSystem 转换)
                        // LocalCenter 是厘米，需要转换为米
                        float offsetX = box.LocalCenter.X.ToFloat() * 0.01f;
                        float offsetZ = box.LocalCenter.Y.ToFloat() * 0.01f;
                        var pos = new Vector3(visual.Position.X + offsetX, 0.25f, visual.Position.Z + offsetZ);
                        
                        // box.HalfWidth/Height 是厘米，转换为米用于渲染
                        float halfWM = box.HalfWidth.ToFloat() * 0.01f;
                        float halfHM = box.HalfHeight.ToFloat() * 0.01f;
                        var scale = new Vector3(halfWM * 2f, 0.5f, halfHM * 2f);
                        draw.TryAdd(new PrimitiveDrawItem
                        {
                            MeshAssetId = PrimitiveMeshAssetIds.Cube,
                            Position = pos,
                            Scale = scale,
                            Color = col
                        });
                    }
                    else if (collider.Type == ColliderType2D.Circle)
                    {
                        if (!ShapeDataStorage2D.TryGetCircle(collider.ShapeDataIndex, out var circle)) continue;

                        float offsetX = circle.LocalCenter.X.ToFloat() * 0.01f;
                        float offsetZ = circle.LocalCenter.Y.ToFloat() * 0.01f;
                        var pos = new Vector3(visual.Position.X + offsetX, 0.25f, visual.Position.Z + offsetZ);
                        
                        float d = circle.Radius.ToFloat() * 2f * 0.01f;  // 厘米转米
                        draw.TryAdd(new PrimitiveDrawItem
                        {
                            MeshAssetId = PrimitiveMeshAssetIds.Sphere,
                            Position = pos,
                            Scale = new Vector3(d, d, d),
                            Color = col
                        });
                    }
                }
            }
        }

        private void AppendInteractionDebugDraw(DebugDrawCommandBuffer buffer)
        {
            if (_isDragging)
            {
                var min = Vector2.Min(_dragStart, _dragCurrent);
                var max = Vector2.Max(_dragStart, _dragCurrent);

                var p0 = new Vector2(min.X, min.Y);
                var p1 = new Vector2(max.X, min.Y);
                var p2 = new Vector2(max.X, max.Y);
                var p3 = new Vector2(min.X, max.Y);

                AddLine(buffer, p0, p1, DebugDrawColor.White);
                AddLine(buffer, p1, p2, DebugDrawColor.White);
                AddLine(buffer, p2, p3, DebugDrawColor.White);
                AddLine(buffer, p3, p0, DebugDrawColor.White);
            }

            for (int i = 0; i < _impulses.Count; i++)
            {
                var v = _impulses[i];
                AddLine(buffer, v.From, v.To, DebugDrawColor.Cyan);
            }
        }

        private static void AddLine(DebugDrawCommandBuffer buffer, Vector2 a, Vector2 b, DebugDrawColor color)
        {
            buffer.Lines.Add(new DebugDrawLine2D
            {
                A = a,
                B = b,
                Thickness = 1f,
                Color = color
            });
        }

        private void HandleInteraction(PlayerInputHandler input, IScreenRayProvider rays)
        {
            if (!TryGetMouseWorld(input, rays, out var mouseWorld))
            {
                _isDragging = false;
                return;
            }

            bool left = input.ReadAction<bool>(Physics2DPlaygroundInputActions.PrimaryClick);
            bool right = input.ReadAction<bool>(Physics2DPlaygroundInputActions.SecondaryClick);

            bool leftPressed = left && !_prevLeft;
            bool leftReleased = !left && _prevLeft;
            bool rightPressed = right && !_prevRight;

            _prevLeft = left;
            _prevRight = right;

            if (leftPressed)
            {
                _isDragging = true;
                _dragStart = mouseWorld;
                _dragCurrent = mouseWorld;
            }

            if (_isDragging)
            {
                _dragCurrent = mouseWorld;
            }

            if (leftReleased)
            {
                _isDragging = false;
                HandleSelection(_dragStart, _dragCurrent);
            }

            if (rightPressed)
            {
                if (TryCollectSelectedEntities(_selectedEntities) > 0)
                {
                    ApplyImpulseToSelected(mouseWorld);
                }
                else
                {
                    SpawnBoxes(mouseWorld);
                }
            }
        }

        private void HandleSelection(Vector2 a, Vector2 b)
        {
            var min = Vector2.Min(a, b);
            var max = Vector2.Max(a, b);

            float w = max.X - min.X;
            float h = max.Y - min.Y;

            const float singleClickThreshold = 0.5f;
            if (w < singleClickThreshold && h < singleClickThreshold)
            {
                SingleSelectClosest(a);
                return;
            }

            SelectInAabb(min, max);
        }

        private void SingleSelectClosest(Vector2 point)
        {
            ClearSelection();

            Entity closest = default;
            float minDistSq = float.MaxValue;

            // point 是米，Position2D 是定点数厘米，需要转换
            var pointCm = point * 100f; // 米转厘米

            _world.Query(in _allSelectableQuery, (Entity e, ref Position2D pos, ref Collider2D _) =>
            {
                var posFloat = pos.Value.ToVector2(); // 定点数厘米转浮点厘米
                var d = posFloat - pointCm;
                float distSq = d.LengthSquared();
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    closest = e;
                }
            });

            // maxSelectDist 是米，转换为厘米进行比较
            const float maxSelectDistCm = 2f * 100f; // 2米 = 200厘米
            if (minDistSq <= maxSelectDistCm * maxSelectDistCm && _world.IsAlive(closest))
            {
                _world.Add<SelectedTag>(closest);
            }
        }

        private void SelectInAabb(Vector2 min, Vector2 max)
        {
            // min/max 是米，物理世界 AABB 是厘米，需要转换
            var minCm = min * 100f;
            var maxCm = max * 100f;
            
            var query = new Aabb
            {
                Min = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromFloat(minCm.X, minCm.Y),
                Max = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromFloat(maxCm.X, maxCm.Y)
            };

            _queryResults.Clear();
            _sim.Spatial.CurrentStrategy.QueryAABB(in query, _queryResults);

            ClearSelection();

            var entities = _sim.Build.Entities;
            for (int i = 0; i < _queryResults.Count; i++)
            {
                int idx = _queryResults[i];
                if ((uint)idx >= (uint)entities.Count) continue;
                var e = entities[idx];
                if (!_world.IsAlive(e)) continue;
                _world.Add<SelectedTag>(e);
            }
        }

        private void ClearSelection()
        {
            var chunks = _world.Query(in _selectedQuery);
            foreach (var chunk in chunks)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    _world.Remove<SelectedTag>(chunk.Entity(i));
                }
            }
        }

        private int TryCollectSelectedEntities(List<Entity> selected)
        {
            selected.Clear();
            var chunks = _world.Query(in _selectedQuery);
            foreach (var chunk in chunks)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    selected.Add(chunk.Entity(i));
                }
            }
            return selected.Count;
        }

        private void ApplyImpulseToSelected(Vector2 target)
        {
            // target 是米，需要转换为厘米
            var targetCm = target * 100f;
            
            for (int i = 0; i < _selectedEntities.Count; i++)
            {
                var e = _selectedEntities[i];
                if (!_world.IsAlive(e)) continue;
                if (!_world.TryGet(e, out Position2D pos)) continue;

                if (_world.Has<SleepingTag>(e))
                {
                    _world.Remove<SleepingTag>(e);
                    if (_world.TryGet(e, out Motion motion))
                    {
                        motion.SleepTimer = 0;
                        _world.Set(e, motion);
                    }
                }

                if (_world.TryGet(e, out Mass2D mass) && mass.IsStatic)
                {
                    mass.InverseMass = Fix64.OneValue;
                    mass.InverseInertia = Fix64.OneValue;
                    _world.Set(e, mass);
                }

                ref var vel = ref e.Get<Velocity2D>();

                // Position2D 是定点数厘米
                var posFloat = pos.Value.ToVector2();
                var dir = targetCm - posFloat;
                float lenSq = dir.LengthSquared();
                if (lenSq < 1e-6f) continue;
                dir /= MathF.Sqrt(lenSq);

                // 冲量转换为定点数厘米/秒并添加到速度
                // _impulseMagnitude 是米/秒，转换为厘米/秒
                var impulseCmPerSec = Fix64Vec2.FromVector2(dir * _impulseMagnitude * 100f);
                vel.Linear = vel.Linear + impulseCmPerSec;

                // 可视化使用米坐标
                var posMeter = posFloat * 0.01f;
                var dirMeter = dir / 100f; // 方向已经归一化，这里只是为了一致性
                _impulses.Add(new ImpulseViz
                {
                    From = posMeter,
                    To = posMeter + (dir * 0.01f) * 2.5f,
                    TimeLeft = 0.35f
                });
            }
        }

        private void SpawnBoxes(Vector2 position)
        {
            for (int i = 0; i < _spawnCount; i++)
            {
                var offset = new Vector2(
                    (Random.Shared.NextSingle() - 0.5f) * 5.0f,
                    (Random.Shared.NextSingle() - 0.5f) * 5.0f
                );

                var initialVelocity = new Vector2(
                    (Random.Shared.NextSingle() - 0.5f) * 8.0f,
                    (Random.Shared.NextSingle() - 0.5f) * 8.0f
                );

                SpawnBox(position + offset, initialVelocity);
            }
        }

        private void SpawnBox(Vector2 position, Vector2 initialVelocity)
        {
            // 统一架构（定点数版本）：
            // 1. Position2D: 物理层位置（Fix64Vec2 厘米）
            // 2. WorldPositionCm + PreviousWorldPositionCm: 逻辑层 SSOT（Fix64Vec2 厘米）
            // 3. VisualTransform: 表现层（浮点米）
            //
            // 数据流：
            //   Physics → Position2D (Fix64Vec2 厘米)
            //   Physics2DToWorldPositionSyncSystem → WorldPositionCm (直接赋值，无舍入)
            //   WorldToVisualSyncSystem → VisualTransform (插值，厘米转米)
            
            // 物理输入是米，需要转换为厘米（定点数）
            var posCmFloat = position * 100f;  // 米转厘米
            var velCmFloat = initialVelocity * 100f;  // 米/秒转厘米/秒
            
            var posCm = Fix64Vec2.FromFloat(posCmFloat.X, posCmFloat.Y);
            var velCm = Fix64Vec2.FromFloat(velCmFloat.X, velCmFloat.Y);
            
            _world.Create(
                new Position2D { Value = posCm },  // 物理位置（Fix64Vec2 厘米）
                new PreviousPosition2D { Value = posCm },
                new Velocity2D { Linear = velCm, Angular = Fix64.Zero },  // 速度（Fix64Vec2 厘米/秒）
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = _boxShapeIndex },
                PhysicsMaterial2D.Default,
                new WorldPositionCm { Value = posCm },  // 逻辑层 SSOT（Fix64Vec2 厘米）
                new PreviousWorldPositionCm { Value = posCm },
                new VisualTransform { Position = new Vector3(position.X, 0f, position.Y), Rotation = Quaternion.Identity, Scale = Vector3.One }
            );
        }

        private void HandleSpawnCountHotkeys(PlayerInputHandler input)
        {
            if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey1, input, ref _prevK1)) _spawnCount = 10;
            else if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey2, input, ref _prevK2)) _spawnCount = 20;
            else if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey3, input, ref _prevK3)) _spawnCount = 30;
            else if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey4, input, ref _prevK4)) _spawnCount = 40;
            else if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey5, input, ref _prevK5)) _spawnCount = 50;
            else if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey6, input, ref _prevK6)) _spawnCount = 60;
            else if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey7, input, ref _prevK7)) _spawnCount = 70;
            else if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey8, input, ref _prevK8)) _spawnCount = 80;
            else if (ConsumePressed(Physics2DPlaygroundInputActions.Hotkey9, input, ref _prevK9)) _spawnCount = 90;
        }
 
        private void HandleChainDemo(PlayerInputHandler input)
        {
            if (!ConsumePressed(Physics2DPlaygroundInputActions.ChainDemo, input, ref _prevChainDemo)) return;
 
            int templateId = EffectTemplateIdRegistry.GetId("Effect.Preset.ApplyForce2D");
            if (templateId <= 0) return;
 
            EnsureChainDemoEntity(templateId);
 
            if (!_engine.GlobalContext.TryGetValue(ContextKeys.EffectRequestQueue, out var qObj) || qObj is not EffectRequestQueue q) return;
            var caller = default(EffectConfigParams);
            caller.TryAddFloat(EffectParamKeys.ForceXAttribute, 10f);
            caller.TryAddFloat(EffectParamKeys.ForceYAttribute, 0f);
            q.Publish(new EffectRequest
            {
                RootId = 0,
                Source = _chainDemoEntity,
                Target = _chainDemoEntity,
                TargetContext = default,
                TemplateId = templateId,
                CallerParams = caller,
                HasCallerParams = true
            });
        }
 
        private void EnsureChainDemoEntity(int templateId)
        {
            if (_chainDemoInited && _world.IsAlive(_chainDemoEntity)) return;
 
            int tagId = TagRegistry.Register("Effect.ApplyForce");
            var listener = default(ResponseChainListener);
            listener.Add(tagId, ResponseType.PromptInput, priority: 1000, effectTemplateId: templateId);
 
            _chainDemoEntity = _world.Create(
                new VisualTransform { Position = new Vector3(0f, 0f, 0f) },
                default(AttributeBuffer),
                default(ActiveEffectContainer),
                listener
            );
            _chainDemoInited = true;
        }

        private bool _prevK1;
        private bool _prevK2;
        private bool _prevK3;
        private bool _prevK4;
        private bool _prevK5;
        private bool _prevK6;
        private bool _prevK7;
        private bool _prevK8;
        private bool _prevK9;

        private static bool ConsumePressed(string actionId, PlayerInputHandler input, ref bool prevDown)
        {
            bool down = input.ReadAction<bool>(actionId);
            bool pressed = down && !prevDown;
            prevDown = down;
            return pressed;
        }

        private static bool TryGetMouseWorld(PlayerInputHandler input, IScreenRayProvider rays, out Vector2 world)
        {
            var mouse = input.ReadAction<Vector2>(Physics2DPlaygroundInputActions.PointerPos);
            var ray = rays.GetRay(mouse);

            float denom = ray.Direction.Y;
            if (MathF.Abs(denom) < 1e-6f)
            {
                world = default;
                return false;
            }

            float t = -ray.Origin.Y / denom;
            if (t < 0)
            {
                world = default;
                return false;
            }

            var hit = ray.Origin + ray.Direction * t;
            world = new Vector2(hit.X, hit.Z);
            return true;
        }

        private PlayerInputHandler? TryGetInput()
        {
            if (_engine.GlobalContext.TryGetValue(ContextKeys.InputHandler, out var obj) && obj is PlayerInputHandler i) return i;
            return null;
        }

        private IScreenRayProvider? TryGetScreenRayProvider()
        {
            if (_engine.GlobalContext.TryGetValue(ContextKeys.ScreenRayProvider, out var obj) && obj is IScreenRayProvider p) return p;
            return null;
        }

        public void Dispose()
        {
        }
    }
}
