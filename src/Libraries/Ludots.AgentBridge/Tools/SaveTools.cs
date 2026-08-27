using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>
    /// Save/load bridge tools. All state flows through the formal pipeline: slots via
    /// SaveSlotStore over the engine ISaveStorage service, capture/restore via
    /// WorldSnapshotService/WorldRestoreService at the clean tick boundary. World digests
    /// serialize through a canonicalized throwaway world (entity WorldIds normalized to 0)
    /// so hashes compare across engine processes.
    /// </summary>
    public sealed class SaveSlotsTool : IAgentTool
    {
        public string Name => "ludots.save.slots";
        public string Description => "List save slots with header metadata (kind, name, tick, mapId, createdUtc, schemaVersion, byte size). Optional 'kind' filter (manual|autosave).";
        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "Optional slot kind filter: manual | autosave." },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            SaveSlotStore store = SaveToolSupport.RequireSlotStore(context.Engine);
            string? kind = SaveToolSupport.OptionalString(args, "kind");
            var slots = new JsonArray();
            foreach (SaveSlotHeader header in store.ListSlots())
            {
                if (kind != null && !string.Equals(header.Id.Kind, kind, StringComparison.Ordinal))
                {
                    continue;
                }

                slots.Add(new JsonObject
                {
                    ["kind"] = header.Id.Kind,
                    ["name"] = header.Id.Name,
                    ["slot"] = header.Id.Value,
                    ["tick"] = header.Header.Tick,
                    ["mapId"] = header.Header.MapId,
                    ["createdUtc"] = header.Header.CreatedUtc.ToString("o"),
                    ["schemaVersion"] = header.Header.SchemaVersion,
                    ["bytes"] = SaveToolSupport.SlotByteSize(context.Engine, header.Id),
                });
            }

            return new JsonObject
            {
                ["slots"] = slots,
                ["count"] = slots.Count,
            };
        }
    }

    public sealed class SaveCaptureTool : IAgentTool
    {
        public string Name => "ludots.save.capture";
        public string Description => "Capture an in-memory world snapshot at the clean tick boundary without writing a slot. Returns tick and normalized world digest. No parameters.";
        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            var snapshotService = new WorldSnapshotService();
            WorldSaveSnapshot snapshot = SaveToolSupport.Capture(context.Engine, snapshotService);
            return new JsonObject
            {
                ["tick"] = snapshot.Header.Tick,
                ["mapId"] = snapshot.Header.MapId,
                ["worldBytes"] = snapshot.WorldBytes.Length,
                ["worldDigest"] = SaveToolSupport.ComputeWorldDigest(context.Engine, snapshot),
            };
        }
    }

    public sealed class SaveWriteTool : IAgentTool
    {
        public string Name => "ludots.save.write";
        public string Description => "Capture at the clean tick boundary and write a real disk slot (atomic temp+commit via engine ISaveStorage). 'name' defaults to 'agent-<utc timestamp>'; 'kind' defaults to manual.";
        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Slot name token [a-zA-Z0-9-_]; defaults to agent-<utc>." },
                ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "manual | autosave; defaults to manual." },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            SaveSlotStore store = SaveToolSupport.RequireSlotStore(context.Engine);
            string name = SaveToolSupport.OptionalString(args, "name") ?? $"agent-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            string kind = SaveToolSupport.OptionalString(args, "kind") ?? "manual";
            SaveSlotId id = SaveToolSupport.ResolveSlotId(name, kind);

            WorldSaveSnapshot snapshot = SaveToolSupport.Capture(context.Engine, new WorldSnapshotService());
            try
            {
                store.WriteSlot(id, snapshot);
            }
            catch (SaveContextException ex)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.ToolFailed, $"Write slot '{id.Value}' failed: {ex.Message}");
            }

            return new JsonObject
            {
                ["slot"] = id.Value,
                ["kind"] = id.Kind,
                ["name"] = id.Name,
                ["tick"] = snapshot.Header.Tick,
                ["bytes"] = SaveToolSupport.SlotByteSize(context.Engine, id),
                ["worldDigest"] = SaveToolSupport.ComputeWorldDigest(context.Engine, snapshot),
            };
        }
    }

    public sealed class SaveReadTool : IAgentTool
    {
        public string Name => "ludots.save.read";
        public string Description => "Read a slot from disk (section hashes verified) without restoring. Requires 'name'; optional 'kind' defaults to manual. Returns header metadata and normalized world digest.";
        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "manual | autosave; defaults to manual." },
            },
            ["required"] = new JsonArray { "name" },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            SaveSlotStore store = SaveToolSupport.RequireSlotStore(context.Engine);
            SaveSlotId id = SaveToolSupport.RequireSlotId(args);
            WorldSaveSnapshot snapshot = SaveToolSupport.ReadSlot(store, id);
            return new JsonObject
            {
                ["slot"] = id.Value,
                ["tick"] = snapshot.Header.Tick,
                ["mapId"] = snapshot.Header.MapId,
                ["createdUtc"] = snapshot.Header.CreatedUtc.ToString("o"),
                ["schemaVersion"] = snapshot.Header.SchemaVersion,
                ["bytes"] = SaveToolSupport.SlotByteSize(context.Engine, id),
                ["worldDigest"] = SaveToolSupport.ComputeWorldDigest(context.Engine, snapshot),
            };
        }
    }

    public sealed class SaveRestoreTool : IAgentTool
    {
        public string Name => "ludots.save.restore";
        public string Description => "Read a slot from disk and restore it into the running world (header gates verified). Requires 'name'; optional 'kind' defaults to manual. Returns restored tick and post-restore normalized world digest.";
        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["kind"] = new JsonObject { ["type"] = "string", ["description"] = "manual | autosave; defaults to manual." },
            },
            ["required"] = new JsonArray { "name" },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            SaveSlotStore store = SaveToolSupport.RequireSlotStore(context.Engine);
            SaveSlotId id = SaveToolSupport.RequireSlotId(args);
            WorldSaveSnapshot snapshot = SaveToolSupport.ReadSlot(store, id);
            try
            {
                new WorldRestoreService().Restore(context.Engine, snapshot);
            }
            catch (SaveContextException ex)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.ToolFailed, $"Restore slot '{id.Value}' failed: {ex.Message}");
            }
            return new JsonObject
            {
                ["slot"] = id.Value,
                ["restoredTick"] = context.Engine.GameSession.CurrentTick,
                ["worldDigest"] = SaveToolSupport.ComputeWorldDigest(context.Engine, new WorldSnapshotService()),
            };
        }
    }

    internal static class SaveToolSupport
    {
        public static SaveSlotStore RequireSlotStore(GameEngine engine)
        {
            if (!engine.TryGetService(CoreServiceKeys.SaveStorage, out ISaveStorage? storage) || storage == null)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    "Engine save storage service is not available; the host did not provide ISaveStorage.");
            }

            return new SaveSlotStore(storage);
        }

        public static WorldSaveSnapshot ReadSlot(SaveSlotStore store, SaveSlotId id)
        {
            try
            {
                return store.ReadSlot(id);
            }
            catch (SaveContextException ex)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.ToolFailed, $"Read slot '{id.Value}' failed: {ex.Message}");
            }
        }

        public static WorldSaveSnapshot Capture(GameEngine engine, WorldSnapshotService snapshots)
        {
            try
            {
                return snapshots.Capture(engine, SaveSnapshotBoundary.CleanAfter(SystemGroup.ClearPresentationFlags));
            }
            catch (SaveContextException ex)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.ToolFailed, $"Capture failed: {ex.Message}");
            }
        }

        public static SaveSlotId RequireSlotId(JsonObject? args)
        {
            string name = OptionalString(args, "name") ?? throw new AgentToolException(
                AgentBridgeErrorCodes.InvalidParams,
                "Parameter 'name' is required.");
            string kind = OptionalString(args, "kind") ?? "manual";
            return ResolveSlotId(name, kind);
        }

        public static SaveSlotId ResolveSlotId(string name, string kind)
        {
            if (!string.Equals(kind, "manual", StringComparison.Ordinal) &&
                !string.Equals(kind, "autosave", StringComparison.Ordinal))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Unknown slot kind '{kind}'. Expected manual | autosave.");
            }

            var id = new SaveSlotId(kind, name);
            try
            {
                _ = id.ToStorageKey();
                return id;
            }
            catch (SaveContextException ex)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, ex.Message);
            }
        }

        public static string? OptionalString(JsonObject? args, string key)
        {
            JsonNode? node = args?[key];
            return node is null ? null : node.GetValue<string>();
        }

        public static int SlotByteSize(GameEngine engine, SaveSlotId id)
        {
            if (!engine.TryGetService(CoreServiceKeys.SaveStorage, out ISaveStorage? storage) || storage == null)
            {
                return 0;
            }

            string key = id.ToStorageKey();
            return storage.Exists(key) ? storage.ReadAllBytes(key).Length : 0;
        }

        public static string ComputeWorldDigest(GameEngine engine, WorldSnapshotService snapshots)
        {
            return ComputeWorldDigest(engine, Capture(engine, snapshots));
        }

        public static string ComputeWorldDigest(GameEngine engine, WorldSaveSnapshot snapshot)
        {
            LudotsBinaryWorldSerializer serializer = LudotsPersistenceSerializerFactory.Create(engine);
            using World canonical = serializer.Deserialize(snapshot.WorldBytes);
            SaveEntityWorldIdNormalizer.Normalize(canonical, canonicalWorldId: 0);
            return HashWorld(canonical);
        }

        // World blob bytes are not stable across serialize invocations (chunk/type ordering
        // varies), so the digest hashes the sorted per-entity/per-component row dump instead —
        // the same lens SaveContinuationTrace uses for cross-instance determinism comparison.
        private static string HashWorld(World world)
        {
            MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithResolver(
                CompositeResolver.Create(
                    LudotsCorePersistenceFormatters.CreateFormatters(),
                    new IFormatterResolver[]
                    {
                        BuiltinResolver.Instance,
                        ContractlessStandardResolverAllowPrivate.Instance
                    }));

            var rows = new List<string>();
            world.Query(in QueryDescription.Null, entity =>
            {
                Signature signature = world.GetSignature(entity);
                var componentRows = new List<string>(signature.Components.Length);
                foreach (ComponentType componentType in signature.Components)
                {
                    Type type = componentType.Type;
                    object? component = world.Get(entity, componentType);
                    componentRows.Add(component == null
                        ? $"{type.FullName ?? type.Name}=<null>"
                        : $"{type.FullName ?? type.Name}={Convert.ToHexString(MessagePackSerializer.Serialize(type, component, options))}");
                }

                componentRows.Sort(StringComparer.Ordinal);
                rows.Add($"{entity.Id}:{entity.Version}|{string.Join("|", componentRows)}");
            });

            rows.Sort(StringComparer.Ordinal);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
        }
    }
}
