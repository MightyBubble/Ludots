using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

const int survivorCount = 10000;
const int repeats = 3;
string outputDirectory = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outputDirectory);
File.WriteAllText(Path.Combine(outputDirectory, "environment.json"), JsonSerializer.Serialize(new
{
    TimestampUtc = DateTime.UtcNow,
    Framework = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    ProcessorCount = Environment.ProcessorCount,
    TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    SurvivorCount = survivorCount,
    Repeats = repeats
}, new JsonSerializerOptions { WriteIndented = true }));

using var csv = new StreamWriter(Path.Combine(outputDirectory, "samples.csv"));
csv.WriteLine("requested_capacity,actual_capacity,survivors,deletes,mode,repeat,elapsed_ms,allocated_bytes,remaining_entries");
foreach (int capacity in new[] { 16384, 131072 })
{
    foreach (int deletes in new[] { 100, 1000, 5000, 10000 })
    {
        foreach (bool releasePresenter in new[] { false, true })
            Run(capacity, deletes, releasePresenter);
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            foreach (bool releasePresenter in repeat % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                var result = Run(capacity, deletes, releasePresenter);
                string mode = releasePresenter ? "remove_then_release" : "remove_only_control";
                string row = FormattableString.Invariant($"{capacity},{result.Capacity},{survivorCount},{deletes},{mode},{repeat},{result.Milliseconds:F6},{result.Allocated},{result.Count}");
                csv.WriteLine(row);
                csv.Flush();
                Console.WriteLine(row);
            }
        }
    }
}

static (int Capacity, double Milliseconds, long Allocated, int Count) Run(int capacity, int deletes, bool releasePresenter)
{
    var table = new PresenterVisualStableIdTable(new PresentationStableIdAllocator(), capacity);
    var survivorIds = new int[survivorCount];
    for (int i = 0; i < survivorCount; i++)
        survivorIds[i] = table.GetOrAllocate(Key(i + 1));
    var deletedIds = new int[deletes];
    for (int i = 0; i < deletes; i++)
        deletedIds[i] = table.GetOrAllocate(Key(survivorCount + i + 1));

    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < deletes; i++)
    {
        var key = Key(survivorCount + i + 1);
        if (!table.Remove(in key, out int stableId) || stableId != deletedIds[i])
            throw new InvalidOperationException("Exact removal lost an expected identity.");
        if (releasePresenter && table.ReleasePresenter(key.PresenterStableId) != 0)
            throw new InvalidOperationException("Unexpected residual presenter visual.");
    }
    double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

    if (table.Count != survivorCount)
        throw new InvalidOperationException("Survivor count changed.");
    for (int i = 0; i < survivorCount; i++)
    {
        if (!table.TryGet(Key(i + 1), out int id) || id != survivorIds[i])
            throw new InvalidOperationException("Survivor identity changed.");
    }
    for (int i = 0; i < deletes; i++)
    {
        if (table.TryGet(Key(survivorCount + i + 1), out _))
            throw new InvalidOperationException("Deleted key is still present.");
    }
    return (table.Capacity, milliseconds, allocated, table.Count);
}

static PresenterVisualStableKey Key(int presenterId) => new(presenterId, 0, AssetKind.Mesh, 1);
