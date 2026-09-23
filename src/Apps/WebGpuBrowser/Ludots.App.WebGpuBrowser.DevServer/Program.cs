using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
string rootArgument = builder.Configuration["root"]
    ?? throw new InvalidOperationException("Pass the published WebAssembly directory with --root.");
string rootPath = Path.GetFullPath(rootArgument);
if (!Directory.Exists(rootPath))
{
    throw new DirectoryNotFoundException($"Published WebAssembly directory does not exist: {rootPath}");
}

var fileProvider = new PhysicalFileProvider(rootPath);
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".dat"] = "application/octet-stream";
contentTypes.Mappings[".pdb"] = "application/octet-stream";
contentTypes.Mappings[".symbols"] = "application/octet-stream";
contentTypes.Mappings[".glb"] = "model/gltf-binary";
contentTypes.Mappings[".obj"] = "model/obj";
contentTypes.Mappings[".mtl"] = "text/plain";
contentTypes.Mappings[".vhtm"] = "application/octet-stream";
contentTypes.Mappings[".webgpu-font"] = "application/octet-stream";
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        context.Response.Headers["Cross-Origin-Embedder-Policy"] = "require-corp";
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        return Task.CompletedTask;
    });

    await next(context);
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = fileProvider,
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "no-store";
    },
});

app.MapGet("/__ludots/health", () => Results.Ok(new
{
    service = "Ludots WebGPU static host",
    root = rootPath,
}));

app.Run();
