using Ludots.Adapter.Web;
using Ludots.Adapter.Web.Streaming;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5200");

var app = builder.Build();

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "launcher.runtime.json";
var gameHost = new WebGameHost(baseDir, configFile);
var setup = gameHost.Setup;

var cts = new CancellationTokenSource();
var gameLoopTask = Task.Run(() => gameHost.Run(cts.Token));
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
