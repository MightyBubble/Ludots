using Ludots.UI.Browser;
using Ludots.UI.Browser.Cef;
using NUnit.Framework;

namespace Ludots.Tests.BrowserCef;

[TestFixture]
[NonParallelizable]
public sealed class CefBrowserRuntimeLifecycleTests
{
	[Test]
	public async Task DisposeAsync_ReleasesRuntimeOwnerWithoutPoisoningProcessCef()
	{
		await RunOnStaThreadAsync(async () =>
		{
			string cacheRoot = CreateTempDirectory("ludots-cef-lifecycle-cache-");
			try
			{
				var firstRuntime = new CefBrowserRuntime(new CefBrowserRuntimeOptions(AppContext.BaseDirectory, cacheRoot));
				Assert.That(firstRuntime.Info.EngineKind, Is.EqualTo(BrowserEngineKind.Cef));

				await firstRuntime.DisposeAsync();

				var secondRuntime = new CefBrowserRuntime(new CefBrowserRuntimeOptions(AppContext.BaseDirectory, cacheRoot));
				try
				{
					Assert.That(secondRuntime.Info.EngineKind, Is.EqualTo(BrowserEngineKind.Cef));
				}
				finally
				{
					await secondRuntime.DisposeAsync();
				}
			}
			finally
			{
				TryDeleteDirectory(cacheRoot);
			}
		});
	}

	private static Task RunOnStaThreadAsync(Func<Task> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() =>
		{
			try
			{
				action().GetAwaiter().GetResult();
				completion.SetResult();
			}
			catch (Exception ex)
			{
				completion.SetException(ex);
			}
		})
		{
			IsBackground = true,
			Name = "Ludots CEF lifecycle test"
		};

		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return completion.Task;
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
}
