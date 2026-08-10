using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Providers;

namespace Ludots.Core.Gameplay.Tasks
{
    public sealed class TaskStateChangedSourceProvider : ISourceProvider
    {
        public readonly List<ProviderSignal> Emitted = new();

        public void Emit(in ProviderSignal signal, ProviderExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Emitted.Add(signal);
        }
    }

    public sealed class TaskCreateEffectHandler : IEffectHandler
    {
        private readonly TaskRuntimeService _runtime;

        public TaskCreateEffectHandler(TaskRuntimeService runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!call.Parameters.TryGetValue("task_id", out object? taskIdObj) ||
                taskIdObj is not string taskId ||
                string.IsNullOrWhiteSpace(taskId))
            {
                throw new InvalidOperationException(
                    "task.create requires parameter task_id.");
            }

            _runtime.OfferOrStart(taskId, context.Subject);
        }
    }

    public static class TaskBridgeProviderInstaller
    {
        public static void Install(ProviderServices providers, TaskRuntimeService runtime)
        {
            ArgumentNullException.ThrowIfNull(providers);
            ArgumentNullException.ThrowIfNull(runtime);

            providers.Gaps.TryResolve("task.state_changed", out _);
            providers.Gaps.TryResolve("task.create", out _);

            if (!providers.Sources.Contains("task.state_changed"))
            {
                providers.Sources.Register(
                    "task.state_changed",
                    new TaskStateChangedSourceProvider(),
                    new ProviderParameterSchema(new[]
                    {
                        new ProviderParameterField("task_id", ProviderParameterKind.String, required: true),
                        new ProviderParameterField("state", ProviderParameterKind.String, required: true),
                    }));
            }

            if (!providers.Effects.Contains("task.create"))
            {
                providers.Effects.Register(
                    "task.create",
                    new TaskCreateEffectHandler(runtime),
                    new ProviderParameterSchema(new[]
                    {
                        new ProviderParameterField("task_id", ProviderParameterKind.String, required: true),
                    }));
            }
        }
    }
}
