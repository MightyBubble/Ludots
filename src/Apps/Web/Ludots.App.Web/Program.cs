using Ludots.Adapter.Web;
using Ludots.Adapter.Web.Streaming;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5200");

var app = builder.Build();

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "game.json";
var gameHost = new WebGameHost(baseDir, configFile);
var setup = gameHost.Setup;

var cts = new CancellationTokenSource();
var gameLoopTask = Task.Run(() => gameHost.Run(cts.Token));

var clientPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "..", "src", "Client", "Web", "dist"));
if (Directory.Exists(clientPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clientPath) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clientPath) });
}

app.MapGet("/health", () =>
{
    var sessions = setup.Transport.GetSessionInfo();
    return Results.Json(new
    {
        status = "ok",
        clients = sessions.Count,
        tick = setup.Engine.GameSession?.CurrentTick ?? 0,
        sessions = sessions.Select(s => new { s.Id, s.FramesSent, s.BytesSent, s.FramesDropped }),
    });
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

app.UseWebSockets();

Console.WriteLine($"Web server starting on http://0.0.0.0:5200 ...");
Console.WriteLine($"Static files: {(Directory.Exists(clientPath) ? clientPath : "NOT FOUND — run 'npx vite build' in src/Client/Web")}");

app.Lifetime.ApplicationStopping.Register(() =>
{
    cts.Cancel();
    gameLoopTask.Wait(TimeSpan.FromSeconds(5));
});

app.Run();
