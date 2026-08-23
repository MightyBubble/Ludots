using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Platform.Abstractions;
using Raylib_cs;

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
        private int _captureCounter;

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
            // Raylib TakeScreenshot flattens any directory to the working dir
            // (same behavior the evidence-sccreenshot path works around).
            string fileName = $"ludots-frame-capture-{Environment.ProcessId}-{++_captureCounter}.png";
            string workingPath = Path.Combine(Environment.CurrentDirectory, fileName);
            try
            {
                Raylib_cs.Raylib.TakeScreenshot(fileName);
                png = File.ReadAllBytes(workingPath);
            }
            catch (Exception ex)
            {
                FailAll(new InvalidOperationException($"Frame capture failed: {ex.Message}", ex));
                return;
            }
            finally
            {
                try { if (File.Exists(workingPath)) File.Delete(workingPath); } catch { /* best effort */ }
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
