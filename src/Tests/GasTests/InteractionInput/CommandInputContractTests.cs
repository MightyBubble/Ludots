using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.InputRouting
{
    [TestFixture]
    public sealed class CommandInputContractTests
    {
        [Test]
        public void RtsDemo_LocalInputAssets_AreCompleteAndReachable()
        {
            string repoRoot = FindRepoRoot();
            string inputPath = Path.Combine(repoRoot, "mods", "showcases", "rts_demo", "RtsDemoMod", "assets", "Input", "default_input.json");
            string contextPath = Path.Combine(repoRoot, "mods", "showcases", "rts_demo", "RtsDemoMod", "assets", "Input", "interaction_context_profiles.json");
            string gamePath = Path.Combine(repoRoot, "mods", "showcases", "rts_demo", "RtsDemoMod", "assets", "game.json");

            Assert.That(File.Exists(inputPath), Is.True, $"Missing RTS input config: {inputPath}");
            Assert.That(File.Exists(contextPath), Is.True, $"Missing RTS interaction contexts: {contextPath}");
            Assert.That(File.Exists(gamePath), Is.True, $"Missing RTS game config: {gamePath}");

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            jsonOptions.Converters.Add(new JsonStringEnumConverter());

            var inputConfig = JsonSerializer.Deserialize<InputConfigRoot>(File.ReadAllText(inputPath), jsonOptions);
            Assert.That(inputConfig, Is.Not.Null);
            Assert.That(inputConfig!.Contexts.Exists(context => string.Equals(context.Id, "Rts_Gameplay", StringComparison.Ordinal)),
                Is.True,
                "RtsDemoMod must register its gameplay context explicitly.");

            var actionIds = inputConfig.Actions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
            using var contextDoc = JsonDocument.Parse(File.ReadAllText(contextPath));
            JsonElement battle = contextDoc.RootElement.GetProperty("profiles").EnumerateArray()
                .Single(profile => profile.GetProperty("id").GetString() == "interaction.context.rts.battle");
            string[] bindings = battle.GetProperty("bindings").EnumerateArray().Select(element => element.GetString()!).ToArray();
            string[] triggers = battle.GetProperty("triggers").EnumerateArray()
                .Select(element => element.GetProperty("trigger").GetString()!)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(battle.GetProperty("commandIntentId").GetString(), Is.EqualTo("intent.command.default"));
                Assert.That(bindings, Does.Contain("Command").And.Contain("Stop").And.Contain("QueueModifier"));
                Assert.That(triggers, Does.Contain("graph.rts.command_commit").And.Contain("graph.rts.stop"));
                foreach (string binding in bindings)
                {
                    Assert.That(actionIds, Does.Contain(binding), $"RTS battle binding '{binding}' is not declared in default_input.json.");
                }
            });

            using var gameDoc = JsonDocument.Parse(File.ReadAllText(gamePath));
            var startupContexts = gameDoc.RootElement.GetProperty("startupInputContexts")
                .EnumerateArray()
                .Select(element => element.GetString())
                .ToArray();
            Assert.That(startupContexts, Does.Contain("Rts_Gameplay"));
        }

        [Test]
        public void DefaultCommandIntentProfile_RoutesGroundAndInspectableEntityHitsToMove()
        {
            string repoRoot = FindRepoRoot();
            string profilePath = Path.Combine(repoRoot, "assets", "Input", "command_intent_profiles.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
            CommandIntentProfilesConfig config = JsonSerializer.Deserialize<CommandIntentProfilesConfig>(
                    File.ReadAllText(profilePath),
                    options)
                ?? throw new InvalidOperationException("Default command intent profile config failed to parse.");

            CommandIntentProfileDefinition profile = config.Profiles.Single(p =>
                string.Equals(p.Id, "intent.command.default", StringComparison.Ordinal));
            Assert.That(
                profile.Rules.Any(rule =>
                    rule.Target?.HasEntity == false &&
                    string.Equals(rule.Route?.OrderTypeKey, "moveTo", StringComparison.Ordinal) &&
                    rule.Route.TargetShape == CommandIntentTargetShape.WorldPositionCm),
                Is.True,
                "Default command must keep explicit ground movement.");
            Assert.That(
                profile.Rules.Any(rule =>
                    rule.Target?.HasEntity == true &&
                    string.Equals(rule.Route?.OrderTypeKey, "moveTo", StringComparison.Ordinal) &&
                    rule.Route.TargetShape == CommandIntentTargetShape.WorldPositionCm),
                Is.True,
                "Default command must explicitly treat an inspectable entity under the pointer as a valid move destination, so right-clicking neutral showcase props is not silently dropped.");
        }

        [Test]
        public void InputOrderActorAuthorization_UsesPlayerRepresentativeOwnershipAndControlGrants()
        {
            using var world = World.Create();
            Entity playerOne = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity playerTwo = world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity ownedActor = world.Create();
            Entity grantedActor = world.Create();
            Entity foreignActor = world.Create();
            var harness = CreateControlDomain(world);
            harness.Ownership.EnsureOwnership(playerOne, ownedActor);
            harness.Ownership.EnsureOwnership(playerTwo, foreignActor);
            harness.Relationships.EnsureLink(playerOne, grantedActor, harness.ControlsTypeId);
            var players = new PlayerEntityLookup();
            players.Register(1, playerOne);
            players.Register(2, playerTwo);

            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, playerOne, 1), Is.True);
            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, ownedActor, 1), Is.True);
            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, grantedActor, 1), Is.True);
            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, foreignActor, 1), Is.False);
            Assert.That(InputOrderActorAuthorization.IsAuthorized(
                world, players, harness.Domains, ownedActor, 2), Is.False);
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static ControlDomainHarness CreateControlDomain(World world)
        {
            var types = new RelationshipTypeRegistry();
            var relationships = new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 8),
                new RelationshipReverseIndex(world));
            int ownsTypeId = types.Register("Owns");
            int controlsTypeId = types.Register("Controls");
            var ownership = new OwnershipResolver(relationships, ownsTypeId);
            return new ControlDomainHarness(
                relationships,
                ownership,
                new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId),
                controlsTypeId);
        }

        private readonly record struct ControlDomainHarness(
            RelationshipRuntime Relationships,
            OwnershipResolver Ownership,
            ControlDomainQuery Domains,
            int ControlsTypeId);
    }
}
