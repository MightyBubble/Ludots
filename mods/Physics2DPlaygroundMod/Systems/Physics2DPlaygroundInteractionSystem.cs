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
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using Physics2DPlaygroundMod.Input;

namespace Physics2DPlaygroundMod.Systems
{
    public sealed class Physics2DPlaygroundInteractionSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly Physics2DSimulationSystem _sim;
        private readonly List<Entity> _selectedEntities = new(1024);

        private Entity _chainDemoEntity;
        private bool _chainDemoInited;
        private bool _prevChainDemo;
        private int _boxShapeIndex = -1;
        private int _spawnCount = 10;
        private float _impulseMagnitude = 10f;
        private int _boxTemplateId;
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

        public Physics2DPlaygroundInteractionSystem(GameEngine engine, Physics2DSimulationSystem sim)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
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
                _boxShapeIndex = ShapeDataStorage2D.RegisterBox(50f, 50f);
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
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationCommandBuffer.Name, out var commandsObj) ||
                commandsObj is not PresentationCommandBuffer commands)
            {
                throw new InvalidOperationException("Physics2DPlayground requires PresentationCommandBuffer for interaction cues.");
            }

            commands.TryAdd(new PresentationCommand
            {
                Kind = PresentationCommandKind.PlayOneShotPerformer,
                IdA = ResolveCueMarkerPrefabId(),
                Position = WorldUnits.WorldCmToVisualMeters(worldCm, yMeters: 0.15f),
                Param0 = new Vector4(0.2f, 0.9f, 1f, 1f),
                Param1 = 0.3f
            });
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

            if (_engine.GetService(CoreServiceKeys.SelectionRuntime) is not SelectionRuntime selectionRuntime)
            {
                return 0;
            }

            Entity localPlayer = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (!_world.IsAlive(localPlayer))
            {
                return 0;
            }

            int count = selectionRuntime.GetSelectionCount(localPlayer, SelectionSetKeys.LivePrimary);
            if (count <= 0)
            {
                return 0;
            }

            Entity[] members = new Entity[count];
            int written = selectionRuntime.CopySelection(localPlayer, SelectionSetKeys.LivePrimary, members);
            if (written <= 0)
            {
                return 0;
            }

            for (int i = 0; i < written; i++)
            {
                selected.Add(members[i]);
            }

            return selected.Count;
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
            int templateId = ResolveBoxTemplateId();
            var templates = _engine.GetService(CoreServiceKeys.PresentationVisualTemplateRegistry) as VisualTemplateRegistry
                ?? throw new InvalidOperationException("Physics2DPlayground requires PresentationVisualTemplateRegistry.");
            if (!templates.TryGet(templateId, out var template))
            {
                throw new InvalidOperationException($"Physics2DPlayground visual template id {templateId} is missing.");
            }

            var stableIds = _engine.GetService(CoreServiceKeys.PresentationStableIdAllocator) as PresentationStableIdAllocator
                ?? throw new InvalidOperationException("Physics2DPlayground requires PresentationStableIdAllocator.");

            var worldCm = Fix64Vec2.FromFloat(worldMeters.X * WorldUnits.CmPerMeter, worldMeters.Y * WorldUnits.CmPerMeter);
            var velocityCmPerSec = Fix64Vec2.FromFloat(
                initialVelocityMetersPerSecond.X * WorldUnits.CmPerMeter,
                initialVelocityMetersPerSecond.Y * WorldUnits.CmPerMeter);

            _world.Create(
                new Position2D { Value = worldCm },
                new PreviousPosition2D { Value = worldCm },
                new Velocity2D { Linear = velocityCmPerSec, Angular = Fix64.Zero },
                Mass2D.FromFloat(1f, 1f),
                new Collider2D { Type = ColliderType2D.Box, ShapeDataIndex = _boxShapeIndex },
                PhysicsMaterial2D.Default,
                new WorldPositionCm { Value = worldCm },
                new PreviousWorldPositionCm { Value = worldCm },
                new VisualTransform
                {
                    Position = new Vector3(worldMeters.X, 0f, worldMeters.Y),
                    Rotation = Quaternion.Identity,
                    Scale = new Vector3(1f, 0.5f, 1f)
                },
                new VisualTemplateRef { TemplateId = templateId },
                template.ToRuntimeState(),
                new PresentationStableId { Value = stableIds.Allocate() },
                new CullState { IsVisible = true, LOD = LODLevel.High, DistanceToCameraSq = 0f },
                default(SelectionSelectableTag),
                SelectionSelectableState.EnabledByDefault);
        }

        private int ResolveBoxTemplateId()
        {
            if (_boxTemplateId > 0)
            {
                return _boxTemplateId;
            }

            var templates = _engine.GetService(CoreServiceKeys.PresentationVisualTemplateRegistry) as VisualTemplateRegistry
                ?? throw new InvalidOperationException("Physics2DPlayground requires PresentationVisualTemplateRegistry.");
            _boxTemplateId = templates.GetId("physics2d.playground.box");
            if (_boxTemplateId <= 0)
            {
                throw new InvalidOperationException("Physics2DPlayground requires visual template 'physics2d.playground.box'.");
            }

            return _boxTemplateId;
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
    }
}
