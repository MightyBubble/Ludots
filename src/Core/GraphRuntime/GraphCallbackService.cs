using System;
using Arch.Core;
using Ludots.Core.Map;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Resume target for a parked graph run waiting on <see cref="GraphCallbackService"/>.
    /// Mounts and harnesses implement this; Dialogue/host code Completes handles, never owns a second waiter.
    /// </summary>
    public interface IGraphCallbackResumeTarget
    {
        bool IsCallbackResumeAlive { get; }

        void ResumeAfterGraphCallback(int handleId, bool confirmed, int resultBoolRegister);
    }

    /// <summary>
    /// #1126 Graph Continuation: registration-ordered await handles and completion queue.
    /// Completions enqueue; <see cref="Drain"/> resumes in registration order (not completion order).
    /// </summary>
    public sealed class GraphCallbackService
    {
        public const int MaxHandles = 256;
        public const int MaxPendingCompletions = 64;
        public const int MaxResumeTargetDepth = 16;

        private readonly Waiter[] _waiters = new Waiter[MaxHandles + 1];
        private readonly int[] _pendingCompleteHandles = new int[MaxPendingCompletions];
        private readonly bool[] _pendingConfirmed = new bool[MaxPendingCompletions];
        private readonly IGraphCallbackResumeTarget?[] _resumeTargetStack = new IGraphCallbackResumeTarget?[MaxResumeTargetDepth];
        private int _nextHandle = 1;
        private int _pendingCount;
        private int _registrationSeq;
        private int _resumeTargetDepth;

        private struct Waiter
        {
            public bool Occupied;
            public bool Completed;
            public bool Confirmed;
            public int RegistrationOrder;
            public int ResultBoolRegister;
            public string CallbackType;
            public MapId MapId;
            public Entity Scope;
            public IGraphCallbackResumeTarget? Target;
        }

        public void PushResumeTarget(IGraphCallbackResumeTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (_resumeTargetDepth >= MaxResumeTargetDepth)
            {
                throw new InvalidOperationException(
                    $"GRAPH.CALLBACK.ERR.ResumeTargetStackFull: nested AwaitCallback resume targets exceeded {MaxResumeTargetDepth}.");
            }

            _resumeTargetStack[_resumeTargetDepth++] = target;
        }

        public void PopResumeTarget(IGraphCallbackResumeTarget target)
        {
            if (_resumeTargetDepth <= 0 ||
                !ReferenceEquals(_resumeTargetStack[_resumeTargetDepth - 1], target))
            {
                throw new InvalidOperationException(
                    "GRAPH.CALLBACK.ERR.ResumeTargetMismatch: PopResumeTarget does not match the bound target.");
            }

            _resumeTargetStack[--_resumeTargetDepth] = null;
        }

        private IGraphCallbackResumeTarget? CurrentResumeTarget
            => _resumeTargetDepth > 0 ? _resumeTargetStack[_resumeTargetDepth - 1] : null;

        /// <summary>
        /// Registers a waiter and returns an opaque handle id. Fail closed when the catalog
        /// name is empty, capacity is exhausted, or no resume target is bound.
        /// </summary>
        public int BeginAwait(
            string callbackType,
            MapId mapId,
            Entity scope,
            int resultBoolRegister)
        {
            if (string.IsNullOrWhiteSpace(callbackType))
            {
                throw new InvalidOperationException(
                    "GRAPH.CALLBACK.ERR.CallbackTypeRequired: AwaitCallback requires a non-empty callbackType.");
            }

            IGraphCallbackResumeTarget? currentTarget = CurrentResumeTarget;
            if (currentTarget == null)
            {
                throw new InvalidOperationException(
                    "GRAPH.CALLBACK.ERR.NoResumeTarget: AwaitCallback requires a bound resume target for this slice.");
            }

            if (resultBoolRegister < 0)
            {
                throw new InvalidOperationException(
                    "GRAPH.CALLBACK.ERR.ResultRegisterRequired: AwaitCallback requires a bool result register (Dst).");
            }

            int handle = AllocateHandle();
            _waiters[handle] = new Waiter
            {
                Occupied = true,
                Completed = false,
                Confirmed = false,
                RegistrationOrder = ++_registrationSeq,
                ResultBoolRegister = resultBoolRegister,
                CallbackType = callbackType.Trim(),
                MapId = mapId,
                Scope = scope,
                Target = currentTarget,
            };
            return handle;
        }

        public bool TryGetWaiter(
            int handleId,
            out string callbackType,
            out MapId mapId,
            out Entity scope,
            out int resultBoolRegister)
        {
            callbackType = string.Empty;
            mapId = default;
            scope = default;
            resultBoolRegister = -1;
            if (!IsLiveHandle(handleId))
            {
                return false;
            }

            Waiter w = _waiters[handleId];
            callbackType = w.CallbackType;
            mapId = w.MapId;
            scope = w.Scope;
            resultBoolRegister = w.ResultBoolRegister;
            return true;
        }

        /// <summary>
        /// Enqueues a completion. Fail closed on unknown, already-completed, or capacity overflow.
        /// Order of Complete calls does not decide resume order — registration order does.
        /// </summary>
        public void Complete(int handleId, bool confirmed)
        {
            if (!IsLiveHandle(handleId))
            {
                throw new InvalidOperationException(
                    $"GRAPH.CALLBACK.ERR.InvalidHandle: handle {handleId} is not a live AwaitCallback waiter.");
            }

            ref Waiter w = ref _waiters[handleId];
            if (w.Completed)
            {
                throw new InvalidOperationException(
                    $"GRAPH.CALLBACK.ERR.DoubleComplete: handle {handleId} was already completed.");
            }

            if (_pendingCount >= MaxPendingCompletions)
            {
                throw new InvalidOperationException(
                    $"GRAPH.CALLBACK.ERR.CompletionQueueFull: pending completions exceeded {MaxPendingCompletions}.");
            }

            w.Completed = true;
            w.Confirmed = confirmed;
            _pendingCompleteHandles[_pendingCount] = handleId;
            _pendingConfirmed[_pendingCount] = confirmed;
            _pendingCount++;
        }

        /// <summary>
        /// Drains completed waiters in registration order and resumes each live target once.
        /// Dead targets fail closed (handle released, no silent resume).
        /// </summary>
        public void Drain()
        {
            if (_pendingCount == 0)
            {
                return;
            }

            // Compact pending into a scratch ordered by RegistrationOrder ascending.
            Span<int> order = stackalloc int[_pendingCount];
            for (int i = 0; i < _pendingCount; i++)
            {
                order[i] = i;
            }

            for (int i = 1; i < _pendingCount; i++)
            {
                int key = order[i];
                int keyHandle = _pendingCompleteHandles[key];
                int keyReg = _waiters[keyHandle].RegistrationOrder;
                int j = i - 1;
                while (j >= 0)
                {
                    int otherHandle = _pendingCompleteHandles[order[j]];
                    if (_waiters[otherHandle].RegistrationOrder <= keyReg)
                    {
                        break;
                    }

                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = key;
            }

            int pending = _pendingCount;
            _pendingCount = 0;

            for (int i = 0; i < pending; i++)
            {
                int slot = order[i];
                int handleId = _pendingCompleteHandles[slot];
                bool confirmed = _pendingConfirmed[slot];
                if (!IsLiveHandle(handleId))
                {
                    continue;
                }

                Waiter w = _waiters[handleId];
                IGraphCallbackResumeTarget? target = w.Target;
                int resultBoolRegister = w.ResultBoolRegister;
                ReleaseHandle(handleId);

                if (target == null || !target.IsCallbackResumeAlive)
                {
                    throw new InvalidOperationException(
                        $"GRAPH.CALLBACK.ERR.ResumeTargetDead: handle {handleId} completed but its resume target is gone; continuation fails closed.");
                }

                target.ResumeAfterGraphCallback(handleId, confirmed, resultBoolRegister);
            }
        }

        public bool HasLiveWaiterForTarget(IGraphCallbackResumeTarget target)
        {
            return TryGetLiveHandleForTarget(target, out _);
        }

        /// <summary>
        /// Returns the live handle bound to <paramref name="target"/>, if any.
        /// Gallery hosts and Dialogue complete through this lookup after a Yielded slice.
        /// </summary>
        public bool TryGetLiveHandleForTarget(IGraphCallbackResumeTarget target, out int handleId)
        {
            ArgumentNullException.ThrowIfNull(target);
            for (int i = 1; i <= MaxHandles; i++)
            {
                if (_waiters[i].Occupied && ReferenceEquals(_waiters[i].Target, target))
                {
                    handleId = i;
                    return true;
                }
            }

            handleId = 0;
            return false;
        }

        /// <summary>
        /// Finds the oldest live waiter for <paramref name="callbackType"/> (registration order).
        /// Dialogue Completers use this when they do not own the resume target.
        /// </summary>
        public bool TryGetOldestLiveHandleByCallbackType(string callbackType, out int handleId)
        {
            handleId = 0;
            if (string.IsNullOrWhiteSpace(callbackType))
            {
                return false;
            }

            string needle = callbackType.Trim();
            int bestOrder = int.MaxValue;
            int bestHandle = 0;
            for (int i = 1; i <= MaxHandles; i++)
            {
                if (!_waiters[i].Occupied || _waiters[i].Completed)
                {
                    continue;
                }

                if (!string.Equals(_waiters[i].CallbackType, needle, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_waiters[i].RegistrationOrder < bestOrder)
                {
                    bestOrder = _waiters[i].RegistrationOrder;
                    bestHandle = i;
                }
            }

            if (bestHandle == 0)
            {
                return false;
            }

            handleId = bestHandle;
            return true;
        }

        /// <summary>
        /// Completes the oldest live waiter for <paramref name="callbackType"/>, if any.
        /// Returns false when no waiter is pending (Dialogue paths are not required to park a mount).
        /// </summary>
        public bool TryCompleteByCallbackType(string callbackType, bool confirmed)
        {
            if (!TryGetOldestLiveHandleByCallbackType(callbackType, out int handleId))
            {
                return false;
            }

            Complete(handleId, confirmed);
            return true;
        }

        public void InvalidateMap(MapId mapId)
        {
            for (int i = 1; i <= MaxHandles; i++)
            {
                if (_waiters[i].Occupied && _waiters[i].MapId.Equals(mapId))
                {
                    ReleaseHandle(i);
                }
            }
        }

        public void InvalidateScope(Entity scope)
        {
            if (scope == Entity.Null || scope == default)
            {
                return;
            }

            for (int i = 1; i <= MaxHandles; i++)
            {
                if (_waiters[i].Occupied && _waiters[i].Scope.Equals(scope))
                {
                    ReleaseHandle(i);
                }
            }
        }

        private int AllocateHandle()
        {
            for (int attempt = 0; attempt < MaxHandles; attempt++)
            {
                int id = _nextHandle;
                _nextHandle++;
                if (_nextHandle > MaxHandles)
                {
                    _nextHandle = 1;
                }

                if (!_waiters[id].Occupied)
                {
                    return id;
                }
            }

            throw new InvalidOperationException(
                $"GRAPH.CALLBACK.ERR.HandleTableFull: live AwaitCallback waiters exceeded {MaxHandles}.");
        }

        private bool IsLiveHandle(int handleId)
        {
            return (uint)handleId <= MaxHandles && _waiters[handleId].Occupied;
        }

        private void ReleaseHandle(int handleId)
        {
            _waiters[handleId] = default;
        }
    }
}
