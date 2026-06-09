using System;
using System.IO;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class MassNavigationShowcaseAcceptanceDocumentTests
    {
        [Test]
        public void AcceptanceDocument_RemainsStableGoalContractWithoutRunEvidenceDrift()
        {
            string doc = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "gitbook",
                "reference",
                "mass-navigation-showcase-acceptance.md"));

            Assert.That(doc, Does.Contain("# Mass Navigation Showcase 验收指南"));
            Assert.That(doc, Does.Contain("本文面向 0 上下文的技术同事和 Mod 开发者"));
            Assert.That(doc, Does.Contain("如果我要把 Ludots 的大世界导航能力接进自己的 Mod"));
            Assert.That(doc, Does.Contain("它定义的是可玩、可观察、可复现的验收形态"));
            Assert.That(doc, Does.Contain("Mod 开发者不应该通过阅读 Core 源码来建立信心"));
            Assert.That(doc, Does.Contain("玩家视角：单位真的能在大世界里稳定移动、绕障、成队、到达"));
            Assert.That(doc, Does.Contain("开发者视角：路线、navmesh、flowfield、chunk streaming、性能预算都能被打开查看"));
            Assert.That(doc, Does.Contain("UAT 视角：同一轮操作能产出 battle report、trace、截图、路径图和性能摘要"));
            Assert.That(doc, Does.Contain("Road graph"));
            Assert.That(doc, Does.Contain("Navmesh"));
            Assert.That(doc, Does.Contain("Flowfield"));
            Assert.That(doc, Does.Contain("Chunk streaming"));
            Assert.That(doc, Does.Contain("Large World Navigation Hub"));
            Assert.That(doc, Does.Contain("Road Graph Corridor Showcase"));
            Assert.That(doc, Does.Contain("NavMesh Bake and Query Showcase"));
            Assert.That(doc, Does.Contain("Mass Crowd Flowfield Showcase"));
            Assert.That(doc, Does.Contain("Chunk Streaming Showcase"));
            Assert.That(doc, Does.Contain("Evidence Recorder Showcase"));
            Assert.That(doc, Does.Contain("64km x 64km"));
            Assert.That(doc, Does.Contain("256x256 macro chunk"));
            Assert.That(doc, Does.Contain("10k 单位"));
            Assert.That(doc, Does.Contain("40k static obstacles"));
            Assert.That(doc, Does.Contain("record UAT"));
            Assert.That(doc, Does.Contain("frame p95"));
            Assert.That(doc, Does.Contain("稳定接近 80 FPS"));
            Assert.That(doc, Does.Contain("小于或等于 12.5ms"));
            Assert.That(doc, Does.Contain("只跑 headless，不记录真实 renderer FPS"));
            Assert.That(doc, Does.Contain("只在 100m x 100m solver window 里通过，却宣称 64km 多热点通过"));

            foreach (string id in new[]
            {
                "S1：64km 世界加载",
                "S2：远距离路网移动",
                "S3：NavMesh 最后一公里",
                "S4：10k 同屏群体移动",
                "S5：40k 静态障碍",
                "S6：Flowfield 开启",
                "S7：多热点大世界",
                "S8：诊断默认关闭",
                "S9：一键 UAT 录制"
            })
            {
                Assert.That(doc, Does.Contain(id), $"Document must list {id} in the scenario matrix.");
            }

            Assert.That(doc, Does.Not.Contain("SMOKE_VALIDATED / NOT_PRODUCTION_READY"));
            Assert.That(doc, Does.Not.Contain("当前状态必须读成"));
            Assert.That(doc, Does.Not.Contain("当前最新一键 showcase suite"));
            Assert.That(doc, Does.Not.Contain("Playable Guided Showcase"));
            Assert.That(doc, Does.Not.Contain("runtime_guide_keyframes"));
            Assert.That(doc, Does.Not.Contain("006a_runtime_u1_visual_heightmap_bake.png"));
            Assert.That(doc, Does.Not.Contain("006p_runtime_u16_bake_tool.png"));
            Assert.That(doc, Does.Not.Contain("Complete Screenshot And Keyframe Manifest"));
            Assert.That(doc, Does.Not.Contain("complete_evidence_manifest"));
            Assert.That(doc, Does.Not.Contain("showcase_incomplete_use_cases = 0"));
            Assert.That(doc, Does.Not.Contain("production_blocked_use_cases = 16"));
            Assert.That(doc, Does.Not.Contain("500/1310220/1310720"));
            Assert.That(doc, Does.Not.Contain("玩家语言版"));
            Assert.That(doc, Does.Not.Contain("亮绿色交叉"));
            Assert.That(doc, Does.Not.Contain("validator composite"));
        }

        [Test]
        public void ShowcaseAcceptanceSuite_RunsBakeSourcesAndLargeWorldWithoutProductionOverclaim()
        {
            string script = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "scripts",
                "acceptance",
                "run-mass-navigation-showcase-acceptance.ps1"));
            string largeWorldScript = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "scripts",
                "acceptance",
                "run-mass-navigation-large-world-uat.ps1"));
            string navBakeScript = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "scripts",
                "acceptance",
                "run-navmesh-bake-raylib-acceptance.ps1"));
            string evidenceRecorder = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "src",
                "Tools",
                "Ludots.Launcher.Evidence",
                "LauncherEvidenceRecorder.cs"));

            Assert.That(script, Does.Contain("run-navmesh-bake-raylib-acceptance.ps1"));
            Assert.That(script, Does.Contain("run-mass-navigation-large-world-uat.ps1"));
            Assert.That(script, Does.Contain("navmesh-layer-editor-current"));
            Assert.That(script, Does.Contain("navmesh-visual-heightmap-current"));
            Assert.That(script, Does.Contain("navmesh-logic-heightmap-current"));
            Assert.That(script, Does.Contain("Source = \"vtxm\""));
            Assert.That(script, Does.Contain("Source = \"vhtm\""));
            Assert.That(script, Does.Contain("Source = \"lhtm\""));
            Assert.That(script, Does.Contain("sourceKind -ne \"lhtm\""));
            Assert.That(script, Does.Contain("sourceOriginKind"));
            Assert.That(script, Does.Contain("totalBakedTiles"));
            Assert.That(script, Does.Contain("coveragePercent"));
            Assert.That(script, Does.Contain("logicSemanticHasMountainRiverSignals"));
            Assert.That(script, Does.Contain("ApplyEditorPatch"));
            Assert.That(navBakeScript, Does.Contain("map\", \"patch-lhtm"));
            Assert.That(navBakeScript, Does.Contain("nav\", \"bake-recast-lhtm\", \"--mapId\", \"$MapId-edited\""));
            Assert.That(navBakeScript, Does.Contain("dirty-chunks.json"));
            Assert.That(navBakeScript, Does.Contain("logic-heightmap-edit-patch.json"));
            Assert.That(navBakeScript, Does.Contain("editorPatchSaved"));
            Assert.That(navBakeScript, Does.Contain("editorDirtyChunks"));
            Assert.That(navBakeScript, Does.Contain("InteractiveWorkbench"));
            Assert.That(script, Does.Contain("use_case_statuses"));
            Assert.That(script, Does.Contain("production_gate_success -ne $true"));
            Assert.That(script, Does.Contain("StopOnFailure"));
            Assert.That(script, Does.Contain("fps_production_passed -ne $true"));
            Assert.That(script, Does.Contain("full_game_renderer_loaded_data_measured -ne $true"));
            Assert.That(script, Does.Contain("navmesh_baked_tiles -lt 1"));
            Assert.That(script, Does.Contain("macro_chunk_count * navmesh_layer_count * navmesh_profile_count"));
            Assert.That(script, Does.Contain("navmesh_not_loaded_tiles -ne"));
            Assert.That(script, Does.Contain("multi-layer active-window smoke"));
            Assert.That(script, Does.Contain("NavMesh active-window smoke"));
            Assert.That(script, Does.Contain("hpa_graph_diagnostics.Available"));
            Assert.That(script, Does.Contain("hpa_graph_diagnostics.GraphNodeCount"));
            Assert.That(script, Does.Contain("hpa_graph_diagnostics.ActiveWindowRouteAvailable"));
            Assert.That(script, Does.Contain("active_window_navmesh_query"));
            Assert.That(script, Does.Contain("path_preview"));
            Assert.That(script, Does.Contain("pick_start_world_point_then_goal_world_point"));
            Assert.That(script, Does.Contain("highlighted_route_ready"));
            Assert.That(script, Does.Contain("immutable_query_result"));
            Assert.That(script, Does.Contain("editable_order_intent"));
            Assert.That(script, Does.Contain("Mountain\", \"Naval\", \"Air"));
            Assert.That(script, Does.Contain("MeshTouchedTileCount"));
            Assert.That(script, Does.Contain("large_world_hpa_graph_route_portals"));
            Assert.That(script, Does.Contain("013_hpa_active_window_portal_route.png"));
            Assert.That(largeWorldScript, Does.Contain("machine_production_evidence_success must be true before human UAT"));
            Assert.That(largeWorldScript, Does.Contain("production_gate_success cannot be true without manual_uat_accepted=true"));
            Assert.That(largeWorldScript, Does.Contain("active_window_navmesh_query"));
            Assert.That(largeWorldScript, Does.Contain("multi-layer active-window bake"));
            Assert.That(largeWorldScript, Does.Contain("UsesSyntheticMacroGridTarget must be false"));
            Assert.That(script, Does.Contain("large_world_navmesh_baked_tiles"));
            Assert.That(script, Does.Contain("Build-ShowcaseEvidenceManifest"));
            Assert.That(script, Does.Contain("Operation Evidence Manifest"));
            Assert.That(script, Does.Contain("operation_evidence_manifest"));
            Assert.That(script, Does.Contain("showcase_body_contract"));
            Assert.That(script, Does.Contain("interactive playable/editor entry points"));
            Assert.That(script, Does.Contain("Get-ShowcaseSuiteStatus"));
            Assert.That(script, Does.Contain("Get-FileHash -Algorithm SHA256"));
            Assert.That(script, Does.Contain("Numbered route cells show the crossed chunks"));
            Assert.That(script, Does.Contain("Route chunks:"));
            Assert.That(script, Does.Contain("NavMesh visual contract"));
            Assert.That(script, Does.Contain("agent radius"));
            Assert.That(script, Does.Contain("manual_uat_accepted"));
            Assert.That(script, Does.Contain("NEEDS_MANUAL_UAT"));
            Assert.That(evidenceRecorder, Does.Contain("manual-uat-signoff.json"));
            Assert.That(evidenceRecorder, Does.Contain("Replay/smoke evidence is not a human UAT signoff"));
            Assert.That(evidenceRecorder, Does.Contain("006a_runtime_u1_visual_heightmap_bake"));
            Assert.That(evidenceRecorder, Does.Contain("006p_runtime_u16_bake_tool"));
            Assert.That(evidenceRecorder, Does.Contain("RequiredMassNavigationRuntimeUseCaseIds"));
            Assert.That(evidenceRecorder, Does.Contain("runtime_guide_keyframes"));
            Assert.That(evidenceRecorder, Does.Contain("OverlayLineContainsUseCaseId"));
            Assert.That(evidenceRecorder, Does.Contain("Playable guided showcase runtime overlay did not sample"));
            Assert.That(evidenceRecorder, Does.Contain("ScreenOverlayBuffer)?.Clear()"));
            Assert.That(script, Does.Contain("playable_guided_showcase.runtime_overlay_sampled"));
            Assert.That(script, Does.Contain("runtime_operation_evidence"));
            Assert.That(script, Does.Contain("006a_runtime_u1_visual_heightmap_bake.png"));
            Assert.That(script, Does.Contain("006p_runtime_u16_bake_tool.png"));
            Assert.That(script, Does.Contain("nav-bake-raylib-result.json"));
            Assert.That(script, Does.Contain("000_boot.png"));
            Assert.That(script, Does.Contain("030_waypoint_edit_after_pathpoints_regenerated.png"));
            Assert.That(script, Does.Contain("mass-navigation-showcase-acceptance-summary.json"));
            Assert.That(script, Does.Contain("summary_alias"));
            Assert.That(script, Does.Contain("mass-navigation-showcase-acceptance-report.md"));
            Assert.That(largeWorldScript, Does.Contain("must not hash summary.json from inside summary.json"));
            Assert.That(script, Does.Not.Contain("013_hpa_macro_synthetic_route.png"));
            Assert.That(script, Does.Not.Contain("SYNTHETIC_SMOKE"));
            Assert.That(script, Does.Not.Contain("status = \"PASS / PRODUCTION_READY\""));
            Assert.That(script, Does.Not.Contain("Write-Host \"status=PASS / PRODUCTION_READY\""));
            Assert.That(script, Does.Not.Contain("Complete Screenshot And Keyframe Manifest"));
            Assert.That(script, Does.Not.Contain("complete_evidence_manifest"));
            Assert.That(largeWorldScript, Does.Not.Contain("must be true for the deterministic showcase route"));
            Assert.That(evidenceRecorder, Does.Not.Contain("013_hpa_macro_synthetic_route.png"));
            Assert.That(evidenceRecorder, Does.Not.Contain("SYNTHETIC_SMOKE"));
        }

        [Test]
        public void ProgressDocument_SeparatesCurrentStatusFromStableAcceptanceGuide()
        {
            string repoRoot = FindRepoRoot();
            string doc = File.ReadAllText(Path.Combine(
                repoRoot,
                "gitbook",
                "reference",
                "mass-navigation-showcase-progress.md"));
            string summary = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "SUMMARY.md"));
            string referenceIndex = File.ReadAllText(Path.Combine(repoRoot, "gitbook", "reference", "README.md"));
            string evidenceRecorder = File.ReadAllText(Path.Combine(
                repoRoot,
                "src",
                "Tools",
                "Ludots.Launcher.Evidence",
                "LauncherEvidenceRecorder.cs"));
            string largeWorldScript = File.ReadAllText(Path.Combine(
                repoRoot,
                "scripts",
                "acceptance",
                "run-mass-navigation-large-world-uat.ps1"));

            Assert.That(doc, Does.Contain("# Mass Navigation Showcase 进度说明"));
            Assert.That(doc, Does.Contain("验收指南是目标合同"));
            Assert.That(doc, Does.Contain("IN_PROGRESS / NEEDS_MANUAL_UAT"));
            Assert.That(doc, Does.Contain("旧报告里曾出现过 `PASS / PRODUCTION_READY`"));
            Assert.That(doc, Does.Contain("截图只作为操作后的证据"));
            Assert.That(doc, Does.Contain("READY_TO_TEST"));
            Assert.That(doc, Does.Contain("SMOKE_ONLY"));
            Assert.That(doc, Does.Contain("NEEDS_MANUAL_UAT"));
            Assert.That(doc, Does.Contain("BLOCKED"));
            foreach (string id in new[]
            {
                "S1 64km 世界加载",
                "S2 远距离路网移动",
                "S3 NavMesh 最后一公里",
                "S4 10k 同屏群体移动",
                "S5 40k 静态障碍",
                "S6 Flowfield 开启",
                "S7 多热点大世界",
                "S8 诊断默认关闭",
                "S9 一键 UAT 录制"
            })
            {
                Assert.That(doc, Does.Contain(id), $"Progress document must cover {id}.");
            }

            Assert.That(doc, Does.Contain("loaded_chunk_count"));
            Assert.That(doc, Does.Contain("boundary_click_result"));
            Assert.That(doc, Does.Contain("ground_picking_result"));
            Assert.That(doc, Does.Contain("world_boundary_diagnostics"));
            Assert.That(doc, Does.Contain("machine_production_evidence_success"));
            Assert.That(doc, Does.Contain("manual_uat_accepted"));
            Assert.That(doc, Does.Contain("正确状态是 `NEEDS_MANUAL_UAT`"));
            Assert.That(doc, Does.Contain("Raylib playable window"));
            Assert.That(doc, Does.Contain("SDK 接入路径"));
            Assert.That(doc, Does.Contain("Clean showcase entry mods"));
            Assert.That(doc, Does.Contain("MassNavigationU01VisualHeightmapBakeShowcaseMod"));
            Assert.That(doc, Does.Contain("MassNavigationU16BakeToolQueryShowcaseMod"));
            Assert.That(doc, Does.Contain("mod:MassNavigationU05WorldHpaRouteShowcaseMod"));
            Assert.That(doc, Does.Contain("Player sees"));
            Assert.That(doc, Does.Contain("Mod author checks"));
            Assert.That(doc, Does.Contain("panelMode=Focused"));
            Assert.That(doc, Does.Contain("MassNavigationConfig.json"));
            Assert.That(doc, Does.Contain("navmesh.json"));
            Assert.That(doc, Does.Contain("pathing.json"));
            Assert.That(doc, Does.Contain("LogicHeightmap"));
            Assert.That(doc, Does.Contain("Waypoint 是可编辑的计划移动"));
            Assert.That(doc, Does.Contain("PathPoint 是某一次 query 得到的不可变底层路径点"));
            Assert.That(doc, Does.Contain("Raylib framebuffer 截图"));
            Assert.That(doc, Does.Contain("showcase 不是 slide show"));
            Assert.That(doc, Does.Contain("run-mass-navigation-usecase.ps1"));
            Assert.That(doc, Does.Contain("logic-heightmap-edit-patch.json"));
            Assert.That(doc, Does.Contain("dirty-chunks.json"));
            Assert.That(doc, Does.Contain("map patch-lhtm"));
            Assert.That(doc, Does.Contain("nav bake-recast-lhtm --dirty"));
            Assert.That(doc, Does.Contain("acceptance_proof"));
            Assert.That(doc, Does.Contain("主操作"));
            Assert.That(doc, Does.Contain("Bake VHTM Window"));
            Assert.That(doc, Does.Contain("Pick Path Preview"));
            Assert.That(doc, Does.Contain("Select 10k Army"));
            Assert.That(doc, Does.Contain("hpa_macro_diagnostics.UsesSyntheticMacroGridTarget=false"));
            Assert.That(doc.Contains("三個 subagent", StringComparison.Ordinal) || doc.Contains("三个 subagent", StringComparison.Ordinal), Is.True);
            Assert.That(doc, Does.Not.Contain("当前目标结论是 `PASS / PRODUCTION_READY`"));
            Assert.That(doc, Does.Not.Contain("本轮已完成一次完整验收复跑"));
            Assert.That(summary, Does.Contain("reference/mass-navigation-showcase-progress.md"));
            Assert.That(referenceIndex, Does.Contain("mass-navigation-showcase-progress.md"));
            Assert.That(evidenceRecorder, Does.Contain("MassNavigationWorldBoundaryDiagnostics"));
            Assert.That(evidenceRecorder, Does.Contain("boundary_click_result"));
            Assert.That(evidenceRecorder, Does.Contain("ground_picking_result"));
            Assert.That(evidenceRecorder, Does.Contain("loaded_chunk_count"));
            Assert.That(evidenceRecorder, Does.Contain("acceptance_proof"));
            Assert.That(largeWorldScript, Does.Contain("S1 boundary_click_result"));
            Assert.That(largeWorldScript, Does.Contain("S1 ground_picking_result"));
            Assert.That(largeWorldScript, Does.Contain("acceptance_proof"));

            string usecaseScript = File.ReadAllText(Path.Combine(
                repoRoot,
                "scripts",
                "acceptance",
                "run-mass-navigation-usecase.ps1"));
            Assert.That(usecaseScript, Does.Contain("MassNavigationU08TargetAllocationShowcaseMod"));
            Assert.That(usecaseScript, Does.Contain("run-navmesh-bake-raylib-acceptance.ps1"));
            Assert.That(usecaseScript, Does.Contain("EditorApplyPatch"));
            Assert.That(usecaseScript, Does.Contain("InteractiveWorkbench"));
            Assert.That(usecaseScript, Does.Contain("showcase_body=interactive_editor_workbench"));
            Assert.That(usecaseScript, Does.Contain("showcase_body=$showcaseBody"));
            Assert.That(usecaseScript, Does.Contain("\"interactive_playable_mod\""));
            Assert.That(usecaseScript, Does.Contain("\"runtime_navdata_authoring_update\""));
            Assert.That(usecaseScript, Does.Contain("evidence_mode=human_operated_window"));
            Assert.That(usecaseScript, Does.Contain("CaptureEvidence"));
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }
    }
}
