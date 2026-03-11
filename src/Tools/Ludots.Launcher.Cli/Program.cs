using System.Text.Json;
using Ludots.Launcher.Backend;
using Ludots.Launcher.Evidence;

var repoRoot = LauncherService.FindRepoRoot(AppDomain.CurrentDomain.BaseDirectory);
var service = new LauncherService(repoRoot);
var command = CliCommand.Parse(args);

try
{
    switch (command.Primary)
    {
        case "":
        case "help":
        case "--help":
        case "-h":
            PrintHelp();
            return 0;
        case "catalog":
        case "mods" when command.Secondary == "list":
            return PrintCatalog(service.DiscoverMods());
        case "resolve":
        {
            var selectors = ResolveRequestedSelectors(service, command, allowDefaultPreset: true);
            var result = service.Resolve(selectors, ResolveRequestedAdapter(service, command), command.BuildMode);
            PrintResolveResult(result, command.Json);
            return 0;
        }
        case "launch":
        {
            var selectors = ResolveRequestedSelectors(service, command, allowDefaultPreset: true);
            if (!string.IsNullOrWhiteSpace(command.RecordDirectory))
            {
                return await RunRecordedLaunchAsync(service, repoRoot, selectors, ResolveRequestedAdapter(service, command), command, args);
            }

            var result = await service.LaunchAsync(selectors, ResolveRequestedAdapter(service, command), command.BuildMode);
            if (!result.Ok)
            {
                Console.Error.WriteLine(result.Error);
                return 1;
            }

            Console.WriteLine($"adapter={result.Plan?.AdapterId ?? ResolveRequestedAdapter(service, command)}");
            Console.WriteLine($"pid={result.Pid}");
            Console.WriteLine($"bootstrap={result.BootstrapPath}");
            if (!string.IsNullOrWhiteSpace(result.Url))
            {
                Console.WriteLine(result.Url);
            }

            return 0;
        }
        case "build" when command.Secondary == "app":
        {
            var result = await service.BuildAppAsync(ResolveRequestedAdapter(service, command));
            Console.WriteLine(result.Output);
            return result.Ok ? 0 : result.ExitCode;
        }
        case "build":
        {
            var selectors = ResolveRequestedSelectors(service, command, allowDefaultPreset: true);
            var results = await service.BuildAsync(selectors, ResolveRequestedAdapter(service, command), command.BuildMode);
            return PrintBuildResults(results);
        }
        case "adapter" when command.Secondary == "list":
        {
            var state = service.GetState();
            foreach (var platform in state.Platforms)
            {
                var selected = string.Equals(platform.Id, state.SelectedPlatformId, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                Console.WriteLine($"{selected} {platform.Id,-8} {platform.Name}");
            }

            return 0;
        }
        case "adapter" when command.Secondary == "select":
        {
            var state = service.SelectPlatform(ResolveRequiredAdapter(command));
            Console.WriteLine(state.SelectedPlatformId);
            return 0;
        }
        case "workspace" when command.Secondary == "list":
        {
            foreach (var source in service.GetWorkspaceSources())
            {
                Console.WriteLine(source);
            }

            return 0;
        }
        case "workspace" when command.Secondary == "add":
        {
            var path = command.PathValues.FirstOrDefault() ?? command.Operands.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("workspace add requires a mod root path.");
            }

            var state = service.AddWorkspaceSource(path);
            foreach (var source in state.WorkspaceSources)
            {
                Console.WriteLine(source);
            }

            return 0;
        }
        case "binding" when command.Secondary == "list":
        {
            var state = service.GetState();
            foreach (var binding in state.Bindings)
            {
                var projectSuffix = string.IsNullOrWhiteSpace(binding.ProjectPath) ? string.Empty : $" (project={binding.ProjectPath})";
                Console.WriteLine($"${binding.Name} -> {binding.TargetType}:{binding.TargetValue}{projectSuffix}");
            }

            return 0;
        }
        case "binding" when command.Secondary == "set":
        {
            var bindingName = NormalizeBindingName(command.Name ?? command.Operands.FirstOrDefault());
            if (string.IsNullOrWhiteSpace(bindingName))
            {
                throw new InvalidOperationException("binding set requires a binding name.");
            }

            var inferredTarget = ResolveBindingTarget(command, bindingName);
            service.UpsertBinding(bindingName, inferredTarget.TargetType, inferredTarget.TargetValue, inferredTarget.ProjectPath);
            Console.WriteLine($"${bindingName} -> {inferredTarget.TargetType}:{inferredTarget.TargetValue}");
            return 0;
        }
        case "binding" when command.Secondary == "delete":
        {
            var bindingName = NormalizeBindingName(command.Name ?? command.Operands.FirstOrDefault());
            if (string.IsNullOrWhiteSpace(bindingName))
            {
                throw new InvalidOperationException("binding delete requires a binding name.");
            }

            service.DeleteBinding(bindingName);
            Console.WriteLine($"deleted ${bindingName}");
            return 0;
        }
        case "preset" when command.Secondary == "list":
        {
            var state = service.GetState();
            Console.WriteLine($"{"Id",-24} {"Adapter",-8} {"Name",-28} Selectors");
            Console.WriteLine(new string('-', 96));
            foreach (var preset in state.Presets)
            {
                Console.WriteLine($"{preset.Id,-24} {preset.AdapterId,-8} {preset.Name,-28} {string.Join(", ", preset.Selectors)}");
            }

            Console.WriteLine();
            Console.WriteLine($"selected={state.SelectedPresetId ?? "(none)"}");
            return 0;
        }
        case "preset" when command.Secondary == "save":
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                throw new InvalidOperationException("preset save requires --name.");
            }

            var selectors = ResolveRequestedSelectors(service, command, allowDefaultPreset: false);
            var preset = service.SavePresetSelectors(
                command.PresetId,
                command.Name,
                selectors,
                command.AdapterId,
                command.BuildMode,
                selectAfterSave: true);
            Console.WriteLine($"{preset.Id} {preset.Name}");
            return 0;
        }
        case "preset" when command.Secondary == "select":
        {
            var presetId = command.PresetId ?? command.Operands.FirstOrDefault();
            var state = service.SelectPreset(presetId);
            Console.WriteLine(state.SelectedPresetId ?? "(none)");
            return 0;
        }
        case "preset" when command.Secondary == "delete":
        {
            var presetId = command.PresetId ?? command.Operands.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(presetId))
            {
                throw new InvalidOperationException("preset delete requires a preset id.");
            }

            service.DeletePreset(presetId);
            Console.WriteLine($"deleted {presetId}");
            return 0;
        }
        case "sdk" when command.Secondary == "export":
        {
            var path = await service.ExportSdkAsync();
            Console.WriteLine(path);
            return 0;
        }
        case "mod" when command.Secondary == "create":
        {
            var modId = command.Name ?? command.Operands.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new InvalidOperationException("mod create requires a mod id.");
            }

            var output = await service.CreateModAsync(modId, command.Template ?? "empty", command.DirectoryPath);
            Console.WriteLine(output);
            return 0;
        }
        case "mod" when command.Secondary == "fix-project":
        {
            var modId = command.Name ?? command.Operands.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new InvalidOperationException("mod fix-project requires a mod id.");
            }

            Console.WriteLine(service.FixModProject(modId));
            return 0;
        }
        case "mod" when command.Secondary == "solution":
        {
            var modId = command.Name ?? command.Operands.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new InvalidOperationException("mod solution requires a mod id.");
            }

            Console.WriteLine(await service.GenerateSolutionAsync(modId));
            return 0;
        }
        default:
            Console.Error.WriteLine($"Unsupported command: {string.Join(' ', args)}");
            PrintHelp();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int PrintCatalog(IReadOnlyList<LauncherModInfo> mods)
{
    Console.WriteLine($"{"Id",-28} {"Kind",-16} {"Build",-10} Root");
    Console.WriteLine(new string('-', 120));
    foreach (var mod in mods.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"{mod.Id,-28} {mod.Kind,-16} {mod.BuildState,-10} {mod.RootPath}");
    }

    return 0;
}

static int PrintBuildResults(IReadOnlyList<LauncherBuildResult> results)
{
    var failed = false;
    foreach (var result in results)
    {
        Console.WriteLine($"[{(result.Ok ? "OK" : "FAIL")}] {result.Id}");
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            Console.WriteLine(result.Output);
        }

        if (!result.Ok)
        {
            failed = true;
        }
    }

    return failed ? 1 : 0;
}

static async Task<int> RunRecordedLaunchAsync(
    LauncherService service,
    string repoRoot,
    IReadOnlyList<string> selectors,
    string adapterId,
    CliCommand command,
    string[] rawArgs)
{
    var resolveResult = service.Resolve(selectors, adapterId, command.BuildMode);
    var buildResults = await service.BuildAsync(selectors, adapterId, command.BuildMode);
    int buildExitCode = PrintBuildResults(buildResults);
    if (buildExitCode != 0)
    {
        return buildExitCode;
    }

    var appBuild = await service.BuildAppAsync(resolveResult.Plan.AdapterId);
    Console.WriteLine(appBuild.Output);
    if (!appBuild.Ok)
    {
        return appBuild.ExitCode;
    }

    string bootstrapPath = service.WriteBootstrap(selectors, resolveResult.Plan.AdapterId, command.BuildMode);
    string outputDirectory = ResolveOutputPath(repoRoot, command.RecordDirectory!);
    var recording = await LauncherEvidenceRecorder.RecordAsync(new LauncherRecordingRequest(
        repoRoot,
        resolveResult.Plan,
        bootstrapPath,
        outputDirectory,
        $".\\scripts\\run-mod-launcher.cmd cli {string.Join(' ', rawArgs)}"));

    Console.WriteLine($"adapter={resolveResult.Plan.AdapterId}");
    Console.WriteLine($"bootstrap={bootstrapPath}");
    Console.WriteLine($"recording={recording.OutputDirectory}");
    Console.WriteLine($"summary={recording.SummaryPath}");
    Console.WriteLine($"signature={recording.NormalizedSignature}");
    return 0;
}

static void PrintResolveResult(LauncherResolveResult result, bool asJson)
{
    if (asJson)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return;
    }

    Console.WriteLine($"adapter={result.Plan.AdapterId}");
    Console.WriteLine($"buildMode={result.Plan.BuildMode}");
    Console.WriteLine($"selectors={string.Join(", ", result.Plan.Selectors)}");
    Console.WriteLine($"rootMods={string.Join(", ", result.Plan.RootModIds)}");
    Console.WriteLine($"orderedMods={string.Join(", ", result.Plan.OrderedModIds)}");
    Console.WriteLine($"bootstrap={result.Plan.BootstrapArtifactPath}");
    Console.WriteLine();
    foreach (var mod in result.Plan.Mods)
    {
        var bindings = mod.BindingNames.Count == 0 ? string.Empty : $" bindings=[{string.Join(", ", mod.BindingNames)}]";
        Console.WriteLine($"- {mod.Id} | {mod.Kind} | {mod.BuildState} | {mod.RootPath}{bindings}");
    }
}

static IReadOnlyList<string> ResolveRequestedSelectors(LauncherService service, CliCommand command, bool allowDefaultPreset)
{
    var selectors = new List<string>();
    if (!string.IsNullOrWhiteSpace(command.PresetId))
    {
        selectors.Add($"preset:{command.PresetId}");
    }

    selectors.AddRange(command.SelectorValues.Select(selector => NormalizeSelector(service, selector)));
    selectors.AddRange(command.ModIds.Select(modId => $"mod:{modId}"));
    selectors.AddRange(command.PathValues.Select(path => $"path:{path}"));

    foreach (var operand in command.Operands)
    {
        selectors.Add(NormalizeSelector(service, operand));
    }

    if (selectors.Count > 0)
    {
        return selectors;
    }

    if (!allowDefaultPreset)
    {
        throw new InvalidOperationException("At least one selector is required.");
    }

    var selectedPresetId = service.GetState().SelectedPresetId;
    if (!string.IsNullOrWhiteSpace(selectedPresetId))
    {
        return new[] { $"preset:{selectedPresetId}" };
    }

    throw new InvalidOperationException("No selectors supplied and no preset is currently selected.");
}

static string NormalizeSelector(LauncherService service, string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return raw;
    }

    var trimmed = raw.Trim();
    if (trimmed.StartsWith('$') || trimmed.Contains(':'))
    {
        return trimmed;
    }

    if (Directory.Exists(trimmed) && File.Exists(Path.Combine(trimmed, "mod.json")))
    {
        return $"path:{trimmed}";
    }

    var state = service.GetState();
    if (state.Bindings.Any(binding => string.Equals(binding.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
    {
        return $"${trimmed}";
    }

    return $"mod:{trimmed}";
}

static string ResolveRequestedAdapter(LauncherService service, CliCommand command)
{
    if (!string.IsNullOrWhiteSpace(command.AdapterId))
    {
        return command.AdapterId;
    }

    return service.GetState().SelectedPlatformId;
}

static string ResolveRequiredAdapter(CliCommand command)
{
    if (string.IsNullOrWhiteSpace(command.AdapterId))
    {
        throw new InvalidOperationException("An adapter id is required.");
    }

    return command.AdapterId;
}

static BindingTarget ResolveBindingTarget(CliCommand command, string bindingName)
{
    if (!string.IsNullOrWhiteSpace(command.BindingTargetType) && !string.IsNullOrWhiteSpace(command.BindingTargetValue))
    {
        return new BindingTarget(command.BindingTargetType, command.BindingTargetValue, command.ProjectPath);
    }

    if (command.PathValues.Count > 0)
    {
        return new BindingTarget("path", command.PathValues[0], command.ProjectPath);
    }

    if (command.ModIds.Count > 0)
    {
        return new BindingTarget("modid", command.ModIds[0], command.ProjectPath);
    }

    if (command.Operands.Count >= 2)
    {
        var rawTarget = command.Operands[1];
        if (Directory.Exists(rawTarget) && File.Exists(Path.Combine(rawTarget, "mod.json")))
        {
            return new BindingTarget("path", rawTarget, command.ProjectPath);
        }

        return new BindingTarget("modid", rawTarget, command.ProjectPath);
    }

    throw new InvalidOperationException($"binding set {bindingName} requires --path, --mod, or an explicit target.");
}

static string NormalizeBindingName(string? raw)
{
    return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().TrimStart('$');
}

static string ResolveOutputPath(string repoRoot, string raw)
{
    if (Path.IsPathRooted(raw))
    {
        return Path.GetFullPath(raw);
    }

    return Path.GetFullPath(Path.Combine(repoRoot, raw));
}

static void PrintHelp()
{
    Console.WriteLine("""
Ludots launcher CLI

Commands
  catalog
  resolve [selectors...] [--adapter raylib|web] [--build auto|always|never] [--json]
  build [selectors...] [--adapter raylib|web] [--build auto|always|never]
  build app [--adapter raylib|web]
  launch [selectors...] [--adapter raylib|web] [--build auto|always|never] [--record <artifactDir>]
  adapter list
  adapter select --adapter raylib|web
  workspace list
  workspace add --path <mod-root-parent>
  binding list
  binding set <name> (--path <modRoot> | --mod <modId>) [--project <relativeOrAbsoluteCsproj>]
  binding delete <name>
  preset list
  preset save --name <name> [selectors...] [--adapter raylib|web] [--build auto|always|never]
  preset select <presetId>
  preset delete <presetId>
  sdk export
  mod create <modId> [--template empty|gameplay] [--dir <targetDir>]
  mod fix-project <modId>
  mod solution <modId>

Selectors
  $camera_acceptance
  camera_acceptance            (binding shorthand for PowerShell)
  mod:CameraAcceptanceMod
  path:D:\mods\MyMod
  preset:camera

Examples
  .\scripts\run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib
  .\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter web
  .\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib --record artifacts/acceptance/launcher-camera-acceptance-raylib
  .\scripts\run-mod-launcher.cmd cli binding set camera_acceptance --path mods/fixtures/camera/CameraAcceptanceMod
  .\scripts\run-mod-launcher.cmd cli preset save --name camera-web camera_acceptance --adapter web
""");
}

internal sealed class CliCommand
{
    public string Primary { get; private set; } = string.Empty;
    public string Secondary { get; private set; } = string.Empty;
    public string? AdapterId { get; private set; }
    public string? PresetId { get; private set; }
    public string? Name { get; private set; }
    public string? Template { get; private set; }
    public string? DirectoryPath { get; private set; }
    public string? ProjectPath { get; private set; }
    public string? RecordDirectory { get; private set; }
    public string? BindingTargetType { get; private set; }
    public string? BindingTargetValue { get; private set; }
    public LauncherBuildMode BuildMode { get; private set; } = LauncherBuildMode.Auto;
    public bool Json { get; private set; }
    public List<string> SelectorValues { get; } = new();
    public List<string> ModIds { get; } = new();
    public List<string> PathValues { get; } = new();
    public List<string> Operands { get; } = new();

    public static CliCommand Parse(string[] args)
    {
        string primary = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        string secondary = ResolveSecondary(primary, args);
        var command = new CliCommand
        {
            Primary = primary,
            Secondary = secondary
        };

        var index = string.IsNullOrWhiteSpace(command.Secondary) ? 1 : 2;
        for (; index < args.Length; index++)
        {
            var token = args[index];
            switch (token)
            {
                case "--adapter" when index + 1 < args.Length:
                    command.AdapterId = args[++index].Trim().ToLowerInvariant();
                    break;
                case "--preset" when index + 1 < args.Length:
                    command.PresetId = args[++index].Trim();
                    break;
                case "--name" when index + 1 < args.Length:
                    command.Name = args[++index].Trim();
                    break;
                case "--template" when index + 1 < args.Length:
                    command.Template = args[++index].Trim();
                    break;
                case "--dir" when index + 1 < args.Length:
                    command.DirectoryPath = args[++index].Trim();
                    break;
                case "--project" when index + 1 < args.Length:
                    command.ProjectPath = args[++index].Trim();
                    break;
                case "--record" when index + 1 < args.Length:
                    command.RecordDirectory = args[++index].Trim();
                    break;
                case "--target-type" when index + 1 < args.Length:
                    command.BindingTargetType = args[++index].Trim();
                    break;
                case "--target" when index + 1 < args.Length:
                    command.BindingTargetValue = args[++index].Trim();
                    break;
                case "--selector" when index + 1 < args.Length:
                    command.SelectorValues.Add(args[++index].Trim());
                    break;
                case "--mod" when index + 1 < args.Length:
                    command.ModIds.Add(args[++index].Trim());
                    break;
                case "--path" when index + 1 < args.Length:
                    command.PathValues.Add(args[++index].Trim());
                    break;
                case "--build" when index + 1 < args.Length:
                    command.BuildMode = ParseBuildMode(args[++index]);
                    break;
                case "--json":
                    command.Json = true;
                    break;
                default:
                    command.Operands.Add(token);
                    break;
            }
        }

        return command;
    }

    private static string ResolveSecondary(string primary, string[] args)
    {
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string candidate = args[1].ToLowerInvariant();
        return IsKnownSecondary(primary, candidate) ? candidate : string.Empty;
    }

    private static bool IsKnownSecondary(string primary, string secondary)
    {
        return primary switch
        {
            "mods" => secondary is "list",
            "build" => secondary is "app",
            "adapter" => secondary is "list" or "select",
            "workspace" => secondary is "list" or "add",
            "binding" => secondary is "list" or "set" or "delete",
            "preset" => secondary is "list" or "save" or "select" or "delete",
            "sdk" => secondary is "export",
            "mod" => secondary is "create" or "fix-project" or "solution",
            _ => false
        };
    }

    private static LauncherBuildMode ParseBuildMode(string raw)
    {
        return Enum.TryParse<LauncherBuildMode>(raw, true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported build mode: {raw}");
    }
}

internal sealed record BindingTarget(string TargetType, string TargetValue, string? ProjectPath);
