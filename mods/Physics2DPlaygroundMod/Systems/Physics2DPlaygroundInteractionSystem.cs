using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using Physics2DPlaygroundMod.Input;

namespace Physics2DPlaygroundMod.Systems
{
    public sealed class Physics2DPlaygroundInteractionSystem : ISystem<float>
    {
        private static readonly PresentationLocalBounds SpawnedBoxBounds =
            PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.5f, 0.25f, 0.5f));

        private static readonly PresentationLodProfile SpawnedBoxLodProfile =
            new(
                new PresentationLodEntry(maxDistanceCm: 8000f, minScreenCoverage01: 0.03f),
                new PresentationLodEntry(maxDistanceCm: 24000f, minScreenCoverage01: 0.008f),
                new PresentationLodEntry(maxDistanceCm: 50000f, minScreenCoverage01: 0.002f));

        private const string SpawnedBoxPerformerId = "physics2d_playground.spawned_box";
        private const int InitialSelectedScratchCapacity = 64;

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly Physics2DSimulationSystem _sim;
        private readonly List<Entity> _selectedEntities = new(1024);
        private Entity[] _selectedScratch = new Entity[InitialSelectedScratchCapacity];
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly PerformerCommandBuffer _performerCommands;
        private readonly PerformerDefinitionRegistry _performerDefinitions;
        private readonly ShapeDataStorage2D _shapeStorage;
        private readonly Physics2DSolverConfig _solverConfig;

        private Entity _chainDemoEntity;
        private bool _chainDemoInited;
        private bool _prevChainDemo;
        private int _boxShapeIndex = -1;
        private int _spawnCount = 10;
        private float _impulseMagnitude = 10f;
        private int _cueMarkerPrefabId;

        private bool _prevK1;
        private bool _prevK2;
        private bool _prevK3;
        private bool _prevK4;
        private bool _prevK5;
        private bool _prevK6;
        private bool _prevK7;
        private bool _prevK8;
        private bool _prevK9;
        private int _spawnedBoxDefinitionId;

        public Physics2DPlaygroundInteractionSystem(
            GameEngine engine,
            Physics2DSimulationSystem sim,
            ShapeDataStorage2D shapeStorage,
            Physics2DSolverConfig solverConfig)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _shapeStorage = shapeStorage ?? throw new ArgumentNullException(nameof(shapeStorage));
            _solverConfig = solverConfig ?? throw new ArgumentNullException(nameof(solverConfig));
            _stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
                ?? throw new InvalidOperationException("Physics2DPlayground requires PresentationStableIdAllocator.");
            _performerCommands = engine.GetService(CoreServiceKeys.PerformerCommandBuffer)
                ?? throw new InvalidOperationException("Physics2DPlayground requires PerformerCommandBuffer.");
            _performerDefinitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("Physics2DPlayground requires PerformerDefinitionRegistry.");
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!Physics2DPlaygroundState.Enabled)
            {
                return;
            }

            if (_boxShapeIndex < 0)
            {
                _boxShapeIndex = _shapeStorage.RegisterBox(50f, 50f);
            }

            if (!TryGetInput(out var input))
            {
                return;
            }

            HandleSpawnCountHotkeys(input);
            HandleChainDemo(input);

            if (!input.PressedThisFrame(Physics2DPlaygroundInputActions.SecondaryClick))
            {
                return;
            }

            if (!TryGetGroundPointer(input, out var worldCm))
            {
                return;
            }

            var worldMeters = new Vector2(WorldUnits.CmToM(worldCm.X), WorldUnits.CmToM(worldCm.Y));
            if (TryCollectSelectedEntities(_selectedEntities) > 0)
            {
                ApplyImpulseToSelected(worldCm);
            }
            else
            {
                SpawnBoxes(worldMeters);
            }

            EmitCueMarker(worldCm);
        }

        public void AfterUpdate(in float dt)
        {
        }

        private void HandleSpawnCountHotkeys(IInputActionReader input)
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

        private void HandleChainDemo(IInputActionReader input)
        {
            if (!ConsumePressed(Physics2DPlaygroundInputActions.ChainDemo, input, ref _prevChainDemo))
            {
                return;
            }

            int templateId = EffectTemplateIdRegistry.GetId("Effect.Preset.ApplyForce2D");
            if (templateId <= 0)
            {
                return;
            }

            EnsureChainDemoEntity(templateId);
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.EffectRequestQueue.Name, out var queueObj) ||
                queueObj is not EffectRequestQueue queue)
            {
                return;
            }

            var caller = default(EffectConfigParams);
            caller.TryAddFloat(EffectParamKeys.ForceXAttribute, 10f);
            caller.TryAddFloat(EffectParamKeys.ForceYAttribute, 0f);
            queue.Publish(new EffectRequest
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
            if (_chainDemoInited && _world.IsAlive(_chainDemoEntity))
            {
                return;
            }

            int tagId = TagRegistry.Register("Effect.ApplyForce");
            var listener = default(ResponseChainListener);
            listener.Add(tagId, ResponseType.PromptInput, priority: 1000, effectTemplateId: templateId);

            _chainDemoEntity = _world.Create(
                new VisualTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One },
                default(AttributeBuffer),
                default(ActiveEffectContainer),
                listener);
            _chainDemoInited = true;
        }

        private void EmitCueMarker(in WorldCmInt2 worldCm)
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.TransientMarkerBuffer.Name, out var markersObj) ||
                markersObj is not TransientMarkerBuffer markers)
            {
                throw new InvalidOperationException("Physics2DPlayground requires TransientMarkerBuffer for interaction cues.");
            }

            markers.TryAddPrefab(
                ResolveCueMarkerPrefabId(),
                WorldUnits.WorldCmToVisualMeters(worldCm, yMeters: 0.15f),
                new Vector3(0.3f),
                new Vector4(0.2f, 0.9f, 1f, 1f),
                0.3f);
        }

        private int ResolveCueMarkerPrefabId()
        {
            if (_cueMarkerPrefabId > 0)
            {
                return _cueMarkerPrefabId;
            }

            if (_engine.GetService(CoreServiceKeys.PresentationPrefabRegistry) is not PrefabRegistry prefabs)
            {
                throw new InvalidOperationException("Physics2DPlayground requires PresentationPrefabRegistry.");
            }

            _cueMarkerPrefabId = prefabs.GetId("cue_marker");
            if (_cueMarkerPrefabId <= 0)
            {
                throw new InvalidOperationException("Physics2DPlayground requires prefab 'cue_marker'.");
            }

            return _cueMarkerPrefabId;
        }

        private bool TryGetGroundPointer(IInputActionReader input, out WorldCmInt2 worldCm)
        {
            worldCm = default;

            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayObj) ||
                rayObj is not IScreenRayProvider rayProvider)
            {
                return false;
            }

            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.WorldSizeSpec.Name, out var worldSizeObj) ||
                worldSizeObj is not WorldSizeSpec worldSize)
            {
                return false;
            }

            var pointer = input.ReadAction<Vector2>(Physics2DPlaygroundInputActions.PointerPos);
            var ray = rayProvider.GetRay(pointer);
            return GroundRaycastUtil.TryGetGroundWorldCmBounded(in ray, worldSize, out worldCm);
        }

        private bool TryGetInput(out IInputActionReader input)
        {
            input = default!;
            return _engine.GlobalContext.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out var obj) &&
                   obj is IInputActionReader reader &&
                   (input = reader) != null;
        }

        private int TryCollectSelectedEntities(List<Entity> selected)
        {
            selected.Clear();

            int count = EntityCollectionContextRuntime.GetCurrentCount(_engine.World, _engine.GlobalContext);
            if (count <= 0)
            {
                return 0;
            }

            EnsureSelectedScratchCapacity(count);
            int written = EntityCollectionContextRuntime.CopyCurrent(
                _engine.World,
                _engine.GlobalContext,
                _selectedScratch.AsSpan(0, count));
            if (written <= 0)
            {
                return 0;
            }

            for (int i = 0; i < written; i++)
            {
                Entity entity = _selectedScratch[i];
                if (_world.IsAlive(entity))
                {
                    selected.Add(entity);
                }
            }

            return selected.Count;
        }

        private void EnsureSelectedScratchCapacity(int required)
        {
            if (_selectedScratch.Length >= required)
            {
                return;
            }

            int next = _selectedScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _selectedScratch, next);
        }

        private void ApplyImpulseToSelected(in WorldCmInt2 targetWorldCm)
        {
            var targetCm = new Vector2(targetWorldCm.X, targetWorldCm.Y);
            for (int i = 0; i < _selectedEntities.Count; i++)
            {
                var entity = _selectedEntities[i];
                if (!_world.IsAlive(entity) || !_world.TryGet(entity, out Position2D pos))
                {
                    continue;
                }

                if (_world.Has<SleepingTag>(entity))
                {
                    _world.Remove<SleepingTag>(entity);
                    if (_world.TryGet(entity, out Motion motion))
                    {
                        motion.SleepTimer = 0;
                        _world.Set(entity, motion);
                    }
                }

                if (_world.TryGet(entity, out Mass2D mass) && mass.IsStatic)
                {
                    mass.InverseMass = Fix64.OneValue;
                    mass.InverseInertia = Fix64.OneValue;
                    _world.Set(entity, mass);
                }

                ref var velocity = ref entity.Get<Velocity2D>();
                var sourceCm = pos.Value.ToVector2();
                var direction = targetCm - sourceCm;
                float lengthSq = direction.LengthSquared();
                if (lengthSq < 1e-6f)
                {
                    continue;
                }

                direction /= MathF.Sqrt(lengthSq);
                var impulseCmPerSec = Fix64Vec2.FromVector2(direction * _impulseMagnitude * WorldUnits.CmPerMeter);
                velocity.Linear = velocity.Linear + impulseCmPerSec;
            }
        }

        private void SpawnBoxes(Vector2 worldMeters)
        {
            for (int i = 0; i < _spawnCount; i++)
            {
                var offset = new Vector2(
                    (Random.Shared.NextSingle() - 0.5f) * 5f,
                    (Random.Shared.NextSingle() - 0.5f) * 5f);
                var initialVelocity = new Vector2(
                    (Random.Shared.NextSingle() - 0.5f) * 8f,
                    (Random.Shared.NextSingle() - 0.5f) * 8f);

                SpawnBox(worldMeters + offset, initialVelocity);
            }
        }

        private void SpawnBox(Vector2 worldMeters, Vector2 initialVelocityMetersPerSecond)
        {
            var worldCm = Fix64Vec2.FromFloat(worldMeters.X * WorldUnits.CmPerMeter, worldMeters.Y * WorldUnits.CmPerMeter);
            var velocityCmPerSec = Fix64Vec2.FromFloat(
                initialVelocityMetersPerSecond.X * WorldUnits.CmPerMeter,
                initialVelocityMetersPerSecond.Y * WorldUnits.CmPerMeter);

            Entity entity = _world.Create(
                new Position2D { Value = worldCm },
                new PreviousPosition2D { Value = worldCm },
                new Velocity2D { Linear = velocityCmPerSec, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = _boxShapeIndex },
                new PhysicsMaterial2D
                {
                    Friction = _solverConfig.DefaultFrictionFix64,
                    Restitution = _solverConfig.DefaultRestitutionFix64,
                    BaseDamping = _solverConfig.DefaultBaseDampingFix64
                },
                new WorldPositionCm { Value = worldCm },
                new PreviousWorldPositionCm { Value = worldCm },
                new VisualTransform
                {
                    Position = new Vector3(worldMeters.X, 0f, worldMeters.Y),
                    Rotation = Quaternion.Identity,
                    Scale = new Vector3(1f, 0.5f, 1f)
                },
                new PresentationStableId { Value = _stableIds.Allocate() },
                SpawnedBoxBounds,
                SpawnedBoxLodProfile,
                new CullState { IsVisible = true, LOD = LODLevel.High, DistanceToCameraSq = 0f },
                default(CommandSourceSelectableTag),
                CommandSourceSelectableState.EnabledByDefault);

            if (!_performerCommands.TryAdd(new PerformerCommand
                {
                    CommandKind = PerformerCommandKind.CreatePerformer,
                    PerformerDefinitionId = ResolveSpawnedBoxDefinitionId(),
                    ScopeTag = ResolveSpawnedBoxScopeId(entity),
                    Source = entity,
                    AnchorKind = PresentationAnchorKind.Entity,
                }))
            {
                throw new InvalidOperationException("Physics2DPlayground failed to queue performer creation for spawned box.");
            }
        }

        private static bool ConsumePressed(string actionId, IInputActionReader input, ref bool prevDown)
        {
            bool down = input.IsDown(actionId);
            bool pressed = down && !prevDown;
            prevDown = down;
            return pressed;
        }

        public void Dispose()
        {
        }

        private int ResolveSpawnedBoxDefinitionId()
        {
            if (_spawnedBoxDefinitionId > 0)
            {
                return _spawnedBoxDefinitionId;
            }

            _spawnedBoxDefinitionId = _performerDefinitions.GetId(SpawnedBoxPerformerId);
            if (_spawnedBoxDefinitionId <= 0)
            {
                throw new InvalidOperationException(
                    $"Performer '{SpawnedBoxPerformerId}' is required by Physics2DPlaygroundMod.");
            }

            return _spawnedBoxDefinitionId;
        }

        private static int ResolveSpawnedBoxScopeId(Entity entity)
        {
            int scopeId = HashCode.Combine(SpawnedBoxPerformerId, entity.Id, entity.WorldId, entity.Version) & int.MaxValue;
            return scopeId == 0 ? 1 : scopeId;
        }
    }
}
