using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Gameplay.Lifecycle
{
    /// <summary>
    /// Layer 1 transaction executor host. Dequeues lifecycle requests and runs op programs.
    /// </summary>
    public sealed class RuntimeEntityLifecycleSystem : BaseSystem<World, float>
    {
        private readonly RuntimeEntityLifecycleQueue _requests;
        private readonly RuntimeEntityLifecycleReceiptQueue? _receipts;
        private readonly EffectRequestQueue? _effectRequests;
        private readonly EntityLifecycleRuntimeServices _services;

        public RuntimeEntityLifecycleSystem(
            World world,
            RuntimeEntityLifecycleQueue requests,
            EntityLifecycleRuntimeServices services,
            EffectRequestQueue? effectRequests = null,
            RuntimeEntityLifecycleReceiptQueue? receipts = null)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _effectRequests = effectRequests;
            _receipts = receipts;
        }

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
            : this(
                world,
                requests,
                new EntityLifecycleRuntimeServices(
                    world,
                    templateRegistry,
                    templateKeys,
                    stableIds,
                    selection,
                    performerRuntime,
                    performerDefinitions,
                    authoringContext),
                effectRequests,
                receipts)
        {
        }

        public EntityLifecycleRuntimeServices Services => _services;

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
            if (string.IsNullOrWhiteSpace(request.TargetTemplateId))
            {
                throw new InvalidOperationException("DeployConsumeSource requires a non-empty TargetTemplateId.");
            }

            if (!LifecyclePlacementResolver.TryResolveAtTargetPoint(World, in request, out Fix64Vec2 positionCm))
            {
                throw new LifecycleExecutionException(
                    "DeployConsumeSource failed because target point could not be resolved.");
            }

            var state = new LifecycleTransactionState
            {
                Source = request.Source,
                TargetTemplateId = request.TargetTemplateId,
                PlacementCm = positionCm,
                Snapshot = LifecycleSnapshot.Capture(World, request.Source),
            };
            RuntimeEntityLifecycleTransactionExecutor.ConfigureDeployConsumeSourceDefaults(state);

            Entity target = RuntimeEntityLifecycleTransactionExecutor.Execute(
                _services,
                state,
                LifecycleTransactionPrograms.DeployConsumeSource);

            PublishOnCompleteEffect(in request, target);
            return target;
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
    }
}
