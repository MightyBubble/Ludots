using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text;
using Ludots.UI.Browser;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
[NonParallelizable]
public sealed class CefBrowserNavigationTests
{
	[Test]
	public async Task NavigateAsync_LoadsPackagedLocalAppThroughRegisteredScheme()
	{
		await RunOnCefThreadAsync(NavigatePackagedLocalAppAsync);
	}

	private static async Task NavigatePackagedLocalAppAsync()
	{
		string appRoot = CreateTempDirectory("ludots-cef-app-");
		string cacheRoot = CreateTempDirectory("ludots-cef-cache-");
		try
		{
			string html = """
				<!doctype html>
				<html>
				<head>
					<meta charset="UTF-8" />
					<style>
						html, body { margin: 0; width: 100%; height: 100%; background: rgb(20, 40, 220); }
					</style>
				</head>
				<body></body>
				</html>
				""";
			File.WriteAllText(Path.Combine(appRoot, "index.html"), html, Encoding.UTF8);

			var runtime = new CefBrowserRuntime(new CefBrowserRuntimeOptions(AppContext.BaseDirectory, cacheRoot));
			IBrowserSurface? surface = null;
			try
			{
				surface = await runtime.CreateSurfaceAsync(
					new BrowserViewport(64, 64),
					new BrowserAppResourceResolver(appRoot));

				await surface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root));

				BrowserFrame frame = await WaitForPaintedFrameAsync(surface);
				Assert.That(frame.PixelFormat, Is.EqualTo(BrowserPixelFormat.Bgra8888Premultiplied));
				Assert.That(HasOpaqueBluePixel(frame), Is.True);
			}
			finally
			{
				if (surface != null)
				{
					await surface.DisposeAsync();
				}

				await runtime.DisposeAsync();
			}
		}
		finally
		{
			TryDeleteDirectory(appRoot);
			TryDeleteDirectory(cacheRoot);
		}
	}

	private static Task RunOnCefThreadAsync(Func<Task> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() =>
		{
			Exception? failure = null;
			SynchronizationContext? previousContext = SynchronizationContext.Current;
			using var context = new SingleThreadSynchronizationContext();
			SynchronizationContext.SetSynchronizationContext(context);
			context.Post(async _ =>
			{
				try
				{
					await action();
				}
				catch (Exception ex)
				{
					failure = ex;
				}
				finally
				{
					context.Complete();
				}
			}, null);

			try
			{
				context.RunOnCurrentThread();
			}
			finally
			{
				SynchronizationContext.SetSynchronizationContext(previousContext);
			}

			if (failure == null)
			{
				completion.SetResult();
			}
			else
			{
				completion.SetException(failure);
			}
		})
		{
			IsBackground = true,
			Name = "Ludots CEF navigation test"
		};
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return completion.Task;
	}

	private static async Task<BrowserFrame> WaitForPaintedFrameAsync(IBrowserSurface surface)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(5);
		BrowserFrame? lastFrame = null;
		while (DateTime.UtcNow < deadline)
		{
			lastFrame = surface.TryGetLatestFrame();
			if (lastFrame != null && HasOpaqueBluePixel(lastFrame))
			{
				return lastFrame;
			}

			await Task.Delay(50);
		}

		Assert.Fail($"CEF local app did not paint the expected frame. Last sequence: {lastFrame?.Sequence.ToString() ?? "<none>"}.");
		throw new InvalidOperationException("Unreachable after Assert.Fail.");
	}

	private static bool HasOpaqueBluePixel(BrowserFrame frame)
	{
		ReadOnlySpan<byte> pixels = frame.Pixels.Span;
		for (int y = 0; y < frame.Viewport.Height; y++)
		{
			int rowStart = y * frame.RowBytes;
			for (int x = 0; x < frame.Viewport.Width; x++)
			{
				int offset = rowStart + (x * BrowserFrameBuffer.BytesPerPixel);
				byte blue = pixels[offset];
				byte green = pixels[offset + 1];
				byte red = pixels[offset + 2];
				byte alpha = pixels[offset + 3];
				if (alpha >= 250 && blue >= 180 && green >= 20 && green <= 80 && red <= 60)
				{
					return true;
				}
			}
		}

		return false;
	}

	private static string CreateTempDirectory(string prefix)
	{
		string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
	{
		private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
		private int _threadId;

		public override void Post(SendOrPostCallback d, object? state)
		{
			ArgumentNullException.ThrowIfNull(d);
			_queue.Add((d, state));
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			ArgumentNullException.ThrowIfNull(d);
			if (Environment.CurrentManagedThreadId == _threadId)
			{
				d(state);
				return;
			}

			using var completed = new ManualResetEventSlim();
			Exception? failure = null;
			Post(callbackState =>
			{
				try
				{
					d(callbackState);
				}
				catch (Exception ex)
				{
					failure = ex;
				}
				finally
				{
					completed.Set();
				}
			}, state);
			completed.Wait();
			if (failure != null)
			{
				ExceptionDispatchInfo.Capture(failure).Throw();
			}
		}

		public void RunOnCurrentThread()
		{
			_threadId = Environment.CurrentManagedThreadId;
			foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
			{
				callback(state);
			}
		}

		public void Complete()
		{
			_queue.CompleteAdding();
		}

		public void Dispose()
		{
			_queue.Dispose();
		}
	}
}
