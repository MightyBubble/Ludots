using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Tween;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class TweenRuntimeSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _frameQuery = new QueryDescription()
            .WithAll<PresentationFrameState>();

        private readonly TweenCommandBuffer _commands;
        private readonly TweenInstanceBuffer _instances;
        private readonly TweenSinkRegistry _sinks;
        private float _currentRenderDelta;

        public TweenRuntimeSystem(World world, TweenCommandBuffer commands, TweenInstanceBuffer instances, TweenSinkRegistry sinks)
            : base(world)
        {
            _commands = commands;
            _instances = instances;
            _sinks = sinks;
        }

        public override void Update(in float dt)
        {
            _currentRenderDelta = ReadRenderDelta(dt);
            ProcessCommands();
            _instances.ProcessActive(_currentRenderDelta, TickTween);
            _commands.Clear();
        }

        private void ProcessCommands()
        {
            var span = _commands.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var cmd = ref span[i];
                switch (cmd.Kind)
                {
                    case TweenCommandKind.Start:
                        StartTween(in cmd);
                        break;
                    case TweenCommandKind.StopScope:
                        _instances.ReleaseScope(cmd.ScopeId);
                        break;
                    case TweenCommandKind.CompleteScope:
                        CompleteScope(cmd.ScopeId);
                        break;
                }
            }
        }

        private void StartTween(in TweenCommand command)
        {
            if (command.ReplaceExisting && command.ScopeId != 0)
                _instances.ReleaseScope(command.ScopeId);

            if (!_instances.TryAllocate(in command, out _))
                return;

            _sinks.TryApply(in command.Target, command.From);
        }

        private void CompleteScope(int scopeId)
        {
            _instances.ProcessScope(scopeId, CompleteInstance);
        }

        private void CompleteInstance(int handle, ref TweenInstance instance)
        {
            _sinks.TryApply(in instance.Target, instance.To);
            _instances.Release(handle);
        }

        private void TickTween(int handle, ref TweenInstance instance)
        {
            float dt = _currentRenderDelta;
            if (dt <= 0f) return;

            if (instance.DelayRemaining > 0f)
            {
                instance.DelayRemaining -= dt;
                _sinks.TryApply(in instance.Target, instance.From);
                if (instance.DelayRemaining > 0f)
                    return;

                dt = -instance.DelayRemaining;
                instance.DelayRemaining = 0f;
            }

            float duration = instance.Duration <= 0f ? 0.0001f : instance.Duration;
            instance.Elapsed += dt;
            float t = System.Math.Clamp(instance.Elapsed / duration, 0f, 1f);
            float eased = TweenEasing.Evaluate(instance.Ease, t);
            float value = instance.From + ((instance.To - instance.From) * eased);
            _sinks.TryApply(in instance.Target, value);

            if (t >= 1f)
                _instances.Release(handle);
        }

        private float ReadRenderDelta(float fallback)
        {
            var job = new ReadRenderDeltaJob { RenderDelta = fallback };
            World.InlineQuery<ReadRenderDeltaJob, PresentationFrameState>(in _frameQuery, ref job);
            return job.RenderDelta;
        }

        private struct ReadRenderDeltaJob : IForEach<PresentationFrameState>
        {
            public float RenderDelta;

            public void Update(ref PresentationFrameState state)
            {
                if (state.RenderDeltaTime > 0f)
                    RenderDelta = state.RenderDeltaTime;
            }
        }
    }
}
