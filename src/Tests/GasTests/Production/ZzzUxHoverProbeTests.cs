using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using Ludots.Tests.TestCommon;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class ZzzUxHoverProbeTests
    {
        private const float DeltaTime = 1f / 60f;
        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "EntityInfoPanelsMod",
            "EntityCommandPanelMod",
            "UxPrototypeMod"
        };

        [Test]
        public void Probe_HoverWriteOwner_And_ContextMounts()
        {
            var sb = new StringBuilder();
            string repoRoot = FindRepoRoot();
            var modPaths = Ludots.Tests.RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));

            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new ProbeInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            backend.SetMousePosition(new Vector2(960f, 540f));

            AcceptanceUiHostInstaller.Install(engine);
            var view = new ProbeViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            var cameraAdapter = new ProbeCameraAdapter();
            var timingDiagnostics = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter, timingDiagnostics);
            var screenProjector = new CoreScreenProjector(engine.AuthorityCamera(), view);
            screenProjector.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, screenProjector);

            engine.Start();
            cameraPresenter.Update(engine.AuthorityCamera(), 1f);
            engine.LoadMap("ux_prototype_battle");
            for (int i = 0; i < 12; i++)
            {
                engine.Tick(DeltaTime);
                cameraPresenter.Update(engine.AuthorityCamera(), 1f);
            }

            sb.AppendLine($"TriggerErrors={engine.TriggerManager.Errors.Count}");
            foreach (TriggerError err in engine.TriggerManager.Errors) sb.AppendLine($"  TRIGERR: {err}");

            Entity rep = Entity.Null;
            bool hasRep = ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out rep);
            sb.AppendLine($"PossessedRep: found={hasRep} id={rep.Id} alive={engine.World.IsAlive(rep)} name={NameOf(engine, rep)}");
            if (engine.World.IsAlive(rep))
            {
                bool hasCtx = engine.World.TryGet(rep, out InteractionContextInstance ctx);
                sb.AppendLine($"  rep has InteractionContextInstance={hasCtx} ctxId={(hasCtx ? ctx.ContextId : -1)}");
            }

            var ctxQuery = new QueryDescription().WithAll<InteractionContextInstance>();
            int holders = 0;
            engine.World.Query(in ctxQuery, (Entity e, ref InteractionContextInstance ctx) =>
            {
                holders++;
                sb.AppendLine($"  CTX-HOLDER id={e.Id} name={NameOf(engine, e)} ctxId={ctx.ContextId} activeKeyId={ctx.ActiveCollectionKeyId} src={ctx.Source}");
            });
            sb.AppendLine($"ContextHolders={holders}");

            Entity blueCity = FindByName(engine, "Blue City");
            sb.AppendLine($"BlueCity id={blueCity.Id} alive={engine.World.IsAlive(blueCity)}");
            Vector2 screen = screenProjector.WorldToScreen(
                engine.World.TryGet(blueCity, out VisualTransform vt)
                    ? vt.Position
                    : Ludots.Core.Mathematics.WorldUnitsFix64.WorldCmToVisualMeters(
                        engine.World.Get<WorldPositionCm>(blueCity).Value, yMeters: 0f));
            sb.AppendLine($"BlueCity screen=({screen.X:F1},{screen.Y:F1})");

            Vector2[] offsets = new[]
            {
                Vector2.Zero, new Vector2(0f, -24f), new Vector2(0f, 24f), new Vector2(-24f, 0f), new Vector2(24f, 0f)
            };
            foreach (Vector2 off in offsets)
            {
                Vector2 candidate = screen + off;
                backend.SetMousePosition(candidate);
                for (int t = 0; t < 3; t++)
                {
                    engine.SetService(CoreServiceKeys.UiCaptured, false);
                    int tickBefore = engine.GameSession.CurrentTick;
                    engine.Tick(DeltaTime);
                    cameraPresenter.Update(engine.AuthorityCamera(), 1f);
                    int tickAfter = engine.GameSession.CurrentTick;
                    bool snapOk = PointerInteractionSnapshotReader.TryRead(engine.GlobalContext, out var snap);
                    Vector2 ptr = snapOk ? snap.Pointer : new Vector2(-9f, -9f);
                    int hCount = rep != Entity.Null
                        ? EntityCollectionContextRuntime.GetCount(engine.GlobalContext, rep, EntityCollectionKeys.HoveredEntity)
                        : -1;
                    Entity hs = Entity.Null;
                    bool okSample = rep != Entity.Null && EntityCollectionContextRuntime.TryGetPrimary(engine.World, engine.GlobalContext, rep, EntityCollectionKeys.HoveredEntity, out hs);
                    sb.AppendLine($"  sample ({candidate.X:F1},{candidate.Y:F1}) t={t} simTick={tickBefore}->{tickAfter} snapOk={snapOk} ptr=({ptr.X:F1},{ptr.Y:F1}) hCount={hCount} hoverOk={okSample} {(okSample ? NameOf(engine, hs) : string.Empty)}");
                }
            }

            DumpHover(engine, sb, rep, "rep");
            DumpHover(engine, sb, blueCity, "blueCity");

            File.WriteAllText(Path.Combine(Path.GetTempPath(), "ux_hover_probe.txt"), sb.ToString());
            Assert.Fail(sb.ToString());
        }

        private static void DumpHover(GameEngine engine, StringBuilder sb, Entity owner, string label)
        {
            if (!engine.World.IsAlive(owner))
            {
                sb.AppendLine($"  hover[{label}]: owner dead");
                return;
            }
            int count = EntityCollectionContextRuntime.GetCount(engine.GlobalContext, owner, EntityCollectionKeys.HoveredEntity);
            bool ok = EntityCollectionContextRuntime.TryGetPrimary(engine.World, engine.GlobalContext, owner, EntityCollectionKeys.HoveredEntity, out Entity h);
            sb.AppendLine($"  hover[{label}] owner={owner.Id} count={count} primaryOk={ok} primary={(ok ? $"{h.Id}:{NameOf(engine, h)}" : "<none>")}");
        }

        private static string NameOf(GameEngine engine, Entity e)
        {
            return engine.World.IsAlive(e) && engine.World.TryGet(e, out Name n) ? n.Value : "?";
        }

        private static Entity FindByName(GameEngine engine, string target)
        {
            Entity found = Entity.Null;
            var q = new QueryDescription().WithAll<Name>();
            engine.World.Query(in q, (Entity e, ref Name n) =>
            {
                if (found == Entity.Null && string.Equals(n.Value, target, StringComparison.Ordinal)) found = e;
            });
            return found;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) && Directory.Exists(Path.Combine(dir.FullName, "assets")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("repo root not found");
        }

        private sealed class ProbeInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.OrdinalIgnoreCase);
            private Vector2 _mousePosition;
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool pressed) && pressed;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
            public void SetMousePosition(Vector2 p) => _mousePosition = p;
        }

        private sealed class ProbeViewController : IViewController
        {
            public ProbeViewController(float width, float height) => Resolution = new Vector2(width, height);
            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class ProbeCameraAdapter : ICameraAdapter
        {
            public CameraRenderState3D LastState { get; private set; }
            public void UpdateCamera(in CameraRenderState3D state) => LastState = state;
        }
    }
}
