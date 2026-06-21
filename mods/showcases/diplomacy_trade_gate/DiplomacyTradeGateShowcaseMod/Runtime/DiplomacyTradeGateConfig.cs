using System;
using System.IO;
using System.Text.Json;

namespace DiplomacyTradeGateShowcaseMod.Runtime;

public sealed class DiplomacyTradeGateConfig
{
    public string Header { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Controls { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string SourceLayout { get; set; } = string.Empty;
    public string TargetLayout { get; set; } = string.Empty;
    public string CreditItem { get; set; } = string.Empty;
    public string GoodsItem { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string RelationshipType { get; set; } = string.Empty;
    public string TrustMetric { get; set; } = string.Empty;
    public string EmbargoFlag { get; set; } = string.Empty;
    public int PactTrust { get; set; } = 60;
    public int StartingCredits { get; set; } = 30;
    public int TradeCost { get; set; } = 5;

    public static DiplomacyTradeGateConfig Load(Stream stream)
    {
        var config = JsonSerializer.Deserialize<DiplomacyTradeGateConfig>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Diplomacy trade gate config is empty.");

        Require(config.Header, nameof(Header));
        Require(config.SourceName, nameof(SourceName));
        Require(config.TargetName, nameof(TargetName));
        Require(config.SourceLayout, nameof(SourceLayout));
        Require(config.TargetLayout, nameof(TargetLayout));
        Require(config.CreditItem, nameof(CreditItem));
        Require(config.GoodsItem, nameof(GoodsItem));
        Require(config.Operation, nameof(Operation));
        Require(config.RelationshipType, nameof(RelationshipType));
        Require(config.TrustMetric, nameof(TrustMetric));
        Require(config.EmbargoFlag, nameof(EmbargoFlag));
        return config;
    }

    private static void Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Diplomacy trade gate config requires '{field}'.");
        }
    }
}
