using System.Text;
using System.Text.Json.Nodes;
using Ludots.Tools.FieldEditor;

return Execute(args);

static int Execute(string[] commandArgs)
{
    if (commandArgs.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    try
    {
        return commandArgs[0] switch
        {
            "layers" => Layers(commandArgs),
            "new-layer" => NewLayer(commandArgs),
            "regions" => Regions(commandArgs),
            "regions-add" => RegionsAdd(commandArgs),
            "regions-remove" => RegionsRemove(commandArgs),
            "regions-rename" => RegionsRename(commandArgs),
            "regions-color" => RegionsColor(commandArgs),
            "colors" => Colors(commandArgs),
            "cell" => Cell(commandArgs),
            "pick" or "eyedrop" => Pick(commandArgs),
            "brush" => Brush(commandArgs),
            "undo" => UndoRedo(commandArgs, redo: false),
            "redo" => UndoRedo(commandArgs, redo: true),
            "rect" => Rect(commandArgs, erase: false),
            "erase" => Rect(commandArgs, erase: true),
            "render" => Render(commandArgs),
            "save" => Save(commandArgs),
            "session" => Session(commandArgs),
            "canvas" => Canvas(commandArgs),
            _ => UnknownCommand(commandArgs[0]),
        };
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
    field-editor — author discrete-id field layers of a mod (engine format: Fields/cells schema v2 rects)

    layers         --mod <dir>
    new-layer      --mod <dir> --id <layerKey> [--cell-size 500] [--chunk 16] [--max-regions 256] [--writer map.field]
    regions        --mod <dir> --layer <layerKey>
    regions-add    --mod <dir> --layer <layerKey> --key <regionKey>
    regions-remove --mod <dir> --layer <layerKey> --key <regionKey>
    regions-rename --mod <dir> --layer <layerKey> --from <oldKey> --to <newKey>
    regions-color  --mod <dir> --layer <layerKey> --key <regionKey> --color #RRGGBB
    colors         --mod <dir> --layer <layerKey>
    cell           --mod <dir> --layer <layerKey> --at x,y [--key <regionKey>|--erase]
    pick|eyedrop   --mod <dir> --layer <layerKey> --at x,y
    rect           --mod <dir> --layer <layerKey> [--key <regionKey>] --from x0,y0 --to x1,y1
    brush          --mod <dir> --layer <layerKey> [--key <regionKey>] --at x,y --radius N
    erase          --mod <dir> --layer <layerKey> --from x0,y0 --to x1,y1
    undo|redo      --mod <dir> --layer <layerKey>
    render         --mod <dir> --layer <layerKey> [--bounds x0,y0,x1,y1]
    save           --mod <dir> --layer <layerKey>
    session        --mod <dir> --layer <layerKey>
    canvas         --mod <dir> --layer <layerKey>

    new-layer accepts optional --map <mapId> to append the layer into Maps/<mapId>.json Fields.Layers.
    pick selects the active brush key; rect and brush use it when --key is omitted.
    brush paints a square Chebyshev-radius footprint in cell space.
    Mutating field commands persist undo/redo beside the cells asset.
    canvas requires a graphical display and writes the cells asset only when S is pressed.
    """);
}

static string RequireOption(string[] commandArgs, string name)
{
    for (int index = 1; index < commandArgs.Length; index++)
    {
        if (!string.Equals(commandArgs[index], name, StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 >= commandArgs.Length ||
            commandArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Option '{name}' requires a value.");
        }

        return commandArgs[index + 1];
    }

    throw new InvalidOperationException($"Missing required option '{name}'.");
}

static bool HasOption(string[] commandArgs, string name) =>
    commandArgs.Any(argument => string.Equals(argument, name, StringComparison.Ordinal));

static string ModRoot(string[] commandArgs) => RequireOption(commandArgs, "--mod");

static (CellsDocument Document, int MaxRegionIds, string AssetPath) OpenDocument(
    string[] commandArgs)
{
    string mod = ModRoot(commandArgs);
    string layerKey = RequireOption(commandArgs, "--layer");
    JsonArray catalog = CatalogDocument.LoadOrNew(CatalogDocument.AssetPath(mod));
    (int CellSizeCm, int ChunkSizeCells, int MaxRegionIds)? layer =
        CatalogDocument.TryGetDiscreteLayer(catalog, layerKey)
        ?? throw new InvalidOperationException(
            $"Layer '{layerKey}' is not a discrete-id layer of mod '{mod}' (Fields/layers.json).");
    string assetPath = CellsDocument.AssetPath(mod, layerKey);
    return (
        CellsDocument.LoadOrNew(assetPath, layerKey, layer.Value.ChunkSizeCells),
        layer.Value.MaxRegionIds,
        assetPath);
}

static int Layers(string[] commandArgs)
{
    JsonArray catalog = CatalogDocument.LoadOrNew(CatalogDocument.AssetPath(ModRoot(commandArgs)));
    if (catalog.Count == 0)
    {
        Console.WriteLine("(no layers declared)");
        return 0;
    }

    foreach (JsonNode? layer in catalog)
    {
        string id = layer!["id"]?.GetValue<string>() ?? "";
        string kind = layer["kind"]?.GetValue<string>() ?? "";
        Console.WriteLine(
            $"{id,-28} kind={kind,-12} cellSize={layer["cellSizeCm"]} chunk={layer["chunkSizeCells"]}");
    }

    return 0;
}

static int NewLayer(string[] commandArgs)
{
    string mod = ModRoot(commandArgs);
    string id = RequireOption(commandArgs, "--id");
    int cellSize = TryIntOption(commandArgs, "--cell-size", 500);
    int chunk = TryIntOption(commandArgs, "--chunk", 16);
    int maxRegions = TryIntOption(commandArgs, "--max-regions", 256);
    string writerDomain = TryStringOption(commandArgs, "--writer") ?? "map.field";

    string path = CatalogDocument.AssetPath(mod);
    JsonArray catalog = CatalogDocument.LoadOrNew(path);
    CatalogDocument.AppendLayer(
        catalog,
        id,
        cellSize,
        chunk,
        maxRegions,
        writerDomain);
    CatalogDocument.Save(path, catalog);
    Console.WriteLine($"Layer '{id}' appended to {path}.");

    string? mapId = TryStringOption(commandArgs, "--map");
    if (!string.IsNullOrWhiteSpace(mapId))
    {
        string mapPath = Path.Combine(mod, "assets", "Maps", $"{mapId}.json");
        if (!File.Exists(mapPath))
        {
            throw new InvalidOperationException(
                $"Map asset '{mapPath}' does not exist; create the map before --map.");
        }

        JsonObject map = JsonNode.Parse(File.ReadAllText(mapPath)) as JsonObject
            ?? throw new InvalidOperationException($"'{mapPath}' must be a JSON object.");
        if (map["Fields"] is not JsonObject fields)
        {
            fields = new JsonObject();
            map["Fields"] = fields;
        }

        if (fields["Layers"] is not JsonArray layers)
        {
            layers = new JsonArray();
            fields["Layers"] = layers;
        }

        bool already = layers.Any(
            node => string.Equals(node?.GetValue<string>(), id, StringComparison.Ordinal));
        if (!already)
        {
            layers.Add(id);
            string tempPath = mapPath + ".tmp";
            File.WriteAllText(
                tempPath,
                map.ToJsonString(
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
            File.Move(tempPath, mapPath, overwrite: true);
            Console.WriteLine($"Enabled '{id}' on map '{mapId}'.");
        }
        else
        {
            Console.WriteLine($"Map '{mapId}' already enables '{id}'.");
        }
    }

    return 0;
}

static int TryIntOption(string[] commandArgs, string name, int fallback)
{
    string? value = TryStringOption(commandArgs, name);
    if (value == null)
    {
        return fallback;
    }

    if (!int.TryParse(value, out int parsed))
    {
        throw new InvalidOperationException($"Option '{name}' must be an integer.");
    }

    return parsed;
}

static string? TryStringOption(string[] commandArgs, string name)
{
    for (int index = 1; index < commandArgs.Length; index++)
    {
        if (!string.Equals(commandArgs[index], name, StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 >= commandArgs.Length ||
            commandArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Option '{name}' requires a value.");
        }

        return commandArgs[index + 1];
    }

    return null;
}

static int Regions(string[] commandArgs)
{
    (CellsDocument document, _, _) = OpenDocument(commandArgs);
    foreach (string key in document.Regions.Keys)
    {
        Console.WriteLine($"{key}  cells={document.GetRegionCellCount(key)}");
    }

    return 0;
}

static int RegionsAdd(string[] commandArgs)
{
    (CellsDocument document, int max, string path) = OpenDocument(commandArgs);
    JsonObject before = PrepareMutation(path, document);
    string key = document.AddRegion(RequireOption(commandArgs, "--key"));
    CommitMutation(path, max, document, before);
    Console.WriteLine($"Region '{key}' added.");
    return 0;
}

static int RegionsRemove(string[] commandArgs)
{
    (CellsDocument document, int max, string path) = OpenDocument(commandArgs);
    string key = RequireOption(commandArgs, "--key");
    FieldEditorMetadataStore.GetColors(path, document);
    JsonObject before = PrepareMutation(path, document);
    document.RemoveRegion(key);
    CommitMutation(path, max, document, before);
    FieldEditorMetadataStore.RemoveRegion(path, document.LayerKey, key);
    HistoryStore.RemoveRegionKey(path, document.LayerKey, key);
    Console.WriteLine("Region removed (its cells were cleared).");
    return 0;
}

static int RegionsRename(string[] commandArgs)
{
    (CellsDocument document, int max, string path) = OpenDocument(commandArgs);
    string from = RequireOption(commandArgs, "--from");
    string to = RequireOption(commandArgs, "--to");
    FieldEditorMetadataStore.GetColors(path, document);
    JsonObject before = PrepareMutation(path, document);
    document.RenameRegion(from, to);
    CommitMutation(path, max, document, before);
    FieldEditorMetadataStore.RenameRegion(path, document.LayerKey, from, to);
    HistoryStore.RenameRegionKey(path, document.LayerKey, from, to);
    Console.WriteLine($"Region '{from}' renamed to '{to}'.");
    return 0;
}

static int RegionsColor(string[] commandArgs)
{
    (CellsDocument document, _, string path) = OpenDocument(commandArgs);
    string key = RequireOption(commandArgs, "--key");
    JsonObject before = PrepareMutation(path, document);
    string color = FieldEditorMetadataStore.SetColor(
        path,
        document,
        key,
        RequireOption(commandArgs, "--color"));
    HistoryStore.PushSnapshot(path, document.LayerKey, before);
    Console.WriteLine($"{key} = {color}");
    return 0;
}

static int Colors(string[] commandArgs)
{
    (CellsDocument document, _, string path) = OpenDocument(commandArgs);
    IReadOnlyDictionary<string, string> colors =
        FieldEditorMetadataStore.GetColors(path, document);
    foreach (string key in document.Regions.Keys)
    {
        Console.WriteLine(
            colors.TryGetValue(key, out string? color)
                ? $"{key}  color={color}"
                : $"{key}  color=(unset)");
    }

    return 0;
}

static int Cell(string[] commandArgs)
{
    (CellsDocument document, int max, string path) = OpenDocument(commandArgs);
    (int x, int y) = ParseCoord(RequireOption(commandArgs, "--at"));
    if (HasOption(commandArgs, "--erase"))
    {
        JsonObject before = PrepareMutation(path, document);
        document.EraseRect(x, y, x, y);
        CommitMutation(path, max, document, before);
        Console.WriteLine($"Erased cell ({x},{y}).");
        return 0;
    }

    if (HasOption(commandArgs, "--key"))
    {
        JsonObject before = PrepareMutation(path, document);
        string key = RequireOption(commandArgs, "--key");
        document.PaintCell(key, x, y);
        CommitMutation(path, max, document, before);
        Console.WriteLine($"Painted cell ({x},{y}) = {key}.");
        return 0;
    }

    Console.WriteLine(
        document.TryGetCellKey(x, y, out string? existing)
            ? existing
            : "(empty)");
    return 0;
}

static int Pick(string[] commandArgs)
{
    (CellsDocument document, _, string path) = OpenDocument(commandArgs);
    (int x, int y) = ParseCoord(RequireOption(commandArgs, "--at"));
    if (!document.TryGetCellKey(x, y, out string? key))
    {
        Console.WriteLine("(empty)");
        return 0;
    }

    HistoryStore.SetActiveBrushKey(path, document, key!);
    Console.WriteLine(key);
    return 0;
}

static int Brush(string[] commandArgs)
{
    (CellsDocument document, int max, string path) = OpenDocument(commandArgs);
    (int x, int y) = ParseCoord(RequireOption(commandArgs, "--at"));
    int radius = RequireNonNegativeInt(commandArgs, "--radius");
    string key = ResolveBrushKey(commandArgs, path, document);
    (int x0, int y0, int x1, int y1) = SquareBounds(x, y, radius);

    JsonObject before = PrepareMutation(path, document);
    document.PaintRect(key, x0, y0, x1, y1);
    CommitMutation(path, max, document, before);
    Console.WriteLine(
        $"Painted square brush at ({x},{y}), radius={radius}, key={key}.");
    return 0;
}

static int UndoRedo(string[] commandArgs, bool redo)
{
    (CellsDocument document, int max, string path) = OpenDocument(commandArgs);
    CellsDocument? restored = redo
        ? HistoryStore.Redo(path, document)
        : HistoryStore.Undo(path, document);
    if (restored == null)
    {
        Console.WriteLine(redo ? "(nothing to redo)" : "(nothing to undo)");
        return 0;
    }

    restored.Save(path, max);
    Console.WriteLine(redo ? "Redo applied." : "Undo applied.");
    return 0;
}

static int Rect(string[] commandArgs, bool erase)
{
    (CellsDocument document, int max, string path) = OpenDocument(commandArgs);
    (int x0, int y0) = ParseCoord(RequireOption(commandArgs, "--from"));
    (int x1, int y1) = ParseCoord(RequireOption(commandArgs, "--to"));
    string? key = erase ? null : ResolveBrushKey(commandArgs, path, document);

    JsonObject before = PrepareMutation(path, document);
    if (erase)
    {
        document.EraseRect(x0, y0, x1, y1);
    }
    else
    {
        document.PaintRect(key!, x0, y0, x1, y1);
    }

    CommitMutation(path, max, document, before);
    Console.WriteLine(
        $"{(erase ? "Erased" : "Painted")} rect ({x0},{y0})-({x1},{y1}).");
    return 0;
}

static int Render(string[] commandArgs)
{
    (CellsDocument document, _, _) = OpenDocument(commandArgs);
    (int minX, int minY, int maxX, int maxY) bounds;
    string? requestedBounds = TryStringOption(commandArgs, "--bounds");
    if (requestedBounds != null)
    {
        bounds = ParseBounds(requestedBounds);
    }
    else if (document.TryGetBounds(out int minX, out int minY, out int maxX, out int maxY))
    {
        bounds = (minX, minY, maxX, maxY);
    }
    else
    {
        Console.WriteLine("(no painted cells)");
        return 0;
    }

    var digitByRegion = new Dictionary<string, char>(StringComparer.Ordinal);
    char nextDigit = '1';
    foreach (string key in document.Regions.Keys)
    {
        if (nextDigit > '9')
        {
            break;
        }

        digitByRegion[key] = nextDigit++;
    }

    Console.WriteLine(
        $"bounds ({bounds.minX},{bounds.minY})-({bounds.maxX},{bounds.maxY})  legend: " +
        string.Join(" ", digitByRegion.Select(pair => $"{pair.Value}={pair.Key}")));
    for (int y = bounds.minY; y <= bounds.maxY; y++)
    {
        var row = new StringBuilder();
        for (int x = bounds.minX; x <= bounds.maxX; x++)
        {
            row.Append(
                document.TryGetCellKey(x, y, out string? key) &&
                digitByRegion.TryGetValue(key!, out char digit)
                    ? digit
                    : '.');
        }

        Console.WriteLine(row.ToString());
    }

    return 0;
}

static int Save(string[] commandArgs)
{
    (CellsDocument document, int max, string path) = OpenDocument(commandArgs);
    document.Save(path, max);
    Console.WriteLine(
        $"Saved {path} ({document.Regions.Count} regions, {document.CellCount} cells).");
    return 0;
}

static int Session(string[] commandArgs)
{
    string mod = ModRoot(commandArgs);
    string layer = RequireOption(commandArgs, "--layer");
    OpenDocument(commandArgs);
    Console.WriteLine(
        $"FieldEditor session opened for '{layer}'. Enter subcommands; use 'quit' to exit.");

    while (true)
    {
        if (!Console.IsInputRedirected)
        {
            Console.Write("field-editor> ");
        }

        string? line = Console.ReadLine();
        if (line == null)
        {
            return 0;
        }

        string[] tokens;
        try
        {
            tokens = SplitCommandLine(line);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            continue;
        }
        if (tokens.Length == 0)
        {
            continue;
        }

        if (string.Equals(tokens[0], "quit", StringComparison.Ordinal) ||
            string.Equals(tokens[0], "exit", StringComparison.Ordinal))
        {
            return 0;
        }

        if (string.Equals(tokens[0], "session", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Nested FieldEditor sessions are not supported.");
            continue;
        }

        var inherited = new List<string>(tokens);
        AddInheritedOption(inherited, "--mod", mod);
        AddInheritedOption(inherited, "--layer", layer);
        Execute(inherited.ToArray());
    }
}

static int Canvas(string[] commandArgs)
{
    (CellsDocument document, int maxRegionIds, string assetPath) =
        OpenDocument(commandArgs);
    return CanvasApp.Run(document, maxRegionIds, assetPath);
}

static JsonObject PrepareMutation(string path, CellsDocument document)
{
    return HistoryStore.CaptureSnapshot(path, document);
}

static void CommitMutation(
    string path,
    int maxRegionIds,
    CellsDocument document,
    JsonObject before)
{
    document.Save(path, maxRegionIds);
    HistoryStore.PushSnapshot(path, document.LayerKey, before);
}

static string ResolveBrushKey(
    string[] commandArgs,
    string path,
    CellsDocument document)
{
    string? key = TryStringOption(commandArgs, "--key")
        ?? HistoryStore.GetActiveBrushKey(path, document.LayerKey);
    if (key == null)
    {
        throw new InvalidOperationException(
            "No region key was supplied and no active brush is selected; use --key or pick.");
    }

    document.RegionIndex(key);
    return key;
}

static int RequireNonNegativeInt(string[] commandArgs, string name)
{
    string value = RequireOption(commandArgs, name);
    if (!int.TryParse(value, out int parsed) || parsed < 0)
    {
        throw new InvalidOperationException($"Option '{name}' must be a non-negative integer.");
    }

    return parsed;
}

static (int X, int Y) ParseCoord(string value)
{
    string[] parts = value.Split(',');
    if (parts.Length != 2 ||
        !int.TryParse(parts[0], out int x) ||
        !int.TryParse(parts[1], out int y))
    {
        throw new InvalidOperationException(
            $"Coordinate '{value}' must be x,y integers.");
    }

    return (x, y);
}

static (int MinX, int MinY, int MaxX, int MaxY) ParseBounds(string value)
{
    string[] parts = value.Split(',');
    if (parts.Length != 4 ||
        !int.TryParse(parts[0], out int minX) ||
        !int.TryParse(parts[1], out int minY) ||
        !int.TryParse(parts[2], out int maxX) ||
        !int.TryParse(parts[3], out int maxY) ||
        maxX < minX ||
        maxY < minY)
    {
        throw new InvalidOperationException(
            $"Bounds '{value}' must be x0,y0,x1,y1 integers with ordered ends.");
    }

    return (minX, minY, maxX, maxY);
}

static (int X0, int Y0, int X1, int Y1) SquareBounds(
    int x,
    int y,
    int radius)
{
    try
    {
        return checked((x - radius, y - radius, x + radius, y + radius));
    }
    catch (OverflowException ex)
    {
        throw new InvalidOperationException(
            "Brush bounds exceed the supported cell coordinate range.",
            ex);
    }
}

static string[] SplitCommandLine(string line)
{
    var tokens = new List<string>();
    var current = new StringBuilder();
    char quote = '\0';
    foreach (char value in line)
    {
        if (quote != '\0')
        {
            if (value == quote)
            {
                quote = '\0';
            }
            else
            {
                current.Append(value);
            }

            continue;
        }

        if (value == '"' || value == '\'')
        {
            quote = value;
        }
        else if (char.IsWhiteSpace(value))
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }
        else
        {
            current.Append(value);
        }
    }

    if (quote != '\0')
    {
        throw new InvalidOperationException("Session command contains an unterminated quote.");
    }

    if (current.Length > 0)
    {
        tokens.Add(current.ToString());
    }

    return tokens.ToArray();
}

static void AddInheritedOption(List<string> commandArgs, string name, string value)
{
    if (!commandArgs.Any(argument => string.Equals(argument, name, StringComparison.Ordinal)))
    {
        commandArgs.Add(name);
        commandArgs.Add(value);
    }
}
