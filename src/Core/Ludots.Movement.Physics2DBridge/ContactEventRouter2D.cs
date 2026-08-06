using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Layers;
using Ludots.Core.Physics2D;

namespace Ludots.Core.Movement.Physics2DBridge
{
    /// <summary>
    /// 碰撞事件消费入口：按 <see cref="IContactEventConsumer2D"/> 注册的 EntityLayer 接收
    /// 该 layer 参与的 Begin/End 事件。消费者异常直接上抛（不静默吞）。
    /// </summary>
    public interface IContactEventConsumer2D
    {
        void OnContactEvent(in ContactEvent2D contactEvent);
    }

    /// <summary>
    /// massnav→kinematic 桥的事件路由半边。
    ///
    /// 物理步后由 <see cref="ContactEventRoutingSystem2D"/> Drain <see cref="ContactEventQueue2D"/>，
    /// 按事件双方的 EntityLayer category 位分发给注册的消费者（每个命中 layer 的消费者各收到一次；
    /// 同一消费者注册在事件双方各自命中的两个 layer 上时会收到两次，按 layer 视角消费）。
    ///
    /// 无消费者命中的事件不静默丢弃：先校验其 layer 与 'Physics2D/kinematic.json'
    /// contactEventEmitterLayers 允许清单相交（不相交视为管线缺陷抛异常），然后按 layer 计数暴露
    /// （<see cref="GetDroppedEventCount"/> / <see cref="TotalDroppedEventCount"/>）。
    /// </summary>
    public sealed class ContactEventRouter2D
    {
        private readonly IContactEventConsumer2D?[] _consumersByLayerIndex =
            new IContactEventConsumer2D?[LayerRegistry.MaxLayers];
        private readonly long[] _droppedEventCountByLayerIndex = new long[LayerRegistry.MaxLayers];
        private readonly IReadOnlyList<string> _allowedEmitterLayerNames;

        private uint _consumerCategoryMask;
        private uint _allowedCategoryMask;
        private bool _allowedMaskResolved;

        public ContactEventRouter2D(IReadOnlyList<string> allowedEmitterLayerNames)
        {
            _allowedEmitterLayerNames = allowedEmitterLayerNames
                ?? throw new ArgumentNullException(nameof(allowedEmitterLayerNames));
        }

        /// <summary>路由不到消费者而被丢弃计数的事件总数（可观测，不静默）。</summary>
        public long TotalDroppedEventCount { get; private set; }

        public void RegisterConsumer(string layerName, IContactEventConsumer2D consumer)
        {
            ArgumentNullException.ThrowIfNull(consumer);
            int layerIndex = LayerRegistry.GetIndex(layerName);
            if (_consumersByLayerIndex[layerIndex] != null)
            {
                throw new InvalidOperationException(
                    $"ContactEventRouter2D already has a consumer registered for layer '{layerName}'; " +
                    "one consumer per layer — compose fan-out inside the consumer instead of double-registering.");
            }

            _consumersByLayerIndex[layerIndex] = consumer;
            _consumerCategoryMask |= 1u << layerIndex;
        }

        public long GetDroppedEventCount(string layerName)
        {
            return _droppedEventCountByLayerIndex[LayerRegistry.GetIndex(layerName)];
        }

        public void Dispatch(ReadOnlySpan<ContactEvent2D> events)
        {
            if (events.Length == 0)
            {
                return;
            }

            ResolveAllowedMaskOnce();

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly ContactEvent2D contactEvent = ref events[i];
                uint categories = contactEvent.LayerA.Category | contactEvent.LayerB.Category;

                uint routedBits = categories & _consumerCategoryMask;
                if (routedBits != 0u)
                {
                    while (routedBits != 0u)
                    {
                        int layerIndex = BitOperations.TrailingZeroCount(routedBits);
                        routedBits &= routedBits - 1u;
                        _consumersByLayerIndex[layerIndex]!.OnContactEvent(in contactEvent);
                    }

                    continue;
                }

                uint allowedBits = categories & _allowedCategoryMask;
                if (allowedBits == 0u)
                {
                    throw new InvalidOperationException(
                        $"ContactEventRouter2D received a {contactEvent.Type} event between entities {contactEvent.EntityA.Id} and {contactEvent.EntityB.Id} " +
                        $"whose layers (0x{categories:X8}) intersect neither a registered consumer nor the 'Physics2D/kinematic.json' contactEventEmitterLayers allowlist " +
                        $"(0x{_allowedCategoryMask:X8}); this indicates a contact event pipeline defect.");
                }

                while (allowedBits != 0u)
                {
                    int layerIndex = BitOperations.TrailingZeroCount(allowedBits);
                    allowedBits &= allowedBits - 1u;
                    _droppedEventCountByLayerIndex[layerIndex]++;
                }

                TotalDroppedEventCount++;
            }
        }

        private void ResolveAllowedMaskOnce()
        {
            if (_allowedMaskResolved)
            {
                return;
            }

            uint mask = 0u;
            for (int i = 0; i < _allowedEmitterLayerNames.Count; i++)
            {
                mask |= 1u << LayerRegistry.GetIndex(_allowedEmitterLayerNames[i]);
            }

            _allowedCategoryMask = mask;
            _allowedMaskResolved = true;
        }
    }
}
