using Arch.System;
using Ludots.WebUI.DataPlane;

namespace SanguoGrandStrategyMod.Web;

internal sealed class SanguoDataPlaneSystem : ISystem<float>
{
    private const float PublishIntervalSeconds = 0.12f;
    private readonly WebUiDataPlaneTickPump _pump;
    private Task? _pumpTask;
    private Exception? _backgroundFault;
    private float _seconds;
    private bool _disposed;

    public SanguoDataPlaneSystem(WebUiDataPlaneTickPump pump)
    {
        _pump = pump;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (_disposed)
        {
            return;
        }

        if (_backgroundFault != null)
        {
            Exception fault = _backgroundFault;
            _backgroundFault = null;
            throw new InvalidOperationException("Sanguo WebUI DataPlane tick failed.", fault);
        }

        if (_pumpTask != null)
        {
            if (!_pumpTask.IsCompleted)
            {
                return;
            }

            ObserveCompletedPumpTask(_pumpTask);
            _pumpTask = null;
            if (_backgroundFault != null)
            {
                return;
            }
        }

        _seconds += MathF.Max(0f, dt);
        bool shouldPublish = _seconds >= PublishIntervalSeconds;
        if (shouldPublish)
        {
            _seconds = 0f;
        }

        _pumpTask = RunPumpAsync(shouldPublish);
        if (_pumpTask.IsCompleted)
        {
            ObserveCompletedPumpTask(_pumpTask);
            _pumpTask = null;
        }
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private async Task RunPumpAsync(bool publishTopics)
    {
        await _pump.FlushCommandsAsync().ConfigureAwait(false);
        if (publishTopics)
        {
            await _pump.PublishTopicsAsync().ConfigureAwait(false);
        }
    }

    private void ObserveCompletedPumpTask(Task task)
    {
        if (task.IsCanceled)
        {
            _backgroundFault = new OperationCanceledException("Sanguo WebUI DataPlane pump was canceled.");
            return;
        }

        if (task.IsFaulted)
        {
            _backgroundFault = task.Exception?.GetBaseException() ??
                new InvalidOperationException("Sanguo WebUI DataPlane pump failed.");
        }
    }
}
