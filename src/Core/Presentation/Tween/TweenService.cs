using System;
using Arch.Core;

namespace Ludots.Core.Presentation.Tween
{
    public sealed class TweenService
    {
        private readonly TweenCommandBuffer _commands;
        private readonly TweenSinkRegistry _sinks;

        public TweenService(TweenCommandBuffer commands, TweenSinkRegistry sinks)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
        }

        public int GetSinkId(string sinkKey) => _sinks.GetId(sinkKey);

        public bool TryStart(
            string sinkKey,
            int targetId,
            int propertyKey,
            float from,
            float to,
            float duration,
            TweenEase ease = TweenEase.Linear,
            int scopeId = 0,
            float delay = 0f,
            bool replaceExisting = false,
            Entity owner = default)
        {
            int sinkId = _sinks.GetId(sinkKey);
            if (sinkId <= 0) return false;

            return TryStart(sinkId, targetId, propertyKey, from, to, duration, ease, scopeId, delay, replaceExisting, owner);
        }

        public bool TryStart(
            int sinkId,
            int targetId,
            int propertyKey,
            float from,
            float to,
            float duration,
            TweenEase ease = TweenEase.Linear,
            int scopeId = 0,
            float delay = 0f,
            bool replaceExisting = false,
            Entity owner = default)
        {
            if (sinkId <= 0) return false;
            if (duration < 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            if (delay < 0f) throw new ArgumentOutOfRangeException(nameof(delay));

            return _commands.TryAdd(new TweenCommand
            {
                Kind = TweenCommandKind.Start,
                ScopeId = scopeId,
                Target = new TweenTarget
                {
                    SinkId = sinkId,
                    TargetId = targetId,
                    PropertyKey = propertyKey,
                    Owner = owner
                },
                From = from,
                To = to,
                Duration = duration,
                Delay = delay,
                Ease = ease,
                ReplaceExisting = replaceExisting
            });
        }

        public bool TryStopScope(int scopeId)
        {
            if (scopeId == 0) return false;
            return _commands.TryAdd(new TweenCommand
            {
                Kind = TweenCommandKind.StopScope,
                ScopeId = scopeId
            });
        }

        public bool TryCompleteScope(int scopeId)
        {
            if (scopeId == 0) return false;
            return _commands.TryAdd(new TweenCommand
            {
                Kind = TweenCommandKind.CompleteScope,
                ScopeId = scopeId
            });
        }
    }
}
