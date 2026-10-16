using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Config;

namespace Ludots.Editor.Bridge;

public static class CrowdSimulationEditorEndpoints
{
    public static void Map(WebApplication app, string repoRoot)
    {
        app.MapGet("/api/mods/{modId}/crowd-config", (string modId) =>
            Results.Json(CrowdSimulationEditorApi.Load(repoRoot, modId)));

        app.MapPost("/api/mods/{modId}/crowd-config/validate", async (string modId, HttpRequest request) =>
            Results.Json(await Mutate(repoRoot, modId, request, write: false)));

        app.MapPost("/api/mods/{modId}/crowd-config/apply", async (string modId, HttpRequest request) =>
            Results.Json(await Mutate(repoRoot, modId, request, write: true)));

        app.MapGet("/api/mods/{modId}/crowd-config/bundle", (string modId) =>
            Results.Json(CrowdSimulationEditorApi.ExportBundle(repoRoot, modId)));

        app.MapPost("/api/mods/{modId}/crowd-config/bundle", async (string modId, HttpRequest request) =>
        {
            JsonObject? body = await ReadBody(request);
            if (body?["bundle"] is not JsonObject bundle)
            {
                return Results.Json(CrowdSimulationEditorResult.Failed(
                    CrowdSimulationAuthoringIssues.One("bundle", "请求体需要 bundle。")).ToJson());
            }

            bool write = body["write"] is JsonValue flag && flag.TryGetValue(out bool enabled) && enabled;
            return Results.Json(CrowdSimulationEditorApi.ImportBundle(repoRoot, modId, bundle, write));
        });
    }

    private static async Task<JsonObject> Mutate(string repoRoot, string modId, HttpRequest request, bool write)
    {
        JsonObject? body = await ReadBody(request);
        if (body?["layer"] is not JsonValue layerValue || !layerValue.TryGetValue(out string? layer) || string.IsNullOrWhiteSpace(layer))
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.One("layer", "请求体需要 layer。")).ToJson();
        }

        if (body["draft"] is not JsonNode draft)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.One("draft", "请求体需要 draft。")).ToJson();
        }

        return write
            ? CrowdSimulationEditorApi.Apply(repoRoot, modId, layer, draft)
            : CrowdSimulationEditorApi.Validate(repoRoot, modId, layer, draft);
    }

    private static async Task<JsonObject?> ReadBody(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        string text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return JsonNode.Parse(text) as JsonObject;
    }
}
