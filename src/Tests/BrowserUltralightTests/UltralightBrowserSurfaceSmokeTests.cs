using Ludots.UI.Browser;
using Ludots.UI.Browser.Ultralight;
using NUnit.Framework;

namespace Ludots.Tests.BrowserUltralight;

[TestFixture]
public sealed class UltralightBrowserSurfaceSmokeTests
{
	[Test]
	public async Task CreateSurface_RendersLocalAppHtml_AndCapturesBgraFrame()
	{
		string runtimeRoot = PublishProviderPackage();
		string cacheRoot = Path.Combine(Path.GetTempPath(), "ludots-ultralight-cache", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(cacheRoot);

		var services = new Dictionary<string, object>();
		IBrowserRuntime runtime = UltralightBrowserRuntimeHost.Install(services, runtimeRoot, cacheRoot);
		Assert.That(runtime.Info.EngineKind, Is.EqualTo(BrowserEngineKind.Ultralight));

		string appRoot = Path.Combine(cacheRoot, "app");
		Directory.CreateDirectory(appRoot);
		await File.WriteAllTextAsync(
			Path.Combine(appRoot, "index.html"),
			"<html><body style='margin:0;background:#2244aa;color:#fff;font:48px sans-serif;display:flex;align-items:center;justify-content:center;height:100vh'>UL</body></html>");

		var resolver = new BrowserAppResourceResolver(appRoot);
		IBrowserSurface appSurface = await runtime.CreateSurfaceAsync(
			new BrowserViewport(320, 180, 1f),
			resolver);
		try
		{
			await appSurface.NavigateAsync(new BrowserNavigationRequest(BrowserLocalAppUri.Root));

			BrowserFrame? frame = null;
			for (int i = 0; i < 90; i++)
			{
				await Task.Delay(32);
				frame = appSurface.TryGetLatestFrame();
				if (frame != null && frame.Pixels.Length > 0)
				{
					int centerProbe = (90 * frame.RowBytes) + (160 * 4);
					if (frame.Pixels.Span[centerProbe] > 100)
					{
						break;
					}
				}
			}

			Assert.That(frame, Is.Not.Null);
			Assert.That(frame!.Viewport.Width, Is.EqualTo(320));
			Assert.That(frame.Viewport.Height, Is.EqualTo(180));
			Assert.That(frame.PixelFormat, Is.EqualTo(BrowserPixelFormat.Bgra8888Premultiplied));
			Assert.That(frame.Pixels.Length, Is.GreaterThan(0));

			int centerOffset = (90 * frame.RowBytes) + (160 * 4);
			byte b = frame.Pixels.Span[centerOffset];
			byte g = frame.Pixels.Span[centerOffset + 1];
			byte r = frame.Pixels.Span[centerOffset + 2];
			Assert.That(b, Is.GreaterThan(r), $"Expected blue-dominant center pixel, got BGR=({b},{g},{r})");
			Assert.That(b, Is.GreaterThan(g), $"Expected blue-dominant center pixel, got BGR=({b},{g},{r})");

			string artifactDir = "/opt/cursor/artifacts";
			Directory.CreateDirectory(artifactDir);
			await File.WriteAllBytesAsync(Path.Combine(artifactDir, "ultralight_provider_frame.bgra"), frame.Pixels.ToArray());
			await File.WriteAllTextAsync(
				Path.Combine(artifactDir, "ultralight_provider_frame.meta.txt"),
				$"width={frame.Viewport.Width}\nheight={frame.Viewport.Height}\nrowBytes={frame.RowBytes}\ncenterBgr={b},{g},{r}\n");
		}
		finally
		{
			await appSurface.DisposeAsync();
			await runtime.DisposeAsync();
			if (services.TryGetValue(BrowserRuntimeServiceNames.HostLifecycle, out object? lifecycle) &&
			    lifecycle is IBrowserRuntimeHostLifecycle hostLifecycle)
			{
				hostLifecycle.ShutdownProcessForHostExit();
			}
		}
	}

	private static string PublishProviderPackage()
	{
		string repoRoot = FindRepoRoot();
		string projectPath = Path.Combine(
			repoRoot,
			"src",
			"Libraries",
			"Ludots.UI.Browser.Ultralight",
			"Ludots.UI.Browser.Ultralight.csproj");
		string output = Path.Combine(Path.GetTempPath(), "ludots-ultralight-pkg", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(output);

		var start = new System.Diagnostics.ProcessStartInfo
		{
			FileName = "dotnet",
			Arguments = $"publish \"{projectPath}\" -c Release -o \"{output}\" --self-contained false -nologo -v:q",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};
		using var process = System.Diagnostics.Process.Start(start)
			?? throw new InvalidOperationException("Failed to start dotnet publish for Ultralight provider.");
		string stdout = process.StandardOutput.ReadToEnd();
		string stderr = process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException($"Ultralight publish failed.\n{stdout}\n{stderr}");
		}

		return output;
	}

	private static string FindRepoRoot()
	{
		string? dir = AppContext.BaseDirectory;
		while (!string.IsNullOrEmpty(dir))
		{
			if (File.Exists(Path.Combine(dir, "launcher.config.json")))
			{
				return dir;
			}

			dir = Directory.GetParent(dir)?.FullName;
		}

		throw new InvalidOperationException("Repository root was not found.");
	}
}
