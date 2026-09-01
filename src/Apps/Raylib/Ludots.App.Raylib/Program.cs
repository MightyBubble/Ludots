using System;
using Ludots.Adapter.Raylib;
using Ludots.Core.Hosting;
using Ludots.Core.Scripting;
using Ludots.Launcher.Backend;
using Ludots.Platform.Abstractions;
using Microsoft.AspNetCore.Builder;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
try
{
    var bootstrapConfigPath = LauncherShellLifecycle.ResolveBootstrapConfigPath(args);
    if (bootstrapConfigPath is null)
    {
        await LauncherShellProgram.RunShellSessionAsync(baseDir);
        return;
    }

    var appHost = new RaylibAppHost(bootstrapConfigPath);
    appHost.Initialize(new AppInitContext(baseDir, Array.Empty<string>(), AssetsRoot: null));
    appHost.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

internal static class LauncherShellProgram
{
    public static async Task RunShellSessionAsync(string baseDir)
    {
        string repoRoot = LauncherService.FindRepoRoot(baseDir);
        var launcher = new LauncherService(repoRoot);

        var prepared = await launcher.PrepareLaunchAsync(
            new[] { LauncherShellSelectors.RaylibShellPreset },
            LauncherPlatformIds.Raylib,
            LauncherBuildMode.Auto,
            buildApp: false);
        if (!prepared.Ok || prepared.Plan is null)
        {
            Console.Error.WriteLine("Launcher shell session failed to prepare:");
            Console.Error.WriteLine(prepared.Error);
            Environment.ExitCode = 1;
            return;
        }

        var (shellApp, baseUrl) = LauncherShellWebApp.BuildLoopback(
            launcher,
            resolveSessionUrl: _ => string.Empty,
            relayToSession: session => LauncherShellLifecycle.RelayTo(session.BootstrapPath));
        await shellApp.StartAsync();

        using var host = new RaylibGameHost(
            baseDir,
            prepared.BootstrapPath,
            onComposed: engine => engine.SetService(
                new ServiceKey<LauncherShellSite>(LauncherShellSite.ServiceKeyName),
                new LauncherShellSite(baseUrl)));
        host.Run();
    }
}
