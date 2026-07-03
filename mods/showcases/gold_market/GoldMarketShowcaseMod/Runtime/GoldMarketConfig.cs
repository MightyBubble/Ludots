using System;
using System.IO;
using System.Text.Json;

namespace GoldMarketShowcaseMod.Runtime;

public sealed class GoldMarketConfig
{
    public string Header { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Controls { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string MarketName { get; set; } = string.Empty;
    public string BuyerLayout { get; set; } = string.Empty;
    public string MarketLayout { get; set; } = string.Empty;
    public string GoldAttribute { get; set; } = string.Empty;
    public string BuyOperation { get; set; } = string.Empty;
    public string ExpensiveOperation { get; set; } = string.Empty;
    public string AtomicFailureOperation { get; set; } = string.Empty;
    public string RelicItem { get; set; } = string.Empty;
    public string BonusItem { get; set; } = string.Empty;
    public int StartingGold { get; set; }
    public int RefillGold { get; set; }

    public static GoldMarketConfig Load(Stream stream)
    {
        GoldMarketConfig? config = JsonSerializer.Deserialize<GoldMarketConfig>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (config == null)
        {
            throw new InvalidOperationException("Gold market config is empty.");
        }

        Require(config.Header, nameof(Header));
        Require(config.BuyerName, nameof(BuyerName));
        Require(config.MarketName, nameof(MarketName));
        Require(config.BuyerLayout, nameof(BuyerLayout));
        Require(config.MarketLayout, nameof(MarketLayout));
        Require(config.GoldAttribute, nameof(GoldAttribute));
        Require(config.BuyOperation, nameof(BuyOperation));
        Require(config.ExpensiveOperation, nameof(ExpensiveOperation));
        Require(config.AtomicFailureOperation, nameof(AtomicFailureOperation));
        Require(config.RelicItem, nameof(RelicItem));
        Require(config.BonusItem, nameof(BonusItem));
        if (config.StartingGold <= 0 || config.RefillGold <= 0)
        {
            throw new InvalidOperationException("Gold market config requires positive gold values.");
        }

        return config;
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Gold market config requires '{field}'.");
        }
    }
}
