using Ludots.Launcher.Backend;

var repoRoot = LauncherService.FindRepoRoot(AppDomain.CurrentDomain.BaseDirectory);
var service = new LauncherService(repoRoot);
var command = CliCommand.Parse(args);

try
{
    switch (command.Primary)
    {
        case "presets" when command.Secondary == "list":
        {
            var state = service.GetState();
            Console.WriteLine($"{"Id",-20} {"Name",-28} {"Mods",5}");
            Console.WriteLine(new string('-', 60));
            foreach (var preset in state.Presets)
            {
                Console.WriteLine($"{preset.Id,-20} {preset.Name,-28} {preset.ActiveModIds.Count,5}");
            }

            Console.WriteLine();
            Console.WriteLine($"Selected preset: {state.SelectedPresetId ?? "(none)"}");
            return 0;
        }
        case "presets" when command.Secondary == "save":
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                throw new InvalidOperationException("--name is required for 'presets save'.");
            }

            var preset = service.SavePreset(command.PresetId, command.Name, ResolveRequestedMods(service, command), includeDependencies: true, selectAfterSave: true);
            Console.WriteLine($"Saved preset {preset.Id} ({preset.Name}).");
            return 0;
        }
        case "presets" when command.Secondary == "delete":
        {
            if (string.IsNullOrWhiteSpace(command.PresetId))
            {
                throw new InvalidOperationException("--preset is required for 'presets delete'.");
            }

            service.DeletePreset(command.PresetId);
            Console.WriteLine($"Deleted preset {command.PresetId}.");
            return 0;
        }
        case "presets" when command.Secondary == "select":
        {
            service.SelectPreset(command.PresetId);
            Console.WriteLine($"Selected preset: {command.PresetId ?? "(custom)"}");
            return 0;
        }
        case "mods" when command.Secondary == "build":
        {
            var results = await service.BuildModsAsync(ResolveRequestedMods(service, command));
            return PrintBuildResults(results);
        }
        case "mod" when command.Secondary == "fix-project":
        {
            if (string.IsNullOrWhiteSpace(command.ModIds.FirstOrDefault()))
            {
                throw new InvalidOperationException("--mod is required for 'mod fix-project'.");
            }

            var path = service.FixModProject(command.ModIds[0]);
            Console.WriteLine(path);
            return 0;
        }
        case "app" when command.Secondary == "build":
        {
            var result = await service.BuildAppAsync(command.PlatformId);
            Console.WriteLine(result.Output);
            return result.Ok ? 0 : result.ExitCode;
        }
        case "gamejson" when command.Secondary == "write":
        {
            var path = service.WriteGameJson(command.PlatformId, ResolveRequestedMods(service, command));
            Console.WriteLine(path);
            return 0;
        }
        case "run":
        {
            var result = await service.LaunchAsync(command.PlatformId, ResolveRequestedMods(service, command));
            if (!result.Ok)
            {
                Console.Error.WriteLine(result.Error);
                return 1;
            }

            Console.WriteLine($"Started pid={result.Pid}");
            if (!string.IsNullOrWhiteSpace(result.Url))
            {
                Console.WriteLine(result.Url);
            }

            return 0;
        }
        default:
            Console.Error.WriteLine("Unsupported command.");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
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

static IReadOnlyList<string> ResolveRequestedMods(LauncherService service, CliCommand command)
{
    if (command.ModIds.Count > 0)
    {
        return command.ModIds;
    }

    var state = service.GetState();
    var presetId = string.IsNullOrWhiteSpace(command.PresetId) ? state.SelectedPresetId : command.PresetId;
    if (string.IsNullOrWhiteSpace(presetId))
    {
        throw new InvalidOperationException("No preset selected and no --mods supplied.");
    }

    var preset = state.Presets.FirstOrDefault(item => string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
    if (preset == null)
    {
        throw new InvalidOperationException($"Preset not found: {presetId}");
    }

    return preset.ActiveModIds;
}

internal sealed class CliCommand
{
    public string Primary { get; private set; } = string.Empty;
    public string Secondary { get; private set; } = string.Empty;
    public string PlatformId { get; private set; } = LauncherPlatformIds.Raylib;
    public string? PresetId { get; private set; }
    public string? Name { get; private set; }
    public List<string> ModIds { get; } = new();

    public static CliCommand Parse(string[] args)
    {
        var command = new CliCommand
        {
            Primary = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty,
            Secondary = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty
        };

        for (var index = 2; index < args.Length; index++)
        {
            var token = args[index];
            switch (token)
            {
                case "--platform" when index + 1 < args.Length:
                    command.PlatformId = args[++index].ToLowerInvariant();
                    break;
                case "--preset" when index + 1 < args.Length:
                    command.PresetId = args[++index];
                    break;
                case "--name" when index + 1 < args.Length:
                    command.Name = args[++index];
                    break;
                case "--mod" when index + 1 < args.Length:
                    command.ModIds.Add(args[++index]);
                    break;
                case "--mods" when index + 1 < args.Length:
                    command.ModIds.AddRange(args[++index].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
            }
        }

        return command;
    }
}
