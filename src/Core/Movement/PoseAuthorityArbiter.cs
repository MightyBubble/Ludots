using System;
using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Components;

namespace Ludots.Core.Movement
{
    /// <summary>
    /// 位姿写权切换提交后的通知回调（issue #643）。
    /// 由关心写权归属的运动子系统实现（例如 MassNavigation 把 displaced 态同步进求解器）。
    /// 回调在 <see cref="PoseAuthorityCommitSystem"/> 的 CommandBuffer 回放之后触发，
    /// 此时 ECS 上的 <see cref="PoseAuthority"/> 已是切换后的值。
    /// </summary>
    public interface IPoseAuthorityTransitionListener
    {
        void OnPoseAuthorityCommitted(World world, Entity entity, PoseAuthorityKind from, PoseAuthorityKind to);
    }

    /// <summary>
    /// 位姿写权仲裁器（issue #643 参与模型轴一的运行时执行点）。
    /// 记录"谁持有写权窗口、已持有多久、何时到期"；切换申请只入队，
    /// 由 <see cref="PoseAuthorityCommitSystem"/> 在固定步边界经 CommandBuffer 统一结算。
    /// 任何非法申请（缺参与声明、写权不匹配、重复申请）立即抛异常，无静默回退。
    /// </summary>
    public sealed class PoseAuthorityArbiter
    {
        private struct WindowState
        {
            public Entity Entity;
            public PoseAuthorityKind Holder;
            public float HeldSeconds;
            public int MaxDurationMs;
        }

        private struct PendingTransition
        {
            public Entity Entity;
            public PoseAuthorityKind From;
            public PoseAuthorityKind To;
            public int MaxDurationMs;
        }

        private readonly List<WindowState> _activeWindows = new();
        private readonly List<PendingTransition> _pending = new();
        private readonly List<IPoseAuthorityTransitionListener> _listeners = new();
        private bool _committing;

        public int ActiveWindowCount => _activeWindows.Count;
        public int PendingTransitionCount => _pending.Count;

        public void AddListener(IPoseAuthorityTransitionListener listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            if (_listeners.Contains(listener))
            {
                throw new InvalidOperationException("PoseAuthorityArbiter listener is already registered.");
            }

            _listeners.Add(listener);
        }

        /// <summary>
        /// 申请把实体写权从 Nav 切到 Displacement（GAS 位移窗口开启）。
        /// 切换在下一个固定步边界生效；窗口上限取自实体的 MovementParticipation 声明。
        /// </summary>
        public void RequestDisplacementAuthority(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ThrowIfCommitting();
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot open a displacement window for dead entity {entity.Id}.");
            }

            if (!world.Has<MovementParticipation>(entity) || !world.Has<PoseAuthority>(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter requires MovementParticipation and PoseAuthority on entity {entity.Id} before opening a displacement window.");
            }

            MovementParticipation participation = world.Get<MovementParticipation>(entity);
            if (!participation.DisplacementAllowed)
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot open a displacement window for entity {entity.Id}: MovementParticipation.displacement.allowed is false.");
            }

            PoseAuthority authority = world.Get<PoseAuthority>(entity);
            if (authority.Value != PoseAuthorityKind.Nav)
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot open a displacement window for entity {entity.Id}: current pose authority is {authority.Value}, expected Nav.");
            }

            if (TryFindWindowIndex(entity, out _) || HasPendingTransition(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter already tracks a window or pending transition for entity {entity.Id}.");
            }

            _pending.Add(new PendingTransition
            {
                Entity = entity,
                From = PoseAuthorityKind.Nav,
                To = PoseAuthorityKind.Displacement,
                MaxDurationMs = participation.DisplacementMaxDurationMs,
            });
        }

        /// <summary>
        /// 申请把实体写权从 Displacement 交还 Nav（位移窗口正常结束）。
        /// 切换在下一个固定步边界生效。
        /// </summary>
        public void RequestNavHandback(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ThrowIfCommitting();
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot hand back pose authority for dead entity {entity.Id}.");
            }

            if (!TryFindWindowIndex(entity, out int windowIndex) ||
                _activeWindows[windowIndex].Holder != PoseAuthorityKind.Displacement)
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter has no active displacement window for entity {entity.Id} to hand back.");
            }

            if (HasPendingTransition(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter already has a pending transition for entity {entity.Id}.");
            }

            PoseAuthority authority = world.Get<PoseAuthority>(entity);
            if (authority.Value != PoseAuthorityKind.Displacement)
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot hand back entity {entity.Id}: current pose authority is {authority.Value}, expected Displacement.");
            }

            _pending.Add(new PendingTransition
            {
                Entity = entity,
                From = PoseAuthorityKind.Displacement,
                To = PoseAuthorityKind.Nav,
                MaxDurationMs = 0,
            });
        }

        /// <summary>查询实体当前的写权窗口（谁持有、已持有多久、上限多少毫秒）。</summary>
        public bool TryGetWindow(Entity entity, out PoseAuthorityKind holder, out float heldSeconds, out int maxDurationMs)
        {
            if (TryFindWindowIndex(entity, out int index))
            {
                WindowState window = _activeWindows[index];
                holder = window.Holder;
                heldSeconds = window.HeldSeconds;
                maxDurationMs = window.MaxDurationMs;
                return true;
            }

            holder = default;
            heldSeconds = 0f;
            maxDurationMs = 0;
            return false;
        }

        /// <summary>
        /// 固定步边界结算：把排队的写权切换经 CommandBuffer 一次性生效，然后通知监听者。
        /// 仅供 <see cref="PoseAuthorityCommitSystem"/> 调用。
        /// </summary>
        internal void CommitPendingTransitions(World world, CommandBuffer commandBuffer)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            _committing = true;
            try
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    PendingTransition transition = _pending[i];
                    if (!world.IsAlive(transition.Entity))
                    {
                        throw new InvalidOperationException(
                            $"PoseAuthorityArbiter cannot commit {transition.From}->{transition.To} for entity {transition.Entity.Id}: entity died before the fixed-step boundary.");
                    }

                    PoseAuthority authority = world.Get<PoseAuthority>(transition.Entity);
                    if (authority.Value != transition.From)
                    {
                        throw new InvalidOperationException(
                            $"PoseAuthorityArbiter cannot commit {transition.From}->{transition.To} for entity {transition.Entity.Id}: current pose authority is {authority.Value}.");
                    }

                    commandBuffer.Set(transition.Entity, new PoseAuthority { Value = transition.To });
                }

                commandBuffer.Playback(world);

                for (int i = 0; i < _pending.Count; i++)
                {
                    PendingTransition transition = _pending[i];
                    if (transition.To == PoseAuthorityKind.Displacement)
                    {
                        _activeWindows.Add(new WindowState
                        {
                            Entity = transition.Entity,
                            Holder = PoseAuthorityKind.Displacement,
                            HeldSeconds = 0f,
                            MaxDurationMs = transition.MaxDurationMs,
                        });
                    }
                    else if (TryFindWindowIndex(transition.Entity, out int windowIndex))
                    {
                        _activeWindows.RemoveAt(windowIndex);
                    }

                    for (int listenerIndex = 0; listenerIndex < _listeners.Count; listenerIndex++)
                    {
                        _listeners[listenerIndex].OnPoseAuthorityCommitted(
                            world,
                            transition.Entity,
                            transition.From,
                            transition.To);
                    }
                }

                _pending.Clear();
            }
            finally
            {
                _committing = false;
            }
        }

        /// <summary>
        /// 固定步边界推进已持有窗口的时钟；超过窗口声明的 maxDurationMs 直接抛异常（fail-fast 兜底上限）。
        /// 仅供 <see cref="PoseAuthorityCommitSystem"/> 调用。
        /// </summary>
        internal void AdvanceActiveWindows(World world, float dt)
        {
            if (dt <= 0f || _activeWindows.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _activeWindows.Count; i++)
            {
                WindowState window = _activeWindows[i];
                if (!world.IsAlive(window.Entity))
                {
                    throw new InvalidOperationException(
                        $"PoseAuthorityArbiter window holder entity {window.Entity.Id} died while holding {window.Holder} pose authority.");
                }

                window.HeldSeconds += dt;
                if (window.HeldSeconds * 1000f > window.MaxDurationMs)
                {
                    throw new InvalidOperationException(
                        $"PoseAuthority displacement window for entity {window.Entity.Id} exceeded MovementParticipation.displacement.maxDurationMs {window.MaxDurationMs} (held {window.HeldSeconds * 1000f:0.###} ms).");
                }

                _activeWindows[i] = window;
            }
        }

        private bool TryFindWindowIndex(Entity entity, out int index)
        {
            for (int i = 0; i < _activeWindows.Count; i++)
            {
                if (_activeWindows[i].Entity == entity)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private bool HasPendingTransition(Entity entity)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Entity == entity)
                {
                    return true;
                }
            }

            return false;
        }

        private void ThrowIfCommitting()
        {
            if (_committing)
            {
                throw new InvalidOperationException(
                    "PoseAuthorityArbiter transitions cannot be requested while a fixed-step boundary commit is in progress.");
            }
        }
    }
}
