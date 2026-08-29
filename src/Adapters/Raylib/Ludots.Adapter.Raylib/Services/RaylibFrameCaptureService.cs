using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

namespace Ludots.Adapter.Raylib.Services
{
    /// <summary>
    /// Raylib implementation of <see cref="IHostFrameCapture"/>. Requests are
    /// fulfilled by <see cref="OnFramePresented"/>, which the host loop calls
    /// after EndDrawing — the same backbuffer readback the env-driven evidence
    /// screenshots use. Never blocks the game loop; callers get a Task.
    /// </summary>
    public sealed class RaylibFrameCaptureService : IHostFrameCapture
    {
        private sealed class Pending
        {
            public required TaskCompletionSource<byte[]> Completion;
            public CancellationTokenRegistration Registration;
        }

        private readonly List<Pending> _pending = new();

        public int PendingCount => _pending.Count;

        public Task<byte[]> CapturePngAsync(CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new Pending { Completion = completion };
            if (cancellationToken.CanBeCanceled)
            {
                pending.Registration = cancellationToken.Register(() =>
                {
                    if (completion.TrySetCanceled(cancellationToken))
                    {
                        _pending.Remove(pending);
                    }
                });
            }

            _pending.Add(pending);
            return completion.Task;
        }

        /// <summary>Host-loop entry point, called once per presented frame.</summary>
        public void OnFramePresented()
        {
            if (_pending.Count == 0) return;

            byte[] png;
            try
            {
                png = RaylibFramebufferCapture.EncodeFramebufferPng();
            }
            catch (Exception ex)
            {
                FailAll(new InvalidOperationException($"Frame capture failed: {ex.Message}", ex));
                return;
            }

            var batch = _pending.ToArray();
            _pending.Clear();
            foreach (Pending p in batch)
            {
                p.Registration.Dispose();
                p.Completion.TrySetResult(png);
            }
        }

        public void FailAll(Exception ex)
        {
            var batch = _pending.ToArray();
            _pending.Clear();
            foreach (Pending p in batch)
            {
                p.Registration.Dispose();
                p.Completion.TrySetException(ex);
            }
        }
    }
}
