using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Modding;

namespace Ludots.Tool;

/// <summary>
/// 纯数据群体地图 Mod 的起步文件：一张 1,600,000 cm 见方的图，陆地和水两种地形，两名玩家，一种 foot 单位。
/// 不写逻辑地形、高度图和 .navsurface，这三份由 nav init-crowd-terrain / init-crowd-heightmap / bake-crowd-navsurface 生成。
/// </summary>
public static class CrowdModScaffold
{
    public const int BoardSizeCm = 1_600_000;
    public const int NavCellSizeCm = 6250;
    public const int UnitRadiusCm = 150;
    public const int UnitsPerPlayer = 100;
    public const int BaseYCm = 800_000;
    public const int Player1BaseXCm = 600_000;
    public const int Player2BaseXCm = 1_000_000;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void Write(string modDir, string modId, string mapId)
    {
        if (string.IsNullOrWhiteSpace(modId)) throw new InvalidOperationException("--id 为空。");
        if (string.IsNullOrWhiteSpace(mapId)) throw new InvalidOperationException("--template crowd-data 需要 --mapId。");
        if (Directory.Exists(modDir)) throw new IOException($"目录已存在：{modDir}");

        string assets = Path.Combine(modDir, "assets");
        string unit = mapId + ".unit";
        string unitColor = unit + ".color";
        string camera = modId + ".Camera";

        var manifest = new ModManifest
        {
            Name = modId,
            Version = "0.1.0",
            Description = $"Crowd simulation map {mapId}.",
            Priority = 0,
            Dependencies = new Dictionary<string, string>
            {
                ["CrowdSimulationMod"] = "^1.0.0",
                ["CrowdSimulationStandardMod"] = "^1.0.0",
            },
            Tags = new List<string> { "crowd_simulation" },
        };
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "mod.json"), ModManifestJson.ToCanonicalJson(manifest));

        WriteJson(assets, "game.json", new JsonObject
        {
            ["startupMapId"] = mapId,
            ["windowTitle"] = "Ludots - " + modId,
            ["startupLocalSeats"] = new JsonArray(new JsonObject { ["seatId"] = "seat.0", ["playerId"] = 1 }),
        });
        WriteJson(assets, "Engine/clock.json", new JsonObject { ["FixedHz"] = 30 });
        WriteJson(assets, "Camera/virtual_cameras.json", new JsonArray(new JsonObject
        {
            ["id"] = camera,
            ["displayName"] = modId + " Orbit",
            ["priority"] = 0,
            ["facingMode"] = "none",
            ["distanceCm"] = 320000,
            ["pitch"] = 60,
            ["fovYDeg"] = 50,
            ["yaw"] = 0,
            ["targetHeightMode"] = "ContinuousHeightmap",
            ["targetHeightLayerIndex"] = 0,
            ["targetHeightOffsetCm"] = 0,
            ["minDistanceCm"] = 20000,
            ["maxDistanceCm"] = 2400000,
            ["minPitchDeg"] = 20,
            ["maxPitchDeg"] = 85,
            ["panMode"] = "Keyboard",
            ["edgePanMarginPx"] = 10,
            ["edgePanSpeedCmPerSec"] = 100000,
            ["panCmPerSecond"] = 100000,
            ["enableGrabDrag"] = true,
            ["rotateMode"] = "Both",
            ["rotateDegPerPixel"] = 0.28,
            ["rotateRequiresHold"] = true,
            ["rotateDegPerSecond"] = 90,
            ["enableZoom"] = true,
            ["zoomCmPerWheel"] = 40000,
            ["followMode"] = "None",
            ["allowUserInput"] = true,
        }));

        WriteJson(assets, "Navigation/navmesh.json", new JsonObject
        {
            ["maps"] = new JsonObject
            {
                [mapId] = new JsonObject
                {
                    ["solver"] = "crowd-simulation",
                    ["logicTerrainAsset"] = $"terrain/{mapId}.map_data.bin",
                    ["navCellSizeCm"] = NavCellSizeCm,
                    ["terrainTypes"] = new JsonArray("land", "water"),
                },
            },
        });
        WriteJson(assets, "Navigation/agent_profiles.json", new JsonArray(new JsonObject
        {
            ["id"] = mapId + "_unit",
            ["radiusCm"] = UnitRadiusCm,
            ["heightCm"] = 180,
            ["clearanceCm"] = 0,
            ["draftCm"] = 0,
            ["beamCm"] = 0,
            ["mass"] = 2,
            ["layer"] = 0,
            ["navProfileId"] = "foot",
        }));

        WriteJson(assets, $"Maps/{mapId}.json", new JsonObject
        {
            ["Id"] = mapId,
            ["Tags"] = new JsonArray("crowd_simulation"),
            ["Tuning"] = new JsonObject { ["LoadedChunkCapacity"] = 256 },
            ["Boards"] = new JsonArray(new JsonObject
            {
                ["Name"] = "default",
                ["SpatialType"] = "Grid",
                ["WidthCm"] = BoardSizeCm,
                ["HeightCm"] = BoardSizeCm,
                ["Anchor"] = new JsonObject { ["LocalXCm"] = 0, ["LocalYCm"] = 0, ["WorldXCm"] = 0, ["WorldYCm"] = 0 },
                ["Grid"] = new JsonObject { ["CellSizeCm"] = 100 },
            }),
            ["ContinuousHeightmap"] = new JsonObject
            {
                ["Asset"] = $"assets/terrain/{mapId}.height",
                ["RenderProfile"] = new JsonObject { ["DisableDistanceFog"] = true },
            },
            ["DefaultCamera"] = new JsonObject
            {
                ["VirtualCameraId"] = camera,
                ["TargetXCm"] = BoardSizeCm / 2,
                ["TargetYCm"] = BaseYCm,
                ["Yaw"] = 0,
                ["Pitch"] = 60,
                ["DistanceCm"] = 400000,
                ["FovYDeg"] = 50,
            },
            ["Teams"] = new JsonArray(Team(1), Team(2)),
            ["Players"] = new JsonArray(Player(1), Player(2)),
            ["Entities"] = new JsonArray(
                TeamEntity(mapId, 1, 2),
                TeamEntity(mapId, 2, 1),
                new JsonObject { ["InstanceId"] = "player_1", ["Template"] = mapId + ".player" },
                new JsonObject { ["InstanceId"] = "player_2", ["Template"] = mapId + ".player" }),
        });

        WriteJson(assets, "Entities/templates.json", new JsonArray(
            NamedTemplate(mapId + ".team"),
            NamedTemplate(mapId + ".player"),
            new JsonObject
            {
                ["id"] = unit,
                ["components"] = new JsonObject
                {
                    ["Name"] = new JsonObject { ["Value"] = unit },
                    ["CrowdSimulationAgent"] = new JsonObject
                    {
                        ["profileId"] = mapId + "_unit",
                        ["agentTypeId"] = "foot",
                        ["speedCmPerSecond"] = 1400,
                        ["personalRadiusCm"] = 900,
                        ["pushPriority"] = 2,
                        ["special"] = false,
                    },
                },
            }));

        WriteJson(assets, "Presentation/mesh_assets.json", new JsonArray(new JsonObject
        {
            ["id"] = unit + ".body",
            ["type"] = "Primitive",
            ["primitiveKind"] = "Cube",
        }));
        WriteJson(assets, "Presentation/presenters.json", new JsonArray(
            new JsonObject
            {
                ["id"] = mapId + ".units.bootstrap",
                ["rules"] = new JsonArray(
                    new JsonObject
                    {
                        ["event"] = new JsonObject { ["kind"] = "EntitySpawned", ["key"] = unit },
                        ["condition"] = new JsonObject { ["inline"] = "None" },
                        ["command"] = new JsonObject
                        {
                            ["kind"] = "CreatePresenter",
                            ["definitionId"] = unit,
                            ["scopeSource"] = "EventPayloadA",
                        },
                    },
                    new JsonObject
                    {
                        ["event"] = new JsonObject { ["kind"] = "EntityDestroyed", ["key"] = unit },
                        ["command"] = new JsonObject { ["kind"] = "DestroyPresenterScope", ["scopeSource"] = "EventPayloadA" },
                    }),
            },
            new JsonObject
            {
                ["id"] = unit,
                ["behaviors"] = new JsonArray(
                    new JsonObject
                    {
                        ["slot"] = "body",
                        ["kind"] = "AssetBinding",
                        ["activeByDefault"] = true,
                        ["assetBinding"] = new JsonObject
                        {
                            ["assetKind"] = "Mesh",
                            ["assetId"] = unit + ".body",
                            ["materialId"] = "default_surface",
                            ["renderPath"] = "InstancedStaticMesh",
                            ["mobility"] = "Movable",
                            ["localOffset"] = new JsonArray(0, 4, 0),
                            ["localScale"] = new JsonArray(12, 8, 12),
                            ["colorParamKey"] = unitColor,
                        },
                    },
                    new JsonObject
                    {
                        ["slot"] = "grounding",
                        ["kind"] = "Grounding",
                        ["activeByDefault"] = true,
                        ["grounding"] = new JsonObject
                        {
                            ["mode"] = "SnapToGround",
                            ["offset"] = 0,
                            ["updatePolicy"] = "EveryFrame",
                        },
                    }),
                ["bindings"] = new JsonArray(new JsonObject { ["paramKey"] = unitColor, ["source"] = "entityColorVector" }),
                ["paramDefaults"] = new JsonArray(new JsonObject
                {
                    ["paramKey"] = unitColor,
                    ["lane"] = "Vector",
                    ["vectorValue"] = new JsonArray(1, 1, 1, 1),
                }),
            }));

        WriteJson(assets, "CrowdSimulationConfig.json", new JsonObject
        {
            ["world"] = new JsonObject { ["seed"] = 1 },
            ["deploy"] = new JsonObject
            {
                ["initialUnits"] = 0,
                ["bases"] = new JsonArray(
                    new JsonObject { ["playerId"] = 1, ["xCm"] = Player1BaseXCm, ["yCm"] = BaseYCm, ["color"] = "#4DB2FF" },
                    new JsonObject { ["playerId"] = 2, ["xCm"] = Player2BaseXCm, ["yCm"] = BaseYCm, ["color"] = "#FFC53D" }),
            },
        });
        WriteJson(assets, "CrowdSimulationDebug.json", new JsonObject
        {
            ["session"] = new JsonObject
            {
                ["script"] = new JsonArray(
                    Spawn(1, Player1BaseXCm),
                    Spawn(2, Player2BaseXCm),
                    Order(1, Player2BaseXCm),
                    Order(2, Player1BaseXCm)),
            },
        });
        WriteJson(assets, "config_catalog.json", new JsonArray(new JsonObject
        {
            ["Path"] = "CrowdSimulationDebug.json",
            ["Policy"] = "DeepObject",
        }));
        WriteJson(assets, "Presentation/crowd_terrain_type_overlay.json", new JsonObject { ["enabled"] = true });
    }

    private static JsonObject Team(int id) => new() { ["TeamId"] = id, ["RepresentativeInstanceId"] = $"team_{id}" };

    private static JsonObject Player(int id) => new()
    {
        ["PlayerId"] = id,
        ["TeamId"] = id,
        ["RepresentativeInstanceId"] = $"player_{id}",
    };

    private static JsonObject TeamEntity(string mapId, int self, int other) => new()
    {
        ["InstanceId"] = $"team_{self}",
        ["Template"] = mapId + ".team",
        ["Relations"] = new JsonArray(
            new JsonObject { ["To"] = $"team_{self}", ["Type"] = "Friendly" },
            new JsonObject { ["To"] = $"team_{other}", ["Type"] = "Hostile" }),
    };

    private static JsonObject NamedTemplate(string id) => new()
    {
        ["id"] = id,
        ["components"] = new JsonObject { ["Name"] = new JsonObject { ["Value"] = id } },
    };

    private static JsonObject Spawn(int player, int xCm) => new()
    {
        ["tick"] = 0,
        ["cmd"] = new JsonObject
        {
            ["type"] = "spawnAt",
            ["player"] = player,
            ["xCm"] = xCm,
            ["yCm"] = BaseYCm,
            ["count"] = UnitsPerPlayer,
            ["unitType"] = 0,
            ["rIdx"] = 0,
        },
    };

    private static JsonObject Order(int player, int xCm) => new()
    {
        ["tick"] = 1,
        ["cmd"] = new JsonObject { ["type"] = "order", ["player"] = player, ["xCm"] = xCm, ["yCm"] = BaseYCm },
    };

    private static void WriteJson(string assets, string relative, JsonNode node)
    {
        string path = Path.Combine(assets, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(Indented) + Environment.NewLine);
    }
}
