using System;
using System.IO;
using System.Text.Json;

namespace OwnershipCascadeShowcaseMod.Runtime;

public sealed class OwnershipCascadeConfig
{
    public string Header { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Controls { get; set; } = string.Empty;
    public string PlayerRepName { get; set; } = string.Empty;
    public string EnemyPlayerName { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string GarrisonName { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string ProductionName { get; set; } = string.Empty;
    public string CityOwnerLabel { get; set; } = string.Empty;
    public string[] OwnedChildLabels { get; set; } = Array.Empty<string>();

    public static OwnershipCascadeConfig Load(Stream stream)
    {
        var config = JsonSerializer.Deserialize<OwnershipCascadeConfig>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Ownership cascade config is empty.");

        Require(config.Header, nameof(Header));
        Require(config.PlayerRepName, nameof(PlayerRepName));
        Require(config.EnemyPlayerName, nameof(EnemyPlayerName));
        Require(config.CityName, nameof(CityName));
        Require(config.GarrisonName, nameof(GarrisonName));
        Require(config.WarehouseName, nameof(WarehouseName));
        Require(config.ProductionName, nameof(ProductionName));
        return config;
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Ownership cascade config requires '{field}'.");
        }
    }
}
