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
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Gameplay.Lifecycle
{
    /// <summary>
    /// Layer 1 transaction executor for entity lifecycle presets (DeployConsumeSource).
    /// Preset semantics are compiled in code — not profile JSON.
    /// </summary>
    public sealed class RuntimeEntityLifecycleSystem : BaseSystem<World, float>
    {
        private readonly RuntimeEntityLifecycleQueue _requests;
        private readonly RuntimeEntityLifecycleReceiptQueue? _receipts;
        private readonly DataRegistry<EntityTemplate> _templateRegistry;
        private readonly EntityTemplateKeyRegistry _templateKeys;
        private readonly PresentationStableIdAllocator _stableIds;
        private readonly EffectRequestQueue? _effectRequests;
        private readonly SelectionRuntime? _selection;
        private readonly PerformerEntitySpawnBootstrap _performerBootstrap;
        private readonly Dictionary<string, EntityTemplate> _cachedTemplates = new(StringComparer.Ordinal);
        private readonly EntityBuilder _builder;
        private readonly ComponentAuthoringContext _authoringContext;

        public RuntimeEntityLifecycleSystem(
            World world,
            RuntimeEntityLifecycleQueue requests,
            DataRegistry<EntityTemplate> templateRegistry,
            EntityTemplateKeyRegistry templateKeys,
            PresentationStableIdAllocator stableIds,
            EffectRequestQueue? effectRequests = null,
            RuntimeEntityLifecycleReceiptQueue? receipts = null,
            SelectionRuntime? selection = null,
            PerformerEntityRuntime? performerRuntime = null,
            PerformerDefinitionRegistry? performerDefinitions = null,
            ComponentAuthoringContext? authoringContext = null)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _templateRegistry = templateRegistry ?? throw new ArgumentNullException(nameof(templateRegistry));
            _templateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
            _stableIds = stableIds ?? throw new ArgumentNullException(nameof(stableIds));
            _effectRequests = effectRequests;
            _receipts = receipts;
            _selection = selection;
            _authoringContext = authoringContext ?? ComponentAuthoringContext.Empty;
            _builder = new EntityBuilder(world, _cachedTemplates, _authoringContext);
            _performerBootstrap = new PerformerEntitySpawnBootstrap(
                world,
                templateKeys,
                stableIds,
                performerRuntime,
                performerDefinitions,
                performerDefinitions?.BootstrapRegistry);
        }

        public override void Update(in float dt)
        {
            while (_requests.TryDequeue(out RuntimeEntityLifecycleRequest request))
            {
                Entity target = ExecuteDeployConsumeSource(in request);
                PublishReceipt(in request, target);
            }
        }

        private Entity ExecuteDeployConsumeSource(in RuntimeEntityLifecycleRequest request)
        {
            Entity source = request.Source;
            if (!World.IsAlive(source))
            {
                throw new LifecycleExecutionException("DeployConsumeSource failed because the source entity is no longer alive.");
            }

            if (World.Has<PresentationDestroyPending>(source))
            {
                throw new LifecycleExecutionException("DeployConsumeSource failed because the source entity is already pending destroy.");
            }

            if (string.IsNullOrWhiteSpace(request.TargetTemplateId))
            {
                throw new InvalidOperationException("DeployConsumeSource requires a non-empty TargetTemplateId.");
            }

            if (!LifecyclePlacementResolver.TryResolveAtTargetPoint(World, in request, out Fix64Vec2 positionCm))
            {
                throw new LifecycleExecutionException(
                    "DeployConsumeSource failed because target point could not be resolved.");
            }

            LifecycleSnapshot snapshot = LifecycleSnapshot.CaptureDeployConsumeSource(World, source);

            Entity target = Entity.Null;
            try
            {
                target = MaterializeTarget(request.TargetTemplateId, source);
                ApplyWorldPosition(World, target, positionCm);
                LifecycleDeployConsumeSourceApplier.Apply(World, target, in snapshot);
                LifecycleDeployConsumeSourceApplier.TransferStableId(World, target, in snapshot);
                LifecycleSelectionRewire.ReplaceSource(_selection, source, target);
                PublishOnCompleteEffect(in request, target);
                PresentationEntityLifecycle.RequestDestroy(World, source, "DeployConsumeSource consume source");
                return target;
            }
            catch
            {
                RollbackMaterializedTarget(target);
                throw;
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
                PresentationEntityLifecycle.RequestDestroy(World, target, "DeployConsumeSource rollback");
                return;
            }

            World.Destroy(target);
        }

        private Entity MaterializeTarget(string templateId, Entity source)
        {
            EnsureTemplateLoaded(templateId);
            var entity = _builder
                .UseTemplate(templateId)
                .WithEntityContext($"RuntimeEntityLifecycle template '{templateId}'")
                .Build();

            ApplyTemplateKey(entity, templateId);
            if (World.Has<PresentationStableId>(entity))
            {
                World.Remove<PresentationStableId>(entity);
            }

            RuntimeEntityMapOwnershipSupport.TryCopyMapEntityFromSource(World, source, entity);
            _performerBootstrap.TryBootstrap(entity, templateId);
            return entity;
        }

        private void PublishOnCompleteEffect(in RuntimeEntityLifecycleRequest request, Entity target)
        {
            if (_effectRequests == null || request.OnCompleteEffectTemplateId <= 0)
            {
                return;
            }

            _effectRequests.Publish(new EffectRequest
            {
                RootId = 0,
                Source = target,
                Target = target,
                TargetContext = target,
                TemplateId = request.OnCompleteEffectTemplateId,
            });
        }

        private void PublishReceipt(in RuntimeEntityLifecycleRequest request, Entity target)
        {
            if (_receipts == null || request.EmitReceipt == 0)
            {
                return;
            }

            if (!_receipts.TryEnqueue(new RuntimeEntityLifecycleReceipt
            {
                ReceiptChannelId = request.ReceiptChannelId,
                ReceiptId = request.ReceiptId,
                Source = request.Source,
                Target = target,
                TargetTemplateId = request.TargetTemplateId,
            }))
            {
                throw new InvalidOperationException("RuntimeEntityLifecycleReceiptQueue capacity exceeded.");
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
    }
}
