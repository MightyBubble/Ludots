using System.Text.Json;
using Ludots.Launcher.Backend;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Shell 会话的 launcher HTTP API：与 Editor.Bridge 同形的路由集合，全部薄封装 LauncherService；
    /// /api/launch 为 shell 语义——PrepareLaunchAsync 后经会话中继切换进程，不 spawn 旁观进程。
    /// </summary>
    public static class LauncherShellApiMapper
    {
        public static void MapLauncherShellApi(
            this WebApplication app,
            LauncherService launcher,
            string currentAdapterId,
            Func<LauncherPrepareResult, string> resolveSessionUrl,
            Action<LauncherPrepareResult> relayToSession)
        {
            MapLauncherStatic(app, LauncherShellWebApp.ResolveLauncherDistPath(launcher.RepoRoot));

            app.MapGet("/health", () => Results.Ok(new { ok = true, mode = "shell" }));

            app.MapGet("/api/launcher/state", () =>
            {
                return Results.Ok(new
                {
                    ok = true,
                    state = launcher.GetState(),
                    mods = launcher.DiscoverMods()
                });
            });

            app.MapGet("/api/presets", () =>
            {
                var state = launcher.GetState();
                return Results.Ok(new { ok = true, presets = state.Presets, selectedPresetId = state.SelectedPresetId });
            });

            app.MapPost("/api/presets", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                if (!payload.TryGetProperty("name", out var nameElement) || string.IsNullOrWhiteSpace(nameElement.GetString()))
                {
                    return Results.BadRequest(new { ok = false, error = "Missing 'name'." });
                }

                var modIds = new List<string>();
                if (payload.TryGetProperty("activeModIds", out var modIdsElement) && modIdsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var modIdElement in modIdsElement.EnumerateArray())
                    {
                        if (modIdElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(modIdElement.GetString()))
                        {
                            modIds.Add(modIdElement.GetString()!);
                        }
                    }
                }

                var preset = launcher.SavePreset(
                    payload.TryGetProperty("presetId", out var presetIdElement) && presetIdElement.ValueKind == JsonValueKind.String
                        ? presetIdElement.GetString()
                        : null,
                    nameElement.GetString()!,
                    modIds,
                    includeDependencies: !payload.TryGetProperty("includeDependencies", out var includeDepsElement) || includeDepsElement.ValueKind != JsonValueKind.False,
                    selectAfterSave: !payload.TryGetProperty("selectAfterSave", out var selectAfterSaveElement) || selectAfterSaveElement.ValueKind != JsonValueKind.False);

                return Results.Ok(new { ok = true, preset, state = launcher.GetState() });
            });

            app.MapPost("/api/presets/select", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                var presetId = payload.TryGetProperty("presetId", out var presetIdElement) && presetIdElement.ValueKind == JsonValueKind.String
                    ? presetIdElement.GetString()
                    : null;
                var state = launcher.SelectPreset(presetId);
                return Results.Ok(new { ok = true, state });
            });

            app.MapDelete("/api/presets/{presetId}", (string presetId) =>
            {
                launcher.DeletePreset(presetId);
                return Results.Ok(new { ok = true, state = launcher.GetState() });
            });

            app.MapGet("/api/platforms", () =>
            {
                var state = launcher.GetState();
                return Results.Ok(new { ok = true, platforms = state.Platforms, selectedPlatformId = state.SelectedPlatformId });
            });

            app.MapPost("/api/platforms/select", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                if (!payload.TryGetProperty("platformId", out var platformIdElement) || string.IsNullOrWhiteSpace(platformIdElement.GetString()))
                {
                    return Results.BadRequest(new { ok = false, error = "Missing 'platformId'." });
                }
                var state = launcher.SelectPlatform(platformIdElement.GetString()!);
                return Results.Ok(new { ok = true, state });
            });

            app.MapGet("/api/mods", () =>
            {
                return Results.Ok(new { ok = true, mods = launcher.DiscoverMods() });
            });

            app.MapGet("/api/mods/{modId}/thumbnail", (string modId) =>
            {
                var mod = launcher.DiscoverMods().FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
                if (mod == null)
                {
                    return Results.NotFound();
                }

                foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
                {
                    var path = Path.Combine(mod.RootPath, "assets", "Launcher", "thumbnail" + ext);
                    if (File.Exists(path))
                    {
                        var contentType = ext switch
                        {
                            ".png" => "image/png",
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".webp" => "image/webp",
                            _ => "application/octet-stream"
                        };
                        return Results.File(File.ReadAllBytes(path), contentType);
                    }
                }
                return Results.NotFound();
            });

            app.MapGet("/api/mods/{modId}/readme", (string modId) =>
            {
                var mod = launcher.DiscoverMods().FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
                if (mod == null)
                {
                    return Results.NotFound(new { ok = false });
                }
                var readmePath = Path.Combine(mod.RootPath, "README.md");
                return File.Exists(readmePath)
                    ? Results.Ok(new { ok = true, content = File.ReadAllText(readmePath) })
                    : Results.NotFound(new { ok = false });
            });

            app.MapGet("/api/mods/{modId}/changelog", (string modId) =>
            {
                var mod = launcher.DiscoverMods().FirstOrDefault(m => string.Equals(m.Id, modId, StringComparison.OrdinalIgnoreCase));
                if (mod == null || string.IsNullOrWhiteSpace(mod.ChangelogFile))
                {
                    return Results.NotFound(new { ok = false });
                }
                var changelogPath = Path.Combine(mod.RootPath, mod.ChangelogFile);
                return File.Exists(changelogPath)
                    ? Results.Ok(new { ok = true, content = File.ReadAllText(changelogPath) })
                    : Results.NotFound(new { ok = false });
            });

            app.MapPost("/api/mods/{modId}/build", async (string modId) =>
            {
                try
                {
                    var result = await launcher.BuildModAsync(modId);
                    return Results.Ok(new { ok = result.Ok, result, mods = launcher.DiscoverMods() });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            });

            app.MapPost("/api/mods/build-all", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                try
                {
                    var selectors = ResolveSelectorsFromPayload(launcher, payload, allowDefaultPreset: false);
                    var results = await launcher.BuildAsync(selectors, ResolveAdapterFromPayload(launcher, payload), ResolveBuildModeFromPayload(payload));
                    return Results.Ok(new { ok = results.All(result => result.Ok), results, mods = launcher.DiscoverMods() });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            });

            app.MapPost("/api/mods/{modId}/fix-project", (string modId) =>
            {
                try
                {
                    var projectPath = launcher.FixModProject(modId);
                    return Results.Ok(new { ok = true, projectPath, mods = launcher.DiscoverMods() });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            });

            app.MapPost("/api/mods/create", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                if (!payload.TryGetProperty("id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString()))
                {
                    return Results.BadRequest(new { ok = false, error = "Missing 'id'" });
                }

                string modId = idElement.GetString()!;
                string template = payload.TryGetProperty("template", out var templateElement) && templateElement.ValueKind == JsonValueKind.String
                    ? templateElement.GetString() ?? "empty"
                    : "empty";
                string? targetDir = payload.TryGetProperty("dir", out var dirElement) && dirElement.ValueKind == JsonValueKind.String
                    ? dirElement.GetString()
                    : null;

                try
                {
                    var output = await launcher.CreateModAsync(modId, template, targetDir);
                    return Results.Ok(new { ok = true, modId, output, mods = launcher.DiscoverMods() });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            });

            app.MapPost("/api/mods/generate-sln", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                if (!payload.TryGetProperty("modId", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString()))
                {
                    return Results.BadRequest(new { ok = false, error = "Missing 'modId'" });
                }

                try
                {
                    var slnPath = await launcher.GenerateSolutionAsync(idElement.GetString()!);
                    return Results.Ok(new { ok = true, slnPath });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            });

            app.MapPost("/api/app/build", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                if (!payload.TryGetProperty("platformId", out var platformIdElement) || string.IsNullOrWhiteSpace(platformIdElement.GetString()))
                {
                    return Results.BadRequest(new { ok = false, error = "Missing 'platformId'." });
                }

                try
                {
                    var result = await launcher.BuildAppAsync(platformIdElement.GetString()!);
                    return Results.Ok(new { ok = result.Ok, result });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            });

            app.MapGet("/api/bindings", () =>
            {
                return Results.Ok(new { ok = true, bindings = launcher.GetState().Bindings });
            });

            app.MapGet("/api/workspace", () =>
            {
                var state = launcher.GetState();
                return Results.Ok(new { ok = true, sources = state.WorkspaceSources });
            });

            app.MapPost("/api/workspace/add-source", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                if (!payload.TryGetProperty("path", out var pathElement))
                {
                    return Results.BadRequest(new { ok = false, error = "Missing 'path' field" });
                }

                string newSource = pathElement.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(newSource) || !Directory.Exists(newSource))
                {
                    return Results.BadRequest(new { ok = false, error = $"Directory not found: {newSource}" });
                }

                var state = launcher.AddWorkspaceSource(newSource);
                return Results.Ok(new { ok = true, sources = state.WorkspaceSources, state });
            });

            app.MapPost("/api/launch", async (HttpRequest req) =>
            {
                var payload = await ReadJsonAsync(req);
                try
                {
                    var selectors = ResolveSelectorsFromPayload(launcher, payload, allowDefaultPreset: true);
                    string targetAdapter = ResolveAdapterFromPayload(launcher, payload);
                    var prepared = await launcher.PrepareLaunchAsync(
                        selectors,
                        targetAdapter,
                        ResolveBuildModeFromPayload(payload),
                        buildApp: !string.Equals(targetAdapter, currentAdapterId, StringComparison.OrdinalIgnoreCase));
                    if (!prepared.Ok || prepared.Plan is null)
                    {
                        return Results.Ok(new { ok = false, error = prepared.Error, plan = (LauncherLaunchPlan?)null });
                    }

                    string sessionUrl = resolveSessionUrl(prepared);
                    // 中继改为响应写出后即交接（child ready/health 语义由新进程的启动序列保证——
                    // 本进程退出即旧 shell 消失，新会话进程的 bootstrap 校验链就是它的 ready 信号）。
                    // 原 800ms 定时是竞态（响应未达客户端进程即退）；Kestrel 在 Results.Ok 返回后
                    // 已序列化写出响应体，此处 await 让出直到响应 flush 后再中继。
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50));
                        relayToSession(prepared);
                    });
                    return Results.Ok(new { ok = true, shell = true, url = sessionUrl, plan = prepared.Plan });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { ok = false, error = ex.Message });
                }
            });
        }

        private static void MapLauncherStatic(WebApplication app, string distPath)
        {
            if (!Directory.Exists(distPath))
            {
                app.MapGet("/launcher/index.html", () => Results.Content(
                    "<html><body style=\"background:#181a20;color:#d0d0d0;font-family:monospace;padding:32px\">" +
                    "<h2>Launcher web assets missing</h2>" +
                    "<p>Build them first: <code>npm ci --include=dev &amp;&amp; npm run build</code> in " +
                    "<code>src/Tools/Ludots.Launcher.React</code></p></body></html>",
                    "text/html"));
                return;
            }

            var provider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(distPath);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider, RequestPath = "/launcher" });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = provider, RequestPath = "/launcher" });
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpRequest req)
        {
            using var reader = new StreamReader(req.Body);
            string body = await reader.ReadToEndAsync();
            return string.IsNullOrWhiteSpace(body)
                ? JsonDocument.Parse("{}").RootElement
                : JsonSerializer.Deserialize<JsonElement>(body);
        }

        private static string ResolveAdapterFromPayload(LauncherService launcher, JsonElement payload)
        {
            if (payload.TryGetProperty("platformId", out var platformIdElement) &&
                platformIdElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(platformIdElement.GetString()))
            {
                return platformIdElement.GetString()!;
            }

            if (payload.TryGetProperty("adapterId", out var adapterIdElement) &&
                adapterIdElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(adapterIdElement.GetString()))
            {
                return adapterIdElement.GetString()!;
            }

            return launcher.GetState().SelectedPlatformId;
        }

        private static LauncherBuildMode ResolveBuildModeFromPayload(JsonElement payload)
        {
            if (payload.TryGetProperty("buildMode", out var buildModeElement) &&
                buildModeElement.ValueKind == JsonValueKind.String &&
                Enum.TryParse<LauncherBuildMode>(buildModeElement.GetString(), true, out var buildMode))
            {
                return buildMode;
            }

            return LauncherBuildMode.Auto;
        }

        private static IReadOnlyList<string> ResolveSelectorsFromPayload(LauncherService launcher, JsonElement payload, bool allowDefaultPreset)
        {
            var selectors = new List<string>();
            if (payload.TryGetProperty("presetId", out var presetIdElement) &&
                presetIdElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(presetIdElement.GetString()))
            {
                selectors.Add($"preset:{presetIdElement.GetString()!}");
            }

            if (payload.TryGetProperty("selectors", out var selectorsElement) && selectorsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var selectorElement in selectorsElement.EnumerateArray())
                {
                    if (selectorElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(selectorElement.GetString()))
                    {
                        selectors.Add(NormalizeSelector(launcher, selectorElement.GetString()!));
                    }
                }
            }

            if (payload.TryGetProperty("modIds", out var modIdsElement) && modIdsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var modIdElement in modIdsElement.EnumerateArray())
                {
                    if (modIdElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(modIdElement.GetString()))
                    {
                        selectors.Add($"mod:{modIdElement.GetString()!}");
                    }
                }
            }

            if (payload.TryGetProperty("paths", out var pathsElement) && pathsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var pathElement in pathsElement.EnumerateArray())
                {
                    if (pathElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pathElement.GetString()))
                    {
                        selectors.Add($"path:{pathElement.GetString()!}");
                    }
                }
            }

            if (selectors.Count > 0)
            {
                return selectors;
            }

            if (!allowDefaultPreset)
            {
                return Array.Empty<string>();
            }

            var selectedPresetId = launcher.GetState().SelectedPresetId;
            return string.IsNullOrWhiteSpace(selectedPresetId)
                ? Array.Empty<string>()
                : new[] { $"preset:{selectedPresetId}" };
        }

        private static string NormalizeSelector(LauncherService launcher, string raw)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith('$') || trimmed.Contains(':'))
            {
                return trimmed;
            }

            if (Directory.Exists(trimmed) && File.Exists(Path.Combine(trimmed, "mod.json")))
            {
                return $"path:{trimmed}";
            }

            if (launcher.GetState().Bindings.Any(binding => string.Equals(binding.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return $"${trimmed}";
            }

            return $"mod:{trimmed}";
        }
    }
}
