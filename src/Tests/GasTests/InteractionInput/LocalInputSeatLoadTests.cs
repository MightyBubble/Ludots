using System.Numerics;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class LocalInputSeatLoadTests
{
    [Test]
    public void LoadStartupMap_WhenLocalInputHasContextsAndNoSeat_NamesMapAndGame()
    {
        using GameEngine engine = CreateEngine();
        BindLocalInput(engine);
        engine.Start();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => engine.LoadStartupMap());

        Assert.That(ex!.Message, Does.Contain("entry"));
        Assert.That(ex.Message, Does.Contain("LudotsCoreMod"));
        Assert.That(ex.Message, Does.Contain("LOCAL_INPUT.ERR.SeatsRequired"));
    }

    [Test]
    public void LoadStartupMap_WhenNoInputHandler_DoesNotRequireSeats()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();

        Assert.DoesNotThrow(() => engine.LoadStartupMap());

        ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry);
        Assert.That(seats.Count, Is.EqualTo(0));
    }

    [Test]
    public void LoadStartupMap_WhenAuthoritativeServerHasInputHandler_DoesNotRequireSeats()
    {
        using GameEngine engine = CreateEngine();
        BindLocalInput(engine);
        engine.Start();
        engine.SetService(CoreServiceKeys.NetworkProcessRole, NetworkProcessRole.AuthoritativeServer);
        engine.SetService(CoreServiceKeys.NetworkRuntimePort, new IdleNetworkRuntime());

        Assert.DoesNotThrow(() => engine.LoadStartupMap());

        ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry);
        Assert.That(seats.Count, Is.EqualTo(0));
    }

    [Test]
    public void LoadStartupMap_WhenLocalInputDeclaresASeat_PublishesSchemeAndPossession()
    {
        using GameEngine engine = CreateEngine();
        engine.MergedConfig.StartupLocalSeats = new List<StartupLocalSeatConfig>
        {
            new()
            {
                SeatId = "seat.0",
                PlayerId = 1,
                ControlSchemeId = "scheme.default",
            },
        };
        BindLocalInput(engine);
        engine.Start();

        Assert.DoesNotThrow(() => engine.LoadStartupMap());

        ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry);
        ClientLocalSeat seat = seats.Require("seat.0");
        Assert.That(seat.ControlSchemeId, Is.EqualTo("scheme.default"));
        Assert.That(seat.HasPossession, Is.True);
        Assert.That(engine.World.IsAlive(seat.PossessedRep), Is.True);
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "CommandSourceCoreFixtureMod" }),
            Path.Combine(repoRoot, "assets"),
            Ludots.Tests.EngineUserData.CreateIsolatedRoot());
        return engine;
    }

    private static void BindLocalInput(GameEngine engine)
    {
        InputConfigRoot inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var handler = new PlayerInputHandler(new IdleInputBackend(), inputConfig);
        engine.SetService(CoreServiceKeys.InputHandler, handler);
    }

    private static string FindRepoRoot()
    {
        string? current = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "mods", "LudotsCoreMod", "mod.json")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing mods/LudotsCoreMod.");
    }

    private sealed class IdleInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }

    private sealed class IdleNetworkRuntime : INetworkRuntimePort
    {
        public NetworkProcessRole Role => NetworkProcessRole.AuthoritativeServer;
        public void Activate() { }
        public void PumpTransport() { }
        public void BeforeAuthoritativeTick(uint executingTick) { }
        public void AfterAuthoritativeCommit(uint committedTick) { }
        public void PumpReplicatedClient(float frameDeltaTime) { }
        public void Dispose() { }
    }
}
