using Ludots.Launcher.Backend;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Shell 会话的环回 Web 宿主：伺服 React launcher dist（/launcher）+ launcher API。
    /// raylib Shell 经它同源喂 CEF；web Shell 直接把它跑在产品端口上。
    /// </summary>
    public static class LauncherShellWebApp
    {
        public const int LoopbackBasePort = 47951;
        public const int PortProbeCount = 16;

        public static string ResolveLauncherDistPath(string repoRoot)
        {
            return Path.Combine(repoRoot, "src", "Tools", "Ludots.Launcher.React", "dist");
        }

        public static (WebApplication App, string BaseUrl) BuildLoopback(
            LauncherService launcher,
            Func<LauncherPrepareResult, string> resolveSessionUrl,
            Action<LauncherPrepareResult> relayToSession)
        {
            int? port = null;
            for (int probe = 0; probe < PortProbeCount; probe++)
            {
                int candidate = LoopbackBasePort + probe;
                if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .All(listener => listener.Port != candidate))
                {
                    port = candidate;
                    break;
                }
            }

            if (port is null)
            {
                throw new InvalidOperationException(
                    $"No free loopback port in range {LoopbackBasePort}-{LoopbackBasePort + PortProbeCount - 1} for the launcher shell site.");
            }

            var app = Build(launcher, $"http://127.0.0.1:{port}", LauncherPlatformIds.Raylib, resolveSessionUrl, relayToSession);
            return (app, $"http://127.0.0.1:{port}");

        }

        public static WebApplication Build(
            LauncherService launcher,
            string url,
            string currentAdapterId,
            Func<LauncherPrepareResult, string> resolveSessionUrl,
            Action<LauncherPrepareResult> relayToSession)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(url);
            var app = builder.Build();

            app.MapLauncherShellApi(launcher, currentAdapterId, resolveSessionUrl, relayToSession);

            return app;
        }
    }
}
