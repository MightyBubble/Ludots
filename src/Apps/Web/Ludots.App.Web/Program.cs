using Ludots.Adapter.Web;
using Ludots.Adapter.Web.Streaming;
using Ludots.Launcher.Backend;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5200");

var app = builder.Build();

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
var bootstrapConfigPath = LauncherShellLifecycle.ResolveBootstrapConfigPath(args);
if (bootstrapConfigPath is null)
{
    await RunShellModeAsync(app);
    return;
}

var gameHost = new WebGameHost(baseDir, bootstrapConfigPath);

var cts = new CancellationTokenSource();
var setupReady = new TaskCompletionSource<WebHostSetup>(TaskCreationOptions.RunContinuationsAsynchronously);
var gameLoopTask = Task.Factory.StartNew(
    () =>
    {
        try
        {
            WebHostSetup loopSetup = gameHost.Setup;
            setupReady.TrySetResult(loopSetup);
            gameHost.Run(cts.Token);
        }
        catch (Exception ex)
        {
            setupReady.TrySetException(ex);
            throw;
        }
    },
    CancellationToken.None,
    TaskCreationOptions.LongRunning,
    TaskScheduler.Default);
var setup = setupReady.Task.GetAwaiter().GetResult();
_ = gameLoopTask.ContinueWith(
    t =>
    {
        Exception? taskFault = t.Exception?.GetBaseException() ?? t.Exception;
        Exception fault = taskFault ?? new InvalidOperationException("Web game loop faulted without exception details.");
        setup.LoopStatus.MarkFaulted(fault);
        Console.Error.WriteLine($"[GameLoop FAULTED] {t.Exception}");
    },
    CancellationToken.None,
    TaskContinuationOptions.OnlyOnFaulted,
    TaskScheduler.Default);

app.UseWebSockets();

var clientPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "..", "src", "Client", "Web", "dist"));
if (Directory.Exists(clientPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clientPath) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clientPath) });
}

app.MapGet("/health", () =>
{
    var sessions = setup.Transport.GetSessionInfo();
    WebHostLoopHealthSnapshot loop = setup.LoopStatus.CaptureHealthSnapshot();
    var payload = new
    {
        status = loop.Status,
        loop = new
        {
            loop.Healthy,
            loop.Running,
            loop.Faulted,
            loop.FaultType,
            loop.FaultMessage,
        },
        clients = sessions.Count,
        tick = setup.Engine.GameSession?.CurrentTick ?? 0,
        sessions = sessions.Select(s => new { s.Id, s.FramesSent, s.BytesSent, s.FramesDropped }),
    };

    return Results.Json(
        payload,
        statusCode: loop.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }
    var ws = await context.WebSockets.AcceptWebSocketAsync();
    await setup.Transport.HandleClientAsync(ws, cts.Token);
});

Console.WriteLine($"Web server starting on http://0.0.0.0:5200 ...");
Console.WriteLine($"Static files: {(Directory.Exists(clientPath) ? clientPath : "NOT FOUND — run 'npx vite build' in src/Client/Web")}");

app.Lifetime.ApplicationStopping.Register(() =>
{
    cts.Cancel();
    gameLoopTask.Wait(TimeSpan.FromSeconds(5));
});

app.Run();
return;

static async Task RunShellModeAsync(WebApplication app)
{
    string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
    string repoRoot = LauncherService.FindRepoRoot(baseDirectory);
    var launcher = new LauncherService(repoRoot);

    app.MapLauncherShellApi(
        launcher,
        LauncherPlatformIds.Web,
        resolveSessionUrl: _ => "/",
        relayToSession: session => LauncherShellLifecycle.RelayTo(session.BootstrapPath));
    app.MapGet("/", () => Results.Redirect("/launcher/index.html"));

    Console.WriteLine("Web launcher shell starting on http://0.0.0.0:5200/launcher/index.html ...");
    await app.RunAsync();
}
