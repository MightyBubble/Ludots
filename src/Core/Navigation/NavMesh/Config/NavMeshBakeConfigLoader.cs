using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Scripting;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public sealed class NavMeshBakeConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly AgentProfileRegistry _agentProfiles;

        public NavMeshBakeConfigLoader(ConfigPipeline pipeline, AgentProfileRegistry agentProfiles)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
        }

        public NavMeshBakeConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            return ConfigMergeProvenance.Wrap(relativePath, report, tracked =>
            {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, tracked);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig not found in any source for '{relativePath}'.");
            }

            return Parse(mergedObject, _agentProfiles, relativePath);
            });
        }

        /// <summary>
        /// 校验并物化已经合并好的 navmesh.json。目录合并与文件合并走同一条校验。
        /// </summary>
        public static NavMeshBakeConfig Parse(
            JsonObject mergedObject,
            AgentProfileRegistry agentProfiles,
            string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            ArgumentNullException.ThrowIfNull(mergedObject);
            ArgumentNullException.ThrowIfNull(agentProfiles);
            ValidateRaw(mergedObject, relativePath);
            var options = StrictJsonOptions.CreateCamelCase();

            var config = mergedObject.Deserialize<NavMeshBakeConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException($"Failed to deserialize NavMeshBakeConfig from '{relativePath}'.");
            }

            _ = config.ParsedMode;
            _ = config.ParsedAlgorithm;

            ValidatePerMapTileGranularity(config);

            if (config.ParsedMode == NavBakeMode.RuntimeIncremental &&
                config.ParsedAlgorithm != NavBakeAlgorithmKind.Cdt &&
                config.ParsedAlgorithm != NavBakeAlgorithmKind.Recast)
            {
                throw new InvalidOperationException("NavMeshBakeConfig runtime-incremental mode must use algorithm 'cdt' or 'recast'.");
            }

            if (config.Profiles == null || config.Profiles.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.profiles is empty.");
            }

            if (config.Layers == null || config.Layers.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.layers is empty.");
            }

            if (config.Areas == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.areas is required.");
            }

            return config;
        }

        /// <summary>
        /// 只校验被 pathing agent type 或单位模板点名的体型。navProfileId 必须是能力档 id。
        /// </summary>
        public static void ValidateReferencedAgentNavProfiles(
            NavMeshBakeConfig config,
            AgentProfileRegistry agentProfiles,
            IReadOnlyCollection<string> agentProfileIds)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(agentProfiles);
            ArgumentNullException.ThrowIfNull(agentProfileIds);
            var navIds = new HashSet<string>(StringComparer.Ordinal);
            if (config.Profiles != null)
            {
                for (int i = 0; i < config.Profiles.Count; i++)
                {
                    NavMeshAgentProfileConfig profile = config.Profiles[i]
                        ?? throw new InvalidOperationException($"NavMeshBakeConfig.profiles[{i}] is null.");
                    if (!navIds.Add(profile.Id))
                    {
                        throw new InvalidOperationException($"NavMeshBakeConfig.profiles contains duplicate id '{profile.Id}'.");
                    }
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string agentProfileId in agentProfileIds)
            {
                if (string.IsNullOrWhiteSpace(agentProfileId))
                {
                    throw new InvalidOperationException("referenced agent profile id is empty.");
                }

                if (!seen.Add(agentProfileId))
                {
                    continue;
                }

                AgentProfileConfig agent = agentProfiles.Require(agentProfileId, "referenced agent profile");
                if (!navIds.Contains(agent.NavProfileId))
                {
                    throw new InvalidOperationException(
                        $"AgentProfile '{agent.Id}' navProfileId '{agent.NavProfileId}' is not a NavMeshBakeConfig.profiles id.");
                }
            }
        }

        public static NavMeshBakeConfig LoadFromRepoRoot(string repoRoot, string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            return LoadContextFromRepoRoot(repoRoot, targetModId: null, relativePath: relativePath).Config;
        }

        public static NavMeshBakeConfigContext LoadContextFromRepoRoot(
            string repoRoot,
            string? targetModId = null,
            string relativePath = NavMeshConfigPaths.BakeConfigPath)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                throw new ArgumentException("repoRoot is required.", nameof(repoRoot));
            }

            string assetsRoot = Path.Combine(Path.GetFullPath(repoRoot), "assets");
            if (!Directory.Exists(assetsRoot))
            {
                throw new DirectoryNotFoundException($"Repo root is missing assets/: {repoRoot}");
            }

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", assetsRoot);

            ModLoader? modLoader = null;
            if (!string.IsNullOrWhiteSpace(targetModId))
            {
                modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                modLoader.LoadResolvedPlan(ResolveModLoadPlan(repoRoot, targetModId));
            }

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var agentProfiles = new AgentProfileConfigLoader(pipeline).Load(catalog);
            var config = new NavMeshBakeConfigLoader(pipeline, agentProfiles).Load(catalog, relativePath: relativePath);
            return new NavMeshBakeConfigContext(config, agentProfiles);
        }

        /// <summary>
        /// 目标 mod 的依赖闭包，按 DependencyResolver 排序。无烘焙启动计划时，游戏把这份清单交给 LoadResolvedPlan。
        /// </summary>
        public static IReadOnlyList<ResolvedModLoadEntry> ResolveModLoadPlan(string repoRoot, string targetModId)
        {
            string modsRoot = Path.Combine(Path.GetFullPath(repoRoot), "mods");
            if (!Directory.Exists(modsRoot))
            {
                throw new DirectoryNotFoundException($"Repo root is missing mods/: {repoRoot}");
            }

            List<DiscoveredMod> discovered = ModDiscovery.DiscoverMods(new[] { modsRoot });
            var byId = new Dictionary<string, DiscoveredMod>(StringComparer.Ordinal);
            for (int i = 0; i < discovered.Count; i++)
            {
                DiscoveredMod mod = discovered[i];
                if (!byId.TryAdd(mod.Manifest.Name, mod))
                {
                    throw new InvalidOperationException($"Duplicate mod id '{mod.Manifest.Name}' in repo mods/.");
                }
            }

            if (!byId.ContainsKey(targetModId))
            {
                throw new InvalidOperationException($"Unknown mod '{targetModId}'.");
            }

            var required = new HashSet<string>(StringComparer.Ordinal);
            void AddRequired(string modId)
            {
                if (!required.Add(modId))
                {
                    return;
                }

                if (!byId.TryGetValue(modId, out DiscoveredMod mod))
                {
                    throw new InvalidOperationException($"Missing mod dependency '{modId}'.");
                }

                foreach (string dependencyId in mod.Manifest.Dependencies.Keys)
                {
                    AddRequired(dependencyId);
                }
            }

            AddRequired(targetModId);

            var nodes = new List<DependencyResolver.ModNode>(required.Count);
            for (int i = 0; i < discovered.Count; i++)
            {
                DiscoveredMod mod = discovered[i];
                if (!required.Contains(mod.Manifest.Name))
                {
                    continue;
                }

                nodes.Add(new DependencyResolver.ModNode
                {
                    Manifest = mod.Manifest,
                    CreationIndex = i
                });
            }

            List<ModManifest> sorted = new DependencyResolver().Resolve(nodes);
            var result = new List<ResolvedModLoadEntry>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                ModManifest manifest = sorted[i];
                result.Add(new ResolvedModLoadEntry(manifest.Name, byId[manifest.Name].DirectoryPath));
            }

            return result;
        }

        /// <summary>
        /// One map carries one nav tile grid (#1346): runtime tile addressing and
        /// artifact paths share a single (width, height) granularity per map, so
        /// boards declaring different sizes would be silently misaddressed by the
        /// max-size grid.
        /// </summary>
        internal static void ValidatePerMapTileGranularity(NavMeshBakeConfig config)
        {
            foreach ((string mapId, NavMapNavBoardsConfig? mapBoards) in config.Maps)
            {
                if (mapBoards?.Boards is not { Count: > 1 })
                {
                    continue;
                }

                string? firstName = null;
                int firstW = 0, firstH = 0;
                foreach ((string boardName, NavTileGridConfig? grid) in mapBoards.Boards)
                {
                    if (grid == null || grid.TileWorldWidthCm <= 0 || grid.TileWorldHeightCm <= 0)
                    {
                        continue;
                    }

                    if (firstName == null)
                    {
                        firstName = boardName;
                        firstW = grid.TileWorldWidthCm;
                        firstH = grid.TileWorldHeightCm;
                        continue;
                    }

                    if (grid.TileWorldWidthCm != firstW || grid.TileWorldHeightCm != firstH)
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapId}.boards declare mixed tile granularities: '{boardName}' {grid.TileWorldWidthCm}x{grid.TileWorldHeightCm}cm vs '{firstName}' {firstW}x{firstH}cm; one map carries one nav grid until per-board tile addressing lands (#1567 slice 2 follow-up).");
                    }
                }
            }
        }

        private static void ValidateRaw(JsonObject root, string relativePath)
        {
            RequireOnlyProperties(root, "NavMeshBakeConfig", new[] { "mode", "algorithm", "profiles", "layers", "areas", "runtimeIncremental" }, new[] { "terrainFeed", "maps" });
            string mode = RequireString(root, "mode", "NavMeshBakeConfig");
            string algorithm = RequireString(root, "algorithm", "NavMeshBakeConfig");
            _ = NavBakeNames.ParseMode(mode, "NavMeshBakeConfig.mode");
            _ = NavBakeNames.ParseAlgorithm(algorithm, "NavMeshBakeConfig.algorithm");
            if (root.TryGetPropertyValue("terrainFeed", out var terrainFeedNode))
            {
                if (terrainFeedNode is null)
                {
                    throw new InvalidOperationException("NavMeshBakeConfig.terrainFeed must be 'triangles' or 'direct', not null.");
                }

                _ = NavBakeNames.ParseTerrainFeed(RequireString(root, "terrainFeed", "NavMeshBakeConfig"), "NavMeshBakeConfig.terrainFeed");
            }

            if (root.TryGetPropertyValue("maps", out var mapsNode))
            {
                if (mapsNode is not JsonObject maps)
                {
                    throw new InvalidOperationException("NavMeshBakeConfig.maps must be an object keyed by map id.");
                }

                foreach (var mapEntry in maps)
                {
                    if (mapEntry.Value is not JsonObject mapObj)
                    {
                        throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapEntry.Key} must be an object.");
                    }

                    if (mapObj.ContainsKey("surfaceAsset"))
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapEntry.Key}.surfaceAsset 已删除。.navsurface 从地图 ContinuousHeightmap.asset 推导（去掉 assets/ 前缀，.height 换成 .navsurface）。");
                    }

                    if (mapObj.ContainsKey("agentTypes"))
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapEntry.Key}.agentTypes 已删除。本图要烘焙的能力档写在 maps.{mapEntry.Key}.navProfiles（能力档 id 数组）。");
                    }

                    RequireOnlyProperties(
                        mapObj,
                        $"NavMeshBakeConfig.maps.{mapEntry.Key}",
                        new[] { "solver" },
                        new[] { "boards", "terrainAreas", "navProfiles", "navAreas", "logicTerrainAsset", "jumpLinkBake" });
                    string solver = RequireString(mapObj, "solver", $"NavMeshBakeConfig.maps.{mapEntry.Key}");
                    if (!NavSolverDeclaration.IsKnown(solver))
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapEntry.Key}.solver 必须是 {NavSolverDeclaration.AllowedList}，当前是 \"{solver}\"。");
                    }
                    if (mapObj.ContainsKey("navProfiles"))
                    {
                        RequireNavProfiles(mapObj, mapEntry.Key);
                    }

                    if (mapObj.ContainsKey("terrainAreas"))
                    {
                        ValidateTerrainAreasArray(mapObj, mapEntry.Key);
                    }

                    if (mapObj.ContainsKey("jumpLinkBake"))
                    {
                        ValidateJumpLinkBake(mapObj, mapEntry.Key);
                    }

                    if (mapObj.ContainsKey("logicTerrainAsset"))
                    {
                        string asset = RequireString(
                            mapObj,
                            "logicTerrainAsset",
                            $"NavMeshBakeConfig.maps.{mapEntry.Key}");
                        RequireModRelativeAsset(asset, $"NavMeshBakeConfig.maps.{mapEntry.Key}.logicTerrainAsset");
                    }

                    bool bindsAgents = mapObj.ContainsKey("navProfiles") || mapObj.ContainsKey("terrainAreas");
                    if (mapObj.ContainsKey("navAreas") || bindsAgents)
                    {
                        RequireNavAreas(mapObj, mapEntry.Key, bindsAgents);
                    }

                    if (mapObj.ContainsKey("boards"))
                    {
                        if (mapObj["boards"] is not JsonObject boards)
                        {
                            throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapEntry.Key}.boards must be an object.");
                        }

                        foreach (var boardEntry in boards)
                        {
                            if (boardEntry.Value is not JsonObject boardObj)
                            {
                                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapEntry.Key}.boards.{boardEntry.Key} must be an object.");
                            }

                            if (boardObj.ContainsKey("surfaceAsset"))
                            {
                                throw new InvalidOperationException(
                                    $"NavMeshBakeConfig.maps.{mapEntry.Key}.boards.{boardEntry.Key}.surfaceAsset 已删除。.navsurface 从地图 ContinuousHeightmap.asset 推导（去掉 assets/ 前缀，.height 换成 .navsurface）。");
                            }

                            RequireOnlyProperties(
                                boardObj,
                                $"NavMeshBakeConfig.maps.{mapEntry.Key}.boards.{boardEntry.Key}",
                                new[] { "tileWorldWidthCm", "tileWorldHeightCm" },
                                Array.Empty<string>());
                            bool hasWidth = boardObj.ContainsKey("tileWorldWidthCm");
                            bool hasHeight = boardObj.ContainsKey("tileWorldHeightCm");
                            if (hasWidth != hasHeight)
                            {
                                throw new InvalidOperationException(
                                    $"NavMeshBakeConfig.maps.{mapEntry.Key}.boards.{boardEntry.Key} must declare tileWorldWidthCm and tileWorldHeightCm together.");
                            }
                        }

                        if (boards.Count != 1)
                        {
                            throw new InvalidOperationException(
                                $"NavMeshBakeConfig.maps.{mapEntry.Key}.boards 声明了 {boards.Count} 块板。一张图只能有一块求解器板；不写 boards 时用地图的 RootBoard。");
                        }
                    }
                }
            }

            if (root["profiles"] is JsonArray)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.profiles 以 id 为键的对象，不再使用数组。");
            }

            if (root["profiles"] is not JsonObject profiles || profiles.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.profiles 必须是非空的、以 id 为键的对象。");
            }

            foreach (var profileEntry in profiles)
            {
                if (string.IsNullOrWhiteSpace(profileEntry.Key))
                {
                    throw new InvalidOperationException("NavMeshBakeConfig.profiles 的键不能为空。");
                }

                if (profileEntry.Value is null)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profiles.{profileEntry.Key} 为 null。以 id 为键的对象不接受 null。");
                }

                if (profileEntry.Value is not JsonObject profile)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.profiles.{profileEntry.Key} must be an object.");
                }

                string path = $"NavMeshBakeConfig.profiles.{profileEntry.Key}";
                if (profile.ContainsKey("id"))
                {
                    throw new InvalidOperationException($"{path} 的 id 由键给出，对象内不要再写 id。");
                }

                RequireOnlyProperties(profile, path, new[] { "maxClimbCm", "agentProfileId" }, new[] { "maxSlopeDeg", "cellSizeCm", "jump" });
                if (profile.ContainsKey("cellSizeCm"))
                {
                    int cellSizeCm = RequireInt(profile, "cellSizeCm", path);
                    if (cellSizeCm <= 0)
                    {
                        throw new InvalidOperationException($"{path}.cellSizeCm must be > 0 when declared.");
                    }
                }

                RequireNumber(profile, "maxClimbCm", path);
                if (profile.ContainsKey("maxSlopeDeg"))
                {
                    float slope = RequireFloat(profile, "maxSlopeDeg", path);
                    if (slope <= 0f || float.IsNaN(slope))
                    {
                        throw new InvalidOperationException($"{path}.maxSlopeDeg 省略表示不限坡度，0 或负数无效。");
                    }
                }

                RequireString(profile, "agentProfileId", path);

                if (profile.ContainsKey("jump"))
                {
                    ValidateJump(profile, path);
                }
            }

            if (root["layers"] is not JsonArray layers || layers.Count == 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.layers must be a non-empty explicit array.");
            }

            var seenLayers = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] is not JsonObject layer)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layers[{i}] must be an object.");
                }

                string path = $"NavMeshBakeConfig.layers[{i}]";
                RequireOnlyProperties(layer, path, "id", "layer");
                string id = RequireString(layer, "id", path);
                if (!seenLayers.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.layers contains duplicate id '{id}'.");
                }

                RequireNumber(layer, "layer", path);
            }

            if (root["areas"] is JsonArray)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.areas 以 id 为键的对象，不再使用数组。");
            }

            if (root["areas"] is not JsonObject areas)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.areas 必须是以 id 为键的对象。");
            }

            var seenAreaIds = new HashSet<string>(StringComparer.Ordinal);
            var seenAreaNumbers = new HashSet<int>();
            foreach (var areaEntry in areas)
            {
                if (string.IsNullOrWhiteSpace(areaEntry.Key))
                {
                    throw new InvalidOperationException("NavMeshBakeConfig.areas 的键不能为空。");
                }

                if (areaEntry.Value is null)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas.{areaEntry.Key} 为 null。以 id 为键的对象不接受 null。");
                }

                if (areaEntry.Value is not JsonObject area)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas.{areaEntry.Key} must be an object.");
                }

                string path = $"NavMeshBakeConfig.areas.{areaEntry.Key}";
                if (area.ContainsKey("id"))
                {
                    throw new InvalidOperationException($"{path} 的 id 由键给出，对象内不要再写 id。");
                }

                RequireOnlyProperties(area, path, "areaId", "slopeFree");
                if (!seenAreaIds.Add(areaEntry.Key))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas contains duplicate id '{areaEntry.Key}'.");
                }

                int authoredAreaId = RequireInt(area, "areaId", path);
                if (authoredAreaId < 0 || authoredAreaId > 255)
                {
                    throw new InvalidOperationException($"{path}.areaId must be 0..255.");
                }

                if (!seenAreaNumbers.Add(authoredAreaId))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.areas contains duplicate areaId {authoredAreaId}.");
                }

                if (authoredAreaId >= RecastNavTileBaker.ReservedWalkableAreaId)
                {
                    throw new InvalidOperationException(
                        $"{path}.areaId {authoredAreaId} is reserved: ids >= {RecastNavTileBaker.ReservedWalkableAreaId} collide with the Recast walkable marker on the direct terrain feed.");
                }

                RequireBool(area, "slopeFree", path);
            }

            ValidateNavProfileReferences(root);
            ValidateNavAreaReferences(root, seenAreaIds);
            ValidateTerrainAreaReferences(root, seenAreaIds);
            ValidateTerrainAreasCoveredByNavAreas(root);

            if (root["runtimeIncremental"] is not JsonObject runtimeIncremental)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental must be an explicit object.");
            }

            RequireOnlyProperties(
                runtimeIncremental,
                "NavMeshBakeConfig.runtimeIncremental",
                "tileBudgetPerFixedTick",
                "includeNeighborTiles",
                "heightScaleMeters",
                "minWalkableUpDot",
                "cliffHeightThreshold");
            int tileBudget = RequireInt(runtimeIncremental, "tileBudgetPerFixedTick", "NavMeshBakeConfig.runtimeIncremental");
            if (tileBudget <= 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.tileBudgetPerFixedTick must be > 0.");
            }

            RequireBool(runtimeIncremental, "includeNeighborTiles", "NavMeshBakeConfig.runtimeIncremental");
            float heightScale = RequireFloat(runtimeIncremental, "heightScaleMeters", "NavMeshBakeConfig.runtimeIncremental");
            if (heightScale <= 0f || float.IsNaN(heightScale))
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.heightScaleMeters must be > 0.");
            }

            float minUpDot = RequireFloat(runtimeIncremental, "minWalkableUpDot", "NavMeshBakeConfig.runtimeIncremental");
            if (minUpDot < -1f || minUpDot > 1f || float.IsNaN(minUpDot))
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.minWalkableUpDot must be between -1 and 1.");
            }

            int cliff = RequireInt(runtimeIncremental, "cliffHeightThreshold", "NavMeshBakeConfig.runtimeIncremental");
            if (cliff < 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental.cliffHeightThreshold must be >= 0.");
            }
        }

        private static void ValidateJump(JsonObject profile, string path)
        {
            if (profile["jump"] is not JsonObject jump)
            {
                throw new InvalidOperationException($"{path}.jump must be an object.");
            }

            RequireOnlyProperties(jump, $"{path}.jump", "upCm", "downCm", "rangeNavCells", "cost");
            int upCm = RequireInt(jump, "upCm", $"{path}.jump");
            int downCm = RequireInt(jump, "downCm", $"{path}.jump");
            int range = RequireInt(jump, "rangeNavCells", $"{path}.jump");
            if (upCm <= 0 || downCm <= 0 || range <= 0)
            {
                throw new InvalidOperationException($"{path}.jump upCm, downCm and rangeNavCells must be > 0.");
            }

            float cost = RequireFloat(jump, "cost", $"{path}.jump");
            if (cost <= 0f || float.IsNaN(cost))
            {
                throw new InvalidOperationException($"{path}.jump.cost must be > 0.");
            }
        }

        private static void RequireNavProfiles(JsonObject mapObj, string mapId)
        {
            if (mapObj["navProfiles"] is JsonObject)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.maps.{mapId}.navProfiles 是能力档 id 的数组，不再使用对象。");
            }

            if (mapObj["navProfiles"] is not JsonArray navProfiles || navProfiles.Count == 0)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navProfiles 必须是非空的能力档 id 数组。");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < navProfiles.Count; i++)
            {
                if (navProfiles[i] is not JsonValue value ||
                    !value.TryGetValue<string>(out string? id) ||
                    string.IsNullOrWhiteSpace(id) ||
                    !string.Equals(id.Trim(), id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navProfiles[{i}] must be a canonical non-empty string.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navProfiles contains duplicate id '{id}'.");
                }
            }
        }

        private static void ValidateNavProfileReferences(JsonObject root)
        {
            if (root["maps"] is not JsonObject maps || root["profiles"] is not JsonObject profiles)
            {
                return;
            }

            foreach (var mapEntry in maps)
            {
                if (mapEntry.Value is not JsonObject mapObj || mapObj["navProfiles"] is not JsonArray navProfiles)
                {
                    continue;
                }

                for (int i = 0; i < navProfiles.Count; i++)
                {
                    string id = string.Empty;
                    if (navProfiles[i] is not JsonValue value ||
                        !value.TryGetValue<string>(out id) ||
                        string.IsNullOrWhiteSpace(id) ||
                        !profiles.ContainsKey(id))
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapEntry.Key}.navProfiles '{id}' 不是 NavMeshBakeConfig.profiles 的键。");
                    }
                }
            }
        }

        private static void RequireNavAreas(JsonObject mapObj, string mapId, bool required)
        {
            if (mapObj["navAreas"] is null)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navAreas 必须是非空的区域 id 数组。");
            }

            if (!mapObj.ContainsKey("navAreas"))
            {
                if (required)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navAreas 必须是非空的区域 id 数组。");
                }

                return;
            }

            if (mapObj["navAreas"] is JsonObject)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navAreas 是区域 id 的数组，不再使用对象。");
            }

            if (mapObj["navAreas"] is not JsonArray navAreas || navAreas.Count == 0)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navAreas 必须是非空的区域 id 数组。");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < navAreas.Count; i++)
            {
                if (navAreas[i] is not JsonValue value ||
                    !value.TryGetValue<string>(out string? id) ||
                    string.IsNullOrWhiteSpace(id) ||
                    !string.Equals(id.Trim(), id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navAreas[{i}] must be a canonical non-empty string.");
                }

                if (!seen.Add(id))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.navAreas contains duplicate id '{id}'.");
                }
            }
        }

        private static void ValidateNavAreaReferences(JsonObject root, HashSet<string> areaIds)
        {
            if (root["maps"] is not JsonObject maps)
            {
                return;
            }

            foreach (var mapEntry in maps)
            {
                if (mapEntry.Value is not JsonObject mapObj || mapObj["navAreas"] is not JsonArray navAreas)
                {
                    continue;
                }

                for (int i = 0; i < navAreas.Count; i++)
                {
                    string id = string.Empty;
                    if (navAreas[i] is JsonValue value && value.TryGetValue<string>(out string parsed) && parsed != null)
                    {
                        id = parsed;
                    }

                    if (id.Length == 0 || !areaIds.Contains(id))
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapEntry.Key}.navAreas '{id}' 不是 NavMeshBakeConfig.areas 的键。");
                    }
                }
            }
        }

        private static void ValidateTerrainAreasCoveredByNavAreas(JsonObject root)
        {
            if (root["maps"] is not JsonObject maps)
            {
                return;
            }

            foreach (var mapEntry in maps)
            {
                if (mapEntry.Value is not JsonObject mapObj || mapObj["terrainAreas"] is not JsonArray terrainAreas)
                {
                    continue;
                }

                var declared = new HashSet<string>(StringComparer.Ordinal);
                if (mapObj["navAreas"] is JsonArray navAreas)
                {
                    for (int i = 0; i < navAreas.Count; i++)
                    {
                        if (navAreas[i] is JsonValue value && value.TryGetValue<string>(out string id) && id != null)
                        {
                            declared.Add(id);
                        }
                    }
                }

                for (int i = 0; i < terrainAreas.Count; i++)
                {
                    string areaId = string.Empty;
                    if (terrainAreas[i] is JsonObject row &&
                        row["areaId"] is JsonValue value &&
                        value.TryGetValue<string>(out string parsed) &&
                        parsed != null)
                    {
                        areaId = parsed;
                    }

                    if (!declared.Contains(areaId))
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapEntry.Key}.terrainAreas[{i}] 的 areaId \"{areaId}\" 不在 maps.{mapEntry.Key}.navAreas 里。");
                    }
                }
            }
        }

        private static void ValidateTerrainAreasArray(JsonObject mapObj, string mapId)
        {
            if (mapObj["terrainAreas"] is JsonObject)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.maps.{mapId}.terrainAreas 是有序数组 [{{ terrainId, areaId }}]，不再使用对象。数组下标就是 .navsurface 地形编号。");
            }

            if (mapObj["terrainAreas"] is not JsonArray terrainAreas)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.terrainAreas must be an array.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < terrainAreas.Count; i++)
            {
                if (terrainAreas[i] is not JsonObject row)
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.terrainAreas[{i}] must be an object.");
                }

                string path = $"NavMeshBakeConfig.maps.{mapId}.terrainAreas[{i}]";
                RequireOnlyProperties(row, path, "terrainId", "areaId");
                string terrainId = RequireString(row, "terrainId", path);
                RequireString(row, "areaId", path);
                if (!seen.Add(terrainId))
                {
                    throw new InvalidOperationException($"NavMeshBakeConfig.maps.{mapId}.terrainAreas contains duplicate terrainId '{terrainId}'.");
                }
            }
        }

        private static void ValidateTerrainAreaReferences(JsonObject root, HashSet<string> areaIds)
        {
            if (root["maps"] is not JsonObject maps)
            {
                return;
            }

            foreach (var mapEntry in maps)
            {
                if (mapEntry.Value is not JsonObject mapObj || mapObj["terrainAreas"] is not JsonArray terrainAreas)
                {
                    continue;
                }

                for (int i = 0; i < terrainAreas.Count; i++)
                {
                    string areaId = string.Empty;
                    string terrainId = string.Empty;
                    if (terrainAreas[i] is JsonObject row)
                    {
                        if (row["terrainId"] is JsonValue terrainValue &&
                            terrainValue.TryGetValue<string>(out string? parsedTerrain) &&
                            parsedTerrain != null)
                        {
                            terrainId = parsedTerrain;
                        }

                        if (row["areaId"] is JsonValue value &&
                            value.TryGetValue<string>(out string? parsed) &&
                            parsed != null)
                        {
                            areaId = parsed;
                        }
                    }

                    if (areaId.Length == 0 || !areaIds.Contains(areaId))
                    {
                        throw new InvalidOperationException(
                            $"NavMeshBakeConfig.maps.{mapEntry.Key}.terrainAreas[{i}]（{terrainId}）references unknown area '{areaId}'.");
                    }
                }
            }
        }

        private static void ValidateJumpLinkBake(JsonObject mapObj, string mapId)
        {
            string path = $"NavMeshBakeConfig.maps.{mapId}.jumpLinkBake";
            if (mapObj["jumpLinkBake"] is not JsonObject bake)
            {
                throw new InvalidOperationException($"{path} 必须是对象。");
            }

            RequireOnlyProperties(
                bake,
                path,
                "maxGapCm",
                "linkSpacingCm",
                "endpointMaxSlopeDeg",
                "endpointTerrain",
                "gapTerrain");
            int maxGapCm = RequireInt(bake, "maxGapCm", path);
            int linkSpacingCm = RequireInt(bake, "linkSpacingCm", path);
            float endpointMaxSlopeDeg = RequireFloat(bake, "endpointMaxSlopeDeg", path);
            if (maxGapCm <= 0)
            {
                throw new InvalidOperationException($"{path}.maxGapCm = {maxGapCm}，需为 > 0。");
            }

            if (linkSpacingCm <= 0)
            {
                throw new InvalidOperationException($"{path}.linkSpacingCm = {linkSpacingCm}，需为 > 0。");
            }

            if (!(endpointMaxSlopeDeg > 0f) || float.IsInfinity(endpointMaxSlopeDeg))
            {
                throw new InvalidOperationException($"{path}.endpointMaxSlopeDeg = {endpointMaxSlopeDeg}，需为 > 0。");
            }

            if (mapObj["terrainAreas"] is not JsonArray terrainAreas)
            {
                throw new InvalidOperationException($"{path} 需要 maps.{mapId}.terrainAreas 来核对地形 id。");
            }

            var terrainIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < terrainAreas.Count; i++)
            {
                if (terrainAreas[i] is JsonObject row &&
                    row["terrainId"] is JsonValue value &&
                    value.TryGetValue<string>(out string? terrainId) &&
                    terrainId != null)
                {
                    terrainIds.Add(terrainId);
                }
            }

            RequireTerrainIdList(bake, "endpointTerrain", path, terrainIds, mapId);
            RequireTerrainIdList(bake, "gapTerrain", path, terrainIds, mapId);
        }

        private static void RequireTerrainIdList(
            JsonObject bake,
            string field,
            string path,
            HashSet<string> terrainIds,
            string mapId)
        {
            if (bake[field] is not JsonArray list)
            {
                throw new InvalidOperationException($"{path}.{field} 必须是地形 id 数组。");
            }

            if (list.Count == 0)
            {
                throw new InvalidOperationException($"{path}.{field} 不能是空数组。");
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not JsonValue value ||
                    !value.TryGetValue<string>(out string? terrainId) ||
                    string.IsNullOrWhiteSpace(terrainId) ||
                    !string.Equals(terrainId.Trim(), terrainId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.{field}[{i}] 必须是非空地形 id。");
                }

                if (!terrainIds.Contains(terrainId))
                {
                    throw new InvalidOperationException(
                        $"{path}.{field} 的地形 id \"{terrainId}\" 不在 maps.{mapId}.terrainAreas 里。");
                }
            }
        }

        internal static void RequireModRelativeAsset(string asset, string path)
        {
            string trimmed = asset.Replace('\\', '/');
            if (Path.IsPathRooted(asset) ||
                trimmed.StartsWith("/", StringComparison.Ordinal) ||
                trimmed.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} 必须是 Mod assets 下的相对路径，不能是绝对路径：{asset}");
            }

            string[] segments = trimmed.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "..")
                {
                    throw new InvalidOperationException($"{path} 不能包含 ..：{asset}");
                }
            }
        }

        private static void RequireOnlyProperties(JsonObject obj, string path, params string[] allowed)
            => RequireOnlyProperties(obj, path, allowed, Array.Empty<string>());

        /// <summary>Unknown-key rejection is absolute; `optional` keys are validated when
        /// present but carry no presence requirement, so new vocabulary lands without
        /// forcing every existing navmesh.json to repeat it.</summary>
        private static void RequireOnlyProperties(JsonObject obj, string path, string[] allowed, string[] optional)
        {
            foreach (var property in obj)
            {
                bool known = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(property.Key, allowed[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                for (int i = 0; !known && i < optional.Length; i++)
                {
                    if (string.Equals(property.Key, optional[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new InvalidOperationException($"{path} contains unknown property '{property.Key}'.");
                }
            }

            for (int i = 0; i < allowed.Length; i++)
            {
                if (!obj.ContainsKey(allowed[i]))
                {
                    throw new InvalidOperationException($"{path} must explicitly define '{allowed[i]}'.");
                }
            }
        }

        private static string RequireString(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{path}.{key} must be a non-empty string.");
            }

            if (!string.Equals(text.Trim(), text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.{key} must not contain leading or trailing whitespace.");
            }

            return text;
        }

        private static void RequireNumber(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value ||
                (!value.TryGetValue<int>(out _) &&
                 !value.TryGetValue<float>(out _) &&
                 !value.TryGetValue<double>(out _)))
            {
                throw new InvalidOperationException($"{path}.{key} must be a number.");
            }
        }

        private static int RequireInt(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<int>(out int number))
            {
                throw new InvalidOperationException($"{path}.{key} must be an integer.");
            }

            return number;
        }

        private static float RequireFloat(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value)
            {
                throw new InvalidOperationException($"{path}.{key} must be a number.");
            }

            if (value.TryGetValue<int>(out int numberInt))
            {
                return numberInt;
            }

            if (value.TryGetValue<long>(out long numberLong))
            {
                return numberLong;
            }

            if (value.TryGetValue<float>(out float number))
            {
                return number;
            }

            if (value.TryGetValue<double>(out double numberDouble))
            {
                return (float)numberDouble;
            }

            throw new InvalidOperationException($"{path}.{key} must be a number.");
        }

        private static bool RequireBool(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
            {
                throw new InvalidOperationException($"{path}.{key} must be a boolean.");
            }

            return flag;
        }
    }

    public sealed record NavMeshBakeConfigContext(
        NavMeshBakeConfig Config,
        AgentProfileRegistry AgentProfiles);
}
