using System.Text.Json.Nodes;
using Ludots.Tools.FieldEditor;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0])
    {
        case "layers":
            return Layers(args);
        case "new-layer":
            return NewLayer(args);
        case "regions":
            return Regions(args);
        case "regions-add":
            return RegionsAdd(args);
        case "regions-remove":
            return RegionsRemove(args);
        case "rect":
            return Rect(args, erase: false);
        case "erase":
            return Rect(args, erase: true);
        case "render":
            return Render(args);
        case "save":
            return Save(args);
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage();
            return 1;
    }
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
    field-editor — author discrete-id field layers of a mod (engine format: #1175)

    layers        --mod <dir>
    new-layer     --mod <dir> --id <layerKey> [--cell-size 500] [--chunk 16] [--max-regions 256] [--writer map.field]
    regions       --mod <dir> --layer <layerKey>
    regions-add   --mod <dir> --layer <layerKey> --key <regionKey>
    regions-remove--mod <dir> --layer <layerKey> --key <regionKey>
    rect          --mod <dir> --layer <layerKey> --key <regionKey> --from x0,y0 --to x1,y1
    erase         --mod <dir> --layer <layerKey> --from x0,y0 --to x1,y1
    render        --mod <dir> --layer <layerKey> [--bounds x0,y0,x1,y1]
    save          --mod <dir> --layer <layerKey>

    Every mutating command writes the cells document immediately (atomic temp+move);
    'save' additionally validates against the catalog capacity.
    """);
}

static string RequireOption(string[] args, string name)
{
    for (int i = 1; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.Ordinal))
        {
            return args[i + 1];
        }
    }

    throw new InvalidOperationException($"Missing required option '{name}'.");
}

static string ModAssetsRoot(string[] args) => RequireOption(args, "--mod");

static (CellsDocument Document, int MaxRegionIds, string AssetPath) OpenDocument(string[] args)
{
    string mod = ModAssetsRoot(args);
    string layerKey = RequireOption(args, "--layer");
    JsonArray catalog = CatalogDocument.LoadOrNew(CatalogDocument.AssetPath(mod));
    (int CellSizeCm, int MaxRegionIds)? layer = CatalogDocument.TryGetDiscreteLayer(catalog, layerKey)
        ?? throw new InvalidOperationException(
            $"Layer '{layerKey}' is not a discrete-id layer of mod '{mod}' (Fields/layers.json).");
    string assetPath = CellsDocument.AssetPath(mod, layerKey);
    return (CellsDocument.LoadOrNew(assetPath, layerKey), layer.Value.MaxRegionIds, assetPath);
}

static int Layers(string[] args)
{
    JsonArray catalog = CatalogDocument.LoadOrNew(CatalogDocument.AssetPath(ModAssetsRoot(args)));
    if (catalog.Count == 0)
    {
        Console.WriteLine("(no layers declared)");
        return 0;
    }

    foreach (JsonNode? layer in catalog)
    {
        string id = layer!["id"]?.GetValue<string>() ?? "";
        string kind = layer["kind"]?.GetValue<string>() ?? "";
        Console.WriteLine($"{id,-28} kind={kind,-12} cellSize={layer["cellSizeCm"]} chunk={layer["chunkSizeCells"]}");
    }

    return 0;
}

static int NewLayer(string[] args)
{
    string mod = ModAssetsRoot(args);
    string id = RequireOption(args, "--id");
    int cellSize = int.Parse(RequireOption(args, "--cell-size"));
    int chunk = int.Parse(RequireOption(args, "--chunk"));
    int maxRegions = int.Parse(RequireOption(args, "--max-regions"));
    string writerDomain = RequireOption(args, "--writer");

    string path = CatalogDocument.AssetPath(mod);
    JsonArray catalog = CatalogDocument.LoadOrNew(path);
    CatalogDocument.AppendLayer(catalog, id, cellSize, chunk, maxRegions, writerDomain);
    CatalogDocument.Save(path, catalog);
    Console.WriteLine($"Layer '{id}' appended to {path}.");
    return 0;
}

static int Regions(string[] args)
{
    (CellsDocument document, _, _) = OpenDocument(args);
    foreach (string key in document.Regions.Keys)
    {
        int cells = document.Cells.Values.Count(value => string.Equals(value, key, StringComparison.Ordinal));
        Console.WriteLine($"{key}  cells={cells}");
    }

    return 0;
}

static int RegionsAdd(string[] args)
{
    (CellsDocument document, int max, string path) = OpenDocument(args);
    string key = document.AddRegion(RequireOption(args, "--key"));
    document.Save(path, max);
    Console.WriteLine($"Region '{key}' added.");
    return 0;
}

static int RegionsRemove(string[] args)
{
    (CellsDocument document, int max, string path) = OpenDocument(args);
    document.RemoveRegion(RequireOption(args, "--key"));
    document.Save(path, max);
    Console.WriteLine("Region removed (its cells were cleared).");
    return 0;
}

static (int X, int Y) ParseCoord(string value)
{
    string[] parts = value.Split(',');
    if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
    {
        throw new InvalidOperationException($"Coordinate '{value}' must be x,y integers.");
    }

    return (x, y);
}

static int Rect(string[] args, bool erase)
{
    (CellsDocument document, int max, string path) = OpenDocument(args);
    (int x0, int y0) = ParseCoord(RequireOption(args, "--from"));
    (int x1, int y1) = ParseCoord(RequireOption(args, "--to"));
    if (erase)
    {
        document.EraseRect(x0, y0, x1, y1);
    }
    else
    {
        document.PaintRect(RequireOption(args, "--key"), x0, y0, x1, y1);
    }

    document.Save(path, max);
    Console.WriteLine($"{(erase ? "Erased" : "Painted")} rect ({x0},{y0})-({x1},{y1}).");
    return 0;
}

static int Render(string[] args)
{
    (CellsDocument document, _, _) = OpenDocument(args);
    var cells = document.Cells;
    if (cells.Count == 0)
    {
        Console.WriteLine("(no painted cells)");
        return 0;
    }

    int minX = cells.Keys.Min(key => key.X);
    int maxX = cells.Keys.Max(key => key.X);
    int minY = cells.Keys.Min(key => key.Y);
    int maxY = cells.Keys.Max(key => key.Y);
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

    Console.WriteLine($"bounds ({minX},{minY})-({maxX},{maxY})  legend: " +
        string.Join(" ", digitByRegion.Select(pair => $"{pair.Value}={pair.Key}")));
    for (int y = minY; y <= maxY; y++)
    {
        var row = new System.Text.StringBuilder();
        for (int x = minX; x <= maxX; x++)
        {
            row.Append(cells.TryGetValue((x, y), out string? key) && digitByRegion.TryGetValue(key, out char digit)
                ? digit
                : '.');
        }

        Console.WriteLine(row.ToString());
    }

    return 0;
}

static int Save(string[] args)
{
    (CellsDocument document, int max, string path) = OpenDocument(args);
    document.Save(path, max);
    Console.WriteLine($"Saved {path} ({document.Regions.Count} regions, {document.Cells.Count} cells).");
    return 0;
}
