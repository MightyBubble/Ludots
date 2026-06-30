using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Morph
{
    public sealed class RuntimeEntityMorphSystem : BaseSystem<World, float>
    {
        private readonly RuntimeEntityMorphQueue _requests;
        private readonly RuntimeEntityMorphReceiptQueue? _receipts;
        private readonly MorphProfileRegistry _profiles;
        private readonly DataRegistry<EntityTemplate> _templateRegistry;
        private readonly EntityTemplateKeyRegistry _templateKeys;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly EffectRequestQueue? _effectRequests;
        private readonly SelectionRuntime? _selection;
        private readonly Dictionary<string, EntityTemplate> _cachedTemplates = new(StringComparer.Ordinal);
        private readonly EntityBuilder _builder;
        private readonly ComponentAuthoringContext _authoringContext;

        public RuntimeEntityMorphSystem(
            World world,
            RuntimeEntityMorphQueue requests,
            MorphProfileRegistry profiles,
            DataRegistry<EntityTemplate> templateRegistry,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator stableIds,
            EffectRequestQueue? effectRequests = null,
            RuntimeEntityMorphReceiptQueue? receipts = null,
            SelectionRuntime? selection = null,
            ComponentAuthoringContext? authoringContext = null)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            _templateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _effectRequests = effectRequests;
            _receipts = receipts;
            _selection = selection;
            _authoringContext = authoringContext ?? ComponentAuthoringContext.Empty;
            _builder = new EntityBuilder(world, _cachedTemplates, _authoringContext);
        }

        public override void Update(in float dt)
        {
            while (_requests.TryDequeue(out RuntimeEntityMorphRequest request))
            {
                Entity target = Execute(in request);
                PublishReceipt(in request, target);
            }
        }

        private Entity Execute(in RuntimeEntityMorphRequest request)
        {
            Entity source = request.Source;
            if (!World.IsAlive(source))
            {
                throw new MorphExecutionException("Entity morph failed because the source entity is no longer alive.");
            }

            if (string.IsNullOrWhiteSpace(request.TargetTemplateId))
            {
                throw new InvalidOperationException("Runtime entity morph requires a non-empty TargetTemplateId.");
            }

            if (!_profiles.TryGet(request.MorphProfileId, out MorphProfileDescriptor profile))
            {
                throw new InvalidOperationException($"Runtime entity morph references unknown morph profile id '{request.MorphProfileId}'.");
            }

            if (!MorphPlacementResolver.TryResolve(World, in request, profile.Placement, out Fix64Vec2 positionCm, out float facingAngleRad, out bool hasFacing))
            {
                throw new MorphExecutionException(
                    $"Entity morph failed because placement mode '{profile.Placement}' could not resolve a world position.");
            }

            MorphSnapshot snapshot = MorphSnapshot.Capture(World, source, in profile);
            ValidateStableIdPolicy(in snapshot, profile.StableIdPolicy);

            Entity target = Entity.Null;
            try
            {
                target = MaterializeTarget(request.TargetTemplateId);
                ApplyWorldPosition(World, target, positionCm);
                if (hasFacing)
                {
                    ApplyFacing(World, target, facingAngleRad);
                }

                ApplyStableIdPolicy(target, in snapshot, profile.StableIdPolicy);
                MorphInheritanceApplier.Apply(World, target, in snapshot, in profile);

                if (profile.ReplaceSelection)
                {
                    MorphSelectionRewire.ReplaceSource(_selection, source, target);
                }

                PublishOnMorphEffect(in request, target);

                if (profile.DestroySource)
                {
                    PresentationEntityLifecycle.RequestDestroy(World, source, "Entity morph consume source");
                }

                return target;
            }
            catch
            {
                RollbackMaterializedTarget(target);
                throw;
            }
        }

        private static void ValidateStableIdPolicy(in MorphSnapshot snapshot, MorphStableIdPolicy policy)
        {
            if (policy == MorphStableIdPolicy.Transfer && !snapshot.HasStableId)
            {
                throw new MorphExecutionException("Entity morph failed because stableIdPolicy=Transfer requires source PresentationStableId.");
            }
        }

        private void RollbackMaterializedTarget(Entity target)
        {
            if (!World.IsAlive(target))
            {
                return;
            }

            if (World.Has<PresentationStableId>(target))
            {
                PresentationEntityLifecycle.RequestDestroy(World, target, "Entity morph rollback");
                return;
            }

            World.Destroy(target);
        }

        private Entity MaterializeTarget(string templateId)
        {
            EnsureTemplateLoaded(templateId);
            var entity = _builder
                .UseTemplate(templateId)
                .WithEntityContext($"RuntimeEntityMorph template '{templateId}'")
                .Build();

            ApplyTemplateKey(entity, templateId);
            EnsurePresentationStableId(entity);
            return entity;
        }

        private void ApplyStableIdPolicy(Entity target, in MorphSnapshot snapshot, MorphStableIdPolicy policy)
        {
            switch (policy)
            {
                case MorphStableIdPolicy.Transfer:
                    if (World.Has<PresentationStableId>(target))
                    {
                        World.Set(target, new PresentationStableId { Value = snapshot.StableId });
                    }
                    else
                    {
                        World.Add(target, new PresentationStableId { Value = snapshot.StableId });
                    }

                    break;
                case MorphStableIdPolicy.AllocateNew:
                default:
                    EnsurePresentationStableId(target);
                    break;
            }
        }

        private void PublishOnMorphEffect(in RuntimeEntityMorphRequest request, Entity target)
        {
            if (_effectRequests == null || request.OnMorphEffectTemplateId <= 0)
            {
                return;
            }

            _effectRequests.Publish(new EffectRequest
            {
                RootId = 0,
                Source = target,
                Target = target,
                TargetContext = target,
                TemplateId = request.OnMorphEffectTemplateId,
            });
        }

        private void PublishReceipt(in RuntimeEntityMorphRequest request, Entity target)
        {
            if (_receipts == null || request.EmitReceipt == 0)
            {
                return;
            }

            if (!_receipts.TryEnqueue(new RuntimeEntityMorphReceipt
            {
                ReceiptChannelId = request.ReceiptChannelId,
                ReceiptId = request.ReceiptId,
                Source = request.Source,
                Target = target,
                TargetTemplateId = request.TargetTemplateId,
            }))
            {
                throw new InvalidOperationException("RuntimeEntityMorphReceiptQueue capacity exceeded.");
            }
        }

        private void EnsureTemplateLoaded(string templateId)
        {
            if (_cachedTemplates.ContainsKey(templateId))
            {
                return;
            }

            var template = _templateRegistry.Get(templateId);
            if (template == null)
            {
                throw new InvalidOperationException($"Unknown entity template '{templateId}'.");
            }

            _cachedTemplates[templateId] = template;
        }

        private void ApplyTemplateKey(Entity entity, string templateId)
        {
            int templateKeyId = _templateKeys.GetId(templateId);
            if (templateKeyId <= 0)
            {
                templateKeyId = _templateKeys.Register(templateId);
            }

            var templateKey = new EntityTemplateKeyRef { TemplateKeyId = templateKeyId };
            if (World.Has<EntityTemplateKeyRef>(entity))
            {
                World.Set(entity, templateKey);
            }
            else
            {
                World.Add(entity, templateKey);
            }
        }

        private void EnsurePresentationStableId(Entity entity)
        {
            if (World.Has<PresentationStableId>(entity))
            {
                return;
            }

            World.Add(entity, new PresentationStableId { Value = _stableIds.Allocate() });
        }

        private static void ApplyWorldPosition(World world, Entity entity, Fix64Vec2 worldPositionCm)
        {
            var position = new WorldPositionCm { Value = worldPositionCm };
            var previous = new PreviousWorldPositionCm { Value = worldPositionCm };

            if (world.Has<WorldPositionCm>(entity))
            {
                world.Set(entity, position);
            }
            else
            {
                world.Add(entity, position);
            }

            if (world.Has<PreviousWorldPositionCm>(entity))
            {
                world.Set(entity, previous);
            }
            else
            {
                world.Add(entity, previous);
            }
        }

        private static void ApplyFacing(World world, Entity entity, float facingAngleRad)
        {
            var facing = new FacingDirection { AngleRad = facingAngleRad };
            if (world.Has<FacingDirection>(entity))
            {
                world.Set(entity, facing);
            }
            else
            {
                world.Add(entity, facing);
            }
        }
    }
}
