using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Lifecycle
{
    /// <summary>
    /// Layer 1 lifecycle request host. Publishes lifecycle effects; graph presets execute atomic transactions.
    /// </summary>
    public sealed class RuntimeEntityLifecycleSystem : BaseSystem<World, float>
    {
        private readonly RuntimeEntityLifecycleQueue _requests;
        private readonly EffectRequestQueue _effectRequests;
        private readonly RuntimeEntityLifecycleReceiptQueue? _receipts;

        public RuntimeEntityLifecycleSystem(
            World world,
            RuntimeEntityLifecycleQueue requests,
            EffectRequestQueue effectRequests,
            RuntimeEntityLifecycleReceiptQueue? receipts = null)
            : base(world)
        {
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _effectRequests = effectRequests ?? throw new ArgumentNullException(nameof(effectRequests));
            _receipts = receipts;
        }

        public override void Update(in float dt)
        {
            while (_requests.TryDequeue(out RuntimeEntityLifecycleRequest request))
            {
                PublishLifecycleEffectRequest(in request);
                PublishReceipt(in request);
            }
        }

        private void PublishLifecycleEffectRequest(in RuntimeEntityLifecycleRequest request)
        {
            if (request.EffectTemplateId <= 0)
            {
                throw new InvalidOperationException("RuntimeEntityLifecycleRequest requires EffectTemplateId.");
            }

            if (!World.IsAlive(request.Source))
            {
                throw new LifecycleExecutionException(
                    "Entity lifecycle request failed because the source entity is no longer alive.");
            }

            if (World.Has<PresentationDestroyPending>(request.Source))
            {
                throw new LifecycleExecutionException(
                    "Entity lifecycle request failed because the source entity is already pending destroy.");
            }

            if (!World.IsAlive(request.Target))
            {
                throw new LifecycleExecutionException(
                    "Entity lifecycle request failed because the target entity is no longer alive.");
            }

            if (request.TargetContext != Entity.Null && !World.IsAlive(request.TargetContext))
            {
                throw new LifecycleExecutionException(
                    "Entity lifecycle request failed because the target context entity is no longer alive.");
            }

            _effectRequests.Publish(new EffectRequest
            {
                Source = request.Source,
                Target = request.Target,
                TargetContext = request.TargetContext,
                TemplateId = request.EffectTemplateId,
                CallerParams = request.ConfigParams,
                HasCallerParams = request.ConfigParams.Count > 0,
            });
        }

        private void PublishReceipt(in RuntimeEntityLifecycleRequest request)
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
                Target = request.Target,
                EffectTemplateId = request.EffectTemplateId,
            }))
            {
                throw new InvalidOperationException("RuntimeEntityLifecycleReceiptQueue capacity exceeded.");
            }
        }
    }
}
