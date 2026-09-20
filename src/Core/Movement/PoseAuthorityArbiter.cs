using System;
using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Ludots.Core.Components;

namespace Ludots.Core.Movement
{
    /// <summary>
    /// 位姿写权切换提交后的通知回调。
    /// 由关心写权归属的运动子系统实现（例如 MassNavigation 把 displaced 态同步进求解器）。
    /// 回调在 <see cref="PoseAuthorityCommitSystem"/> 的 CommandBuffer 回放之后触发，
    /// 此时 ECS 上的 <see cref="PoseAuthority"/> 已是切换后的值。
    /// </summary>
    public interface IPoseAuthorityTransitionListener
    {
        void OnPoseAuthorityCommitted(World world, Entity entity, PoseAuthorityKind from, PoseAuthorityKind to);

        /// <summary>
        /// 窗口被取消（合法异常终止：目标死亡、地图卸载/agent 解绑、结构重建）。
        /// 与正常交还不同：不保证实体存活，监听者必须自行按 id 解析并做幂等清理。
        /// </summary>
        void OnPoseAuthorityWindowCancelled(World world, Entity entity, PoseAuthorityKind holder);
    }

    /// <summary>
    /// 位姿写权仲裁器（参与模型"写权轴"的运行时执行点）。
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

        /// <summary>
        /// 申请把实体写权从 Nav 切到 Attached（attachment 绑定建立时授予）。
        /// Attached 是无限期持有（无窗口时钟），detach 时经 <see cref="RequestAttachedHandback"/> 归还。
        /// 实体当前已是 Attached（重挂接/换父）时为幂等无操作。
        /// </summary>
        public void RequestAttachedAuthority(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ThrowIfCommitting();
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot grant attached authority for dead entity {entity.Id}.");
            }

            if (!world.Has<PoseAuthority>(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter requires PoseAuthority on entity {entity.Id} before granting attached authority.");
            }

            PoseAuthority authority = world.Get<PoseAuthority>(entity);
            if (authority.Value == PoseAuthorityKind.Attached)
            {
                return;
            }

            if (authority.Value != PoseAuthorityKind.Nav)
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot grant attached authority for entity {entity.Id}: current pose authority is {authority.Value}, expected Nav or Attached.");
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
                To = PoseAuthorityKind.Attached,
                MaxDurationMs = 0,
            });
        }

        /// <summary>
        /// 申请把实体写权从 Attached 归还 Nav（attachment 解除时归还）。
        /// 切换在下一个固定步边界生效。
        /// </summary>
        public void RequestAttachedHandback(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ThrowIfCommitting();
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot hand back attached authority for dead entity {entity.Id}.");
            }

            if (!world.Has<PoseAuthority>(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter requires PoseAuthority on entity {entity.Id} before handing back attached authority.");
            }

            PoseAuthority authority = world.Get<PoseAuthority>(entity);
            if (authority.Value != PoseAuthorityKind.Attached)
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter cannot hand back entity {entity.Id}: current pose authority is {authority.Value}, expected Attached.");
            }

            if (HasPendingTransition(entity))
            {
                throw new InvalidOperationException(
                    $"PoseAuthorityArbiter already has a pending transition for entity {entity.Id}.");
            }

            _pending.Add(new PendingTransition
            {
                Entity = entity,
                From = PoseAuthorityKind.Attached,
                To = PoseAuthorityKind.Nav,
                MaxDurationMs = 0,
            });
        }

        /// <summary>
        /// 撤销实体尚未结算的写权切换待办（效果事务回滚用：事务里 stage 的授权切换
        /// 从未生效，必须从仲裁器待办中摘除，否则下一固定步边界会结算出一个
        /// 事务已回滚的写权状态）。
        /// </summary>
        internal bool RemovePendingTransition(Entity entity)
        {
            bool removed = false;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Entity == entity)
                {
                    _pending.RemoveAt(i);
                    removed = true;
                }
            }

            return removed;
        }

        /// <summary>
        /// 取消实体的写权窗口与待结算切换（合法异常终止路径）。
        /// 幂等：既无窗口也无待办时为无操作——取消方（GAS 位移、地图生命周期、结构重建）
        /// 与仲裁器自身的死亡检测谁先发现都合法。实体仍存活时写权立即回 Nav（取消不等边界：
        /// 窗口作废的语义就是"该持有者从此刻起无权写位姿"）；已取消的活跃窗口会通知监听者做幂等清理。
        /// </summary>
        public void CancelWindow(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ThrowIfCommitting();

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Entity == entity)
                {
                    _pending.RemoveAt(i);
                }
            }

            if (!TryFindWindowIndex(entity, out int windowIndex))
            {
                return;
            }

            PoseAuthorityKind holder = _activeWindows[windowIndex].Holder;
            _activeWindows.RemoveAt(windowIndex);

            if (world.IsAlive(entity) && world.Has<PoseAuthority>(entity))
            {
                world.Set(entity, new PoseAuthority { Value = PoseAuthorityKind.Nav });
            }

            for (int listenerIndex = 0; listenerIndex < _listeners.Count; listenerIndex++)
            {
                _listeners[listenerIndex].OnPoseAuthorityWindowCancelled(world, entity, holder);
            }
        }

        /// <summary>
        /// 批量取消全部窗口与待办（地图卸载、authored agent 集结构重建等生命周期事件）。
        /// </summary>
        public void CancelAllWindows(World world)
        {
            ArgumentNullException.ThrowIfNull(world);
            ThrowIfCommitting();
            _pending.Clear();
            while (_activeWindows.Count > 0)
            {
                Entity entity = _activeWindows[_activeWindows.Count - 1].Entity;
                PoseAuthorityKind holder = _activeWindows[_activeWindows.Count - 1].Holder;
                _activeWindows.RemoveAt(_activeWindows.Count - 1);

                if (world.IsAlive(entity) && world.Has<PoseAuthority>(entity))
                {
                    world.Set(entity, new PoseAuthority { Value = PoseAuthorityKind.Nav });
                }

                for (int listenerIndex = 0; listenerIndex < _listeners.Count; listenerIndex++)
                {
                    _listeners[listenerIndex].OnPoseAuthorityWindowCancelled(world, entity, holder);
                }
            }
        }

        /// <summary>
        /// 刷新活跃窗口的时钟（叠加位移=替换合同：新位移段重置窗口计时，
        /// maxDurationMs 约束单段位移而非累计）。窗口尚未结算（仅待办）时刷新是无操作——
        /// 时钟本来就未开始。既无窗口也无待办则为合同错误。
        /// </summary>
        public void RefreshWindow(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            ThrowIfCommitting();
            if (TryFindWindowIndex(entity, out int windowIndex))
            {
                WindowState window = _activeWindows[windowIndex];
                window.HeldSeconds = 0f;
                _activeWindows[windowIndex] = window;
                return;
            }

            if (HasPendingTransition(entity))
            {
                return;
            }

            throw new InvalidOperationException(
                $"PoseAuthorityArbiter cannot refresh a displacement window for entity {entity.Id}: no active window or pending transition.");
        }

        /// <summary>实体是否有活跃窗口或待结算切换（供位移系统区分"外部取消"与"结算系统缺席"）。</summary>
        public bool HasWindowOrPending(Entity entity)
        {
            return TryFindWindowIndex(entity, out _) || HasPendingTransition(entity);
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
                // 申请后、边界结算前死亡的实体：窗口从未生效，直接丢弃待办。
                // 这是合法取消（死亡是常规玩法事件），不通知监听者——没有已生效的窗口需要清理。
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    if (!world.IsAlive(_pending[i].Entity))
                    {
                        _pending.RemoveAt(i);
                    }
                }

                if (_pending.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < _pending.Count; i++)
                {
                    PendingTransition transition = _pending[i];
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

            for (int i = _activeWindows.Count - 1; i >= 0; i--)
            {
                WindowState window = _activeWindows[i];

                // 持有窗口期间死亡是常规玩法事件：仲裁器在每个固定步最先运行，
                // 是死亡后第一个能安全关闭窗口的位置——必须在这里取消并通知监听者
                // 做幂等清理，否则求解器会在同一固定步稍后的位姿同步中撞上死实体。
                if (!world.IsAlive(window.Entity))
                {
                    _activeWindows.RemoveAt(i);
                    for (int listenerIndex = 0; listenerIndex < _listeners.Count; listenerIndex++)
                    {
                        _listeners[listenerIndex].OnPoseAuthorityWindowCancelled(world, window.Entity, window.Holder);
                    }

                    continue;
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
