using System;
using System.IO;
using System.Text.Json;

namespace ScopeSwitchShowcaseMod.Runtime;

internal sealed class ScopeSwitchConfig
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string MapId { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Controls { get; set; } = string.Empty;
    public string ViewerEntity { get; set; } = string.Empty;
    public ScopeSwitchEntityConfig[] Entities { get; set; } = Array.Empty<ScopeSwitchEntityConfig>();
    public ScopeSwitchScopeConfig[] Scopes { get; set; } = Array.Empty<ScopeSwitchScopeConfig>();

    public static ScopeSwitchConfig Load(Stream stream)
    {
        ScopeSwitchConfig config = JsonSerializer.Deserialize<ScopeSwitchConfig>(stream, Options)
            ?? throw new InvalidOperationException("Scope switch config could not be deserialized.");
        config.Validate();
        return config;
    }

    private void Validate()
    {
        Require(MapId, nameof(MapId));
        Require(ViewerEntity, nameof(ViewerEntity));
        if (Entities.Length == 0)
        {
            throw new InvalidOperationException("Scope switch config requires entities.");
        }

        if (Scopes.Length == 0)
        {
            throw new InvalidOperationException("Scope switch config requires scopes.");
        }

        for (int i = 0; i < Entities.Length; i++)
        {
            Require(Entities[i].Id, $"Entities[{i}].Id");
            Require(Entities[i].Label, $"Entities[{i}].Label");
        }

        for (int i = 0; i < Scopes.Length; i++)
        {
            Require(Scopes[i].Id, $"Scopes[{i}].Id");
            Require(Scopes[i].Label, $"Scopes[{i}].Label");
            Require(Scopes[i].Kind, $"Scopes[{i}].Kind");
            Require(Scopes[i].ActionId, $"Scopes[{i}].ActionId");
            Require(Scopes[i].Host, $"Scopes[{i}].Host");
            if (Scopes[i].Members.Length == 0)
            {
                throw new InvalidOperationException($"Scopes[{i}].Members must not be empty.");
            }

            if (Scopes[i].Visible.Length == 0)
            {
                throw new InvalidOperationException($"Scopes[{i}].Visible must not be empty.");
            }
        }
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Scope switch config requires {name}.");
        }
    }
}

internal sealed class ScopeSwitchEntityConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

internal sealed class ScopeSwitchScopeConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string[] Members { get; set; } = Array.Empty<string>();
    public string[] Visible { get; set; } = Array.Empty<string>();
}
