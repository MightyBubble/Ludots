using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Modding;

namespace MassNavigationMod.Runtime;

public sealed class MassNavigationStaticObstacleWorldAsset
{
    public const string SchemaVersionValue = "mass-navigation.static-obstacle-world.v1";
    public const string DistributionStrategyValue = "deterministic_macro_chunk_hash_permutation";
    public const int DefaultDeterministicSeed = 40_000_256;

    public string SchemaVersion { get; set; } = SchemaVersionValue;
    public string MapId { get; set; } = string.Empty;
    public int MacroChunkColumns { get; set; }
    public int MacroChunkRows { get; set; }
    public int TargetStaticObstacleCount { get; set; }
    public int DeterministicSeed { get; set; } = DefaultDeterministicSeed;
    public string DistributionStrategy { get; set; } = DistributionStrategyValue;
    public int ObstaclesPerCoveredChunk { get; set; } = 1;
    public int RadiusCm { get; set; } = 380;
    public MassNavigationStaticObstacleRuntimeActivationConfig RuntimeActivation { get; set; } = new();

    public int MacroChunkCount => checked(MacroChunkColumns * MacroChunkRows);
    public int PlannedWorldObstacleCount => Math.Max(0, TargetStaticObstacleCount);
    public int MacroChunkCoverageCount => ObstaclesPerCoveredChunk <= 0
        ? 0
        : Math.Min(MacroChunkCount, (PlannedWorldObstacleCount + ObstaclesPerCoveredChunk - 1) / ObstaclesPerCoveredChunk);

    public void Validate(string expectedMapId, int expectedMacroColumns, int expectedMacroRows, int expectedTargetStaticObstacleCount)
    {
        if (!string.Equals(SchemaVersion, SchemaVersionValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle world asset schemaVersion must be '{SchemaVersionValue}', actual='{SchemaVersion}'.");
        }

        if (!string.Equals(MapId, expectedMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle world asset mapId must be '{expectedMapId}', actual='{MapId}'.");
        }

        if (MacroChunkColumns != expectedMacroColumns || MacroChunkRows != expectedMacroRows)
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle world asset macro grid must be {expectedMacroColumns}x{expectedMacroRows}, actual={MacroChunkColumns}x{MacroChunkRows}.");
        }

        if (TargetStaticObstacleCount < expectedTargetStaticObstacleCount)
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle world asset targetStaticObstacleCount must be >= configured target {expectedTargetStaticObstacleCount}, actual={TargetStaticObstacleCount}.");
        }

        if (!string.Equals(DistributionStrategy, DistributionStrategyValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle world asset distributionStrategy must be '{DistributionStrategyValue}', actual='{DistributionStrategy}'.");
        }

        if (ObstaclesPerCoveredChunk <= 0)
        {
            throw new InvalidOperationException("Mass-navigation static obstacle world asset requires ObstaclesPerCoveredChunk > 0.");
        }

        if (RadiusCm <= 0)
        {
            throw new InvalidOperationException("Mass-navigation static obstacle world asset requires RadiusCm > 0.");
        }

        if (!IsPowerOfTwo(MacroChunkCount))
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle world asset currently requires a power-of-two macro chunk count for deterministic permutation coverage, actual={MacroChunkCount}.");
        }

        if (MacroChunkCoverageCount <= 0 || MacroChunkCoverageCount > MacroChunkCount)
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle world asset coverage count is invalid: coverage={MacroChunkCoverageCount}, macroChunks={MacroChunkCount}.");
        }

        RuntimeActivation.Validate();
    }

    public bool IsChunkCovered(int chunkX, int chunkY)
    {
        if ((uint)chunkX >= (uint)MacroChunkColumns || (uint)chunkY >= (uint)MacroChunkRows)
        {
            return false;
        }

        int linear = (chunkY * MacroChunkColumns) + chunkX;
        return ComputePermutationRank(linear) < MacroChunkCoverageCount;
    }

    public int CountCoveredChunksInWindow(int minChunkX, int minChunkY, int maxChunkX, int maxChunkY)
    {
        if (MacroChunkColumns <= 0 || MacroChunkRows <= 0 || maxChunkX < minChunkX || maxChunkY < minChunkY)
        {
            return 0;
        }

        int minX = Math.Clamp(minChunkX, 0, MacroChunkColumns - 1);
        int maxX = Math.Clamp(maxChunkX, 0, MacroChunkColumns - 1);
        int minY = Math.Clamp(minChunkY, 0, MacroChunkRows - 1);
        int maxY = Math.Clamp(maxChunkY, 0, MacroChunkRows - 1);
        int count = 0;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (IsChunkCovered(x, y))
                {
                    count++;
                }
            }
        }

        return count * ObstaclesPerCoveredChunk;
    }

    public string BuildSampleChunkBuckets()
    {
        int coverage = MacroChunkCoverageCount;
        if (coverage <= 0)
        {
            return string.Empty;
        }

        int[] ordinals = new[]
        {
            0,
            Math.Max(0, coverage / 3),
            Math.Max(0, (coverage * 2) / 3),
            Math.Max(0, coverage - 1),
        };
        var samples = new List<string>(ordinals.Length);
        int nextOrdinalIndex = 0;
        int coveredOrdinal = 0;

        for (int y = 0; y < MacroChunkRows && nextOrdinalIndex < ordinals.Length; y++)
        {
            for (int x = 0; x < MacroChunkColumns && nextOrdinalIndex < ordinals.Length; x++)
            {
                if (!IsChunkCovered(x, y))
                {
                    continue;
                }

                while (nextOrdinalIndex < ordinals.Length && ordinals[nextOrdinalIndex] == coveredOrdinal)
                {
                    samples.Add($"{x},{y}");
                    nextOrdinalIndex++;
                }

                coveredOrdinal++;
            }
        }

        return string.Join(";", samples);
    }

    private int ComputePermutationRank(int linearChunkIndex)
    {
        int mask = MacroChunkCount - 1;
        long mixed = ((long)linearChunkIndex * 40_503L) + DeterministicSeed;
        return (int)(mixed & mask);
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }
}

public sealed class MassNavigationStaticObstacleRuntimeActivationConfig
{
    public string Strategy { get; set; } = "active_window_subset_to_mass_flow_solver";
    public int MaxSolverObstacles { get; set; } = MassFlowSimulationState.MaxObstacleCount;

    public void Validate()
    {
        if (!string.Equals(Strategy, "active_window_subset_to_mass_flow_solver", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle runtime activation strategy is unsupported: '{Strategy}'.");
        }

        if (MaxSolverObstacles <= 0 || MaxSolverObstacles > MassFlowSimulationState.MaxObstacleCount)
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle runtime activation MaxSolverObstacles must be in 1..{MassFlowSimulationState.MaxObstacleCount}, actual={MaxSolverObstacles}.");
        }
    }
}

public static class MassNavigationStaticObstacleWorldAssetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string GetRelativePath(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new ArgumentException("mapId is required.", nameof(mapId));
        }

        return $"assets/Data/Nav/{mapId}/static-obstacles.world.json";
    }

    public static MassNavigationStaticObstacleWorldAsset? TryLoad(
        IVirtualFileSystem vfs,
        IEnumerable<string>? loadedModIds,
        string mapId)
    {
        if (vfs == null)
        {
            throw new ArgumentNullException(nameof(vfs));
        }

        string relativePath = GetRelativePath(mapId);
        string? uri = ResolveSingleExistingUri(vfs, loadedModIds, relativePath);
        if (uri == null)
        {
            return null;
        }

        using Stream stream = vfs.GetStream(uri);
        MassNavigationStaticObstacleWorldAsset? asset = JsonSerializer.Deserialize<MassNavigationStaticObstacleWorldAsset>(stream, JsonOptions);
        if (asset == null)
        {
            throw new InvalidOperationException($"Mass-navigation static obstacle world asset '{uri}' is empty or invalid.");
        }

        return asset;
    }

    private static string? ResolveSingleExistingUri(
        IVirtualFileSystem vfs,
        IEnumerable<string>? loadedModIds,
        string relativePath)
    {
        var matches = new List<string>(4);
        AddIfExists(vfs, matches, $"Core:{relativePath}");
        if (loadedModIds != null)
        {
            foreach (string modId in loadedModIds)
            {
                AddIfExists(vfs, matches, $"{modId}:{relativePath}");
            }
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Mass-navigation static obstacle world asset '{relativePath}' resolves to multiple mounted assets ({string.Join(", ", matches)}).");
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private static void AddIfExists(IVirtualFileSystem vfs, List<string> matches, string uri)
    {
        if (vfs.TryResolveFullPath(uri, out string fullPath) && File.Exists(fullPath))
        {
            matches.Add(uri);
        }
    }
}
