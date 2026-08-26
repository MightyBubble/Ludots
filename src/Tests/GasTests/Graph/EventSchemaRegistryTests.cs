using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Event parameter schema SSOT (#1114): built-in schemas derive from code and
    /// reference real MapTriggerEventPayloadKeys constants, mod schema parsing fails
    /// closed on unknown fields / reserved keys / out-of-whitelist types, and fire-time
    /// payload contract validation catches missing, mistyped, and undeclared keys.
    /// </summary>
    [TestFixture]
    public sealed class EventSchemaRegistryTests
    {
        private static EventSchemaRegistry RegistryWithProbeEvent(out EventSchema probeSchema)
        {
            var registry = new EventSchemaRegistry();
            JsonObject node = (JsonObject)JsonNode.Parse(@"
{
  ""id"": ""ModA.Probe"",
  ""description"": ""probe"",
  ""scope"": ""entity"",
  ""params"": [
    { ""name"": ""toolUser"", ""type"": ""entity"", ""key"": ""ModA.ToolUser"" },
    { ""name"": ""probeTag"", ""type"": ""int"", ""key"": ""ModA.ProbeTag"", ""optional"": true }
  ]
}")!;
            probeSchema = CustomEventSchemaParser.TryParse(node, "ModA.Probe", "test entry 'ModA.Probe'")!;
            registry.RegisterCustom(probeSchema);
            return registry;
        }

        [Test]
        public void BuiltinSchemas_CoverPayloadBearingEvents()
        {
            var registry = new EventSchemaRegistry();
            string[] payloadBearing =
            {
                GameEvents.MapHeartbeat.Value,
                GameEvents.EntitySpawned.Value,
                GameEvents.EntityDied.Value,
                GameEvents.EntityAliveCountChanged.Value,
                GameEvents.RegionEntered.Value,
                GameEvents.RegionExited.Value,
                GameEvents.InputActionFired.Value,
                GameEvents.CalendarDayAdvanced.Value,
                GameEvents.CalendarCyclePhaseEntered.Value,
                GameEvents.CalendarCyclePhaseExited.Value,
                GameEvents.CalendarEraChanged.Value,
                GameEvents.CalendarDayPhaseChanged.Value,
            };

            foreach (string eventName in payloadBearing)
            {
                Assert.That(registry.TryGet(eventName, out _), Is.True,
                    $"Built-in event '{eventName}' must have a schema entry.");
            }

            Assert.That(registry.TryGet(GameEvents.EntityDied.Value, out EventSchema died), Is.True);
            Assert.That(died.Params.Select(p => p.Name).ToArray(), Is.EqualTo(new[] { "sourceEntity", "sourceTeamId" }));
            Assert.That(died.Params[0].Type, Is.EqualTo(EventParamType.Entity));
            Assert.That(died.Params[1].Type, Is.EqualTo(EventParamType.Int));

            Assert.That(registry.TryGet(GameEvents.InputActionFired.Value, out EventSchema input), Is.True);
            EventParamSchema target = input.Params.Single(p => p.Name == "targetEntity");
            Assert.That(target.Optional, Is.True, "InputActionFired only carries targetEntity when an entity was picked.");
        }

        [Test]
        public void BuiltinSchemas_ReferenceOnlyRealPayloadKeyConstants()
        {
            var registry = new EventSchemaRegistry();
            var constantValues = typeof(MapTriggerEventPayloadKeys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => (string)field.GetRawConstantValue()!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (EventSchema schema in registry.All)
            {
                foreach (EventParamSchema param in schema.Params)
                {
                    Assert.That(constantValues.Contains(param.PayloadKey), Is.True,
                        $"Schema '{schema.EventName}' param '{param.Name}' must reference a MapTriggerEventPayloadKeys constant.");
                }
            }
        }

        [Test]
        public void Parse_ValidCustomSchema_EntersRegistry()
        {
            var registry = RegistryWithProbeEvent(out EventSchema schema);
            Assert.That(schema.Scope, Is.EqualTo(EventScope.Entity));
            Assert.That(registry.TryGet("ModA.Probe", out EventSchema loaded), Is.True);
            Assert.That(loaded.Params.Select(p => p.PayloadKey).ToArray(),
                Is.EqualTo(new[] { "ModA.ToolUser", "ModA.ProbeTag" }));
            Assert.That(loaded.Params[1].Optional, Is.True);
        }

        [Test]
        public void Parse_WithoutParams_YieldsParameterlessSchema()
        {
            JsonObject node = (JsonObject)JsonNode.Parse(@"{ ""id"": ""ModA.Bare"", ""description"": ""bare"" }")!;
            EventSchema? schema = CustomEventSchemaParser.TryParse(node, "ModA.Bare", "test");
            Assert.That(schema, Is.Not.Null, "parameterless entries still produce a schema (#1123)");
            Assert.That(schema!.Scope, Is.EqualTo(EventScope.Map), "the default scope is Map");
            Assert.That(schema.Params, Is.Empty);
        }

        [Test]
        public void Parse_UnknownEntryField_FailsClosed()
        {
            JsonObject node = (JsonObject)JsonNode.Parse(@"{ ""id"": ""ModA.X"", ""typo"": 1 }")!;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                CustomEventSchemaParser.TryParse(node, "ModA.X", "test"));
            Assert.That(ex.Message, Does.Contain("unknown field 'typo'"));
        }

        [Test]
        public void Parse_UnknownParamField_FailsClosed()
        {
            JsonObject node = (JsonObject)JsonNode.Parse(
                @"{ ""id"": ""ModA.X"", ""params"": [ { ""name"": ""a"", ""type"": ""int"", ""key"": ""ModA.A"", ""extra"": 1 } ] }")!;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                CustomEventSchemaParser.TryParse(node, "ModA.X", "test"));
            Assert.That(ex.Message, Does.Contain("unknown field 'extra'"));
        }

        [Test]
        public void Parse_BadScope_FailsClosed()
        {
            JsonObject node = (JsonObject)JsonNode.Parse(@"{ ""id"": ""ModA.X"", ""scope"": ""solar"" }")!;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                CustomEventSchemaParser.TryParse(node, "ModA.X", "test"));
            Assert.That(ex.Message, Does.Contain("scope"));
        }

        [TestCase("bool")]
        [TestCase("region")]
        [TestCase("team")]
        public void Parse_TypeAwaitingVariableContract_FailsClosed(string type)
        {
            JsonObject node = (JsonObject)JsonNode.Parse(
                @"{ ""id"": ""ModA.X"", ""params"": [ { ""name"": ""a"", ""type"": """ + type + @""", ""key"": ""ModA.A"" } ] }")!;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                CustomEventSchemaParser.TryParse(node, "ModA.X", "test"));
            Assert.That(ex.Message, Does.Contain("map variable type contract"));
        }

        [Test]
        public void Parse_UnknownType_FailsClosed()
        {
            JsonObject node = (JsonObject)JsonNode.Parse(
                @"{ ""id"": ""ModA.X"", ""params"": [ { ""name"": ""a"", ""type"": ""quaternion"", ""key"": ""ModA.A"" } ] }")!;
            Assert.Throws<InvalidOperationException>(() =>
                CustomEventSchemaParser.TryParse(node, "ModA.X", "test"));
        }

        [Test]
        public void Register_ReservedMapTriggerKey_FailsClosed()
        {
            var registry = new EventSchemaRegistry();
            var schema = new EventSchema("ModA.X", EventScope.Map, new[]
            {
                new EventParamSchema("a", EventParamType.Int, MapTriggerEventPayloadKeys.Count),
            });
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => registry.RegisterCustom(schema));
            Assert.That(ex.Message, Does.Contain("MapTrigger"));
        }

        [Test]
        public void Register_UnnamespacedKey_FailsClosed()
        {
            var registry = new EventSchemaRegistry();
            var schema = new EventSchema("ModA.X", EventScope.Map, new[]
            {
                new EventParamSchema("a", EventParamType.Int, "NoDot"),
            });
            Assert.Throws<InvalidOperationException>(() => registry.RegisterCustom(schema));
        }

        [Test]
        public void Register_DuplicateParamKeyWithinEvent_FailsClosed()
        {
            var registry = new EventSchemaRegistry();
            var schema = new EventSchema("ModA.X", EventScope.Map, new[]
            {
                new EventParamSchema("a", EventParamType.Int, "ModA.A"),
                new EventParamSchema("b", EventParamType.Int, "ModA.A"),
            });
            Assert.Throws<InvalidOperationException>(() => registry.RegisterCustom(schema));
        }

        [Test]
        public void Register_EventNameCollidingWithBuiltin_FailsClosed()
        {
            var registry = new EventSchemaRegistry();
            var schema = new EventSchema(GameEvents.EntityDied.Value, EventScope.Map, Array.Empty<EventParamSchema>());
            Assert.Throws<InvalidOperationException>(() => registry.RegisterCustom(schema));
        }

        [Test]
        public void ValidateFirePayload_MissingDeclaredParam_FailsClosedNamingIt()
        {
            var registry = RegistryWithProbeEvent(out _);
            var context = new ScriptContext();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                registry.ValidateFirePayload(new EventKey("ModA.Probe"), context));
            Assert.That(ex.Message, Does.Contain("toolUser"));
            Assert.That(ex.Message, Does.Contain("ModA.ToolUser"));
        }

        [Test]
        public void ValidateFirePayload_MistypedParam_FailsClosed()
        {
            var registry = RegistryWithProbeEvent(out _);
            var context = new ScriptContext();
            context.Set("ModA.ToolUser", "not-an-entity");
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                registry.ValidateFirePayload(new EventKey("ModA.Probe"), context));
            Assert.That(ex.Message, Does.Contain("ParamTypeMismatch"));
        }

        [Test]
        public void ValidateFirePayload_UndeclaredMapTriggerKey_FailsClosed()
        {
            var registry = RegistryWithProbeEvent(out _);
            var context = new ScriptContext();
            context.Set("ModA.ToolUser", default(Entity));
            context.Set(MapTriggerEventPayloadKeys.Count, 3);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                registry.ValidateFirePayload(new EventKey("ModA.Probe"), context));
            Assert.That(ex.Message, Does.Contain("UndeclaredPayloadKey"));
        }

        [Test]
        public void ValidateFirePayload_OptionalAbsentAndParamless_Pass()
        {
            var registry = RegistryWithProbeEvent(out _);
            var context = new ScriptContext();
            context.Set("ModA.ToolUser", default(Entity));
            Assert.DoesNotThrow(() => registry.ValidateFirePayload(new EventKey("ModA.Probe"), context));

            var paramless = new EventSchema("ModA.Bare", EventScope.Map, Array.Empty<EventParamSchema>());
            registry.RegisterCustom(paramless);
            Assert.DoesNotThrow(() => registry.ValidateFirePayload(new EventKey("ModA.Bare"), new ScriptContext()));
        }

        [Test]
        public void TriggerManager_FireMapEvent_ValidatesBuiltinContract()
        {
            var manager = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
            var mapId = new MapId("schema_probe");

            var complete = new ScriptContext();
            complete.Set(MapTriggerEventPayloadKeys.SourceEntity, default(Entity));
            complete.Set(MapTriggerEventPayloadKeys.SourceTeamId, 2);
            Assert.DoesNotThrow(() => manager.FireMapEvent(mapId, GameEvents.EntityDied, complete));

            var missingTeam = new ScriptContext();
            missingTeam.Set(MapTriggerEventPayloadKeys.SourceEntity, default(Entity));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                manager.FireMapEvent(mapId, GameEvents.EntityDied, missingTeam));
            Assert.That(ex.Message, Does.Contain("sourceTeamId"));
        }

        [Test]
        public void BuiltinSchemas_CoverMapVariableChangedContract()
        {
            var registry = new EventSchemaRegistry();

            Assert.That(registry.TryGet(GameEvents.MapVariableChanged.Value, out EventSchema schema), Is.True);
            Assert.That(schema.Scope, Is.EqualTo(EventScope.Map));
            Assert.That(schema.Params.Count, Is.EqualTo(5));
            Assert.That(schema.Params[0].Name, Is.EqualTo("varName"));
            Assert.That(schema.Params[0].PayloadKey, Is.EqualTo(MapTriggerEventPayloadKeys.VarName));
            Assert.That(schema.Params[0].Optional, Is.False);
            Assert.That(schema.DeclaresPayloadKey(MapTriggerEventPayloadKeys.VarValueInt), Is.True);
            Assert.That(schema.DeclaresPayloadKey(MapTriggerEventPayloadKeys.VarValueFloat), Is.True);
            Assert.That(schema.DeclaresPayloadKey(MapTriggerEventPayloadKeys.OldValueInt), Is.True);
            Assert.That(schema.DeclaresPayloadKey(MapTriggerEventPayloadKeys.OldValueFloat), Is.True);

            var complete = new ScriptContext();
            complete.Set(MapTriggerEventPayloadKeys.VarName, "stage");
            complete.Set(MapTriggerEventPayloadKeys.VarValueInt, 2);
            complete.Set(MapTriggerEventPayloadKeys.OldValueInt, 1);
            Assert.DoesNotThrow(() => registry.ValidateFirePayload(GameEvents.MapVariableChanged, complete));

            var missingName = new ScriptContext();
            missingName.Set(MapTriggerEventPayloadKeys.VarValueInt, 2);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                registry.ValidateFirePayload(GameEvents.MapVariableChanged, missingName));
            Assert.That(ex.Message, Does.Contain("varName"));
        }

        [Test]
        public void CatalogLoad_FailsClosed_WhenSchemaExceedsEntryPayloadCapacity()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Ludots_EventSchemaCapacity",
                Guid.NewGuid().ToString("N"));
            string core = System.IO.Path.Combine(root, "Core");
            string eventsDir = System.IO.Path.Combine(core, "Events");
            System.IO.Directory.CreateDirectory(eventsDir);
            try
            {
                var parameters = new System.Text.StringBuilder();
                for (int i = 0; i < 17; i++)
                {
                    if (i > 0)
                    {
                        parameters.Append(',');
                    }

                    parameters.Append($"{{\"name\":\"p{i}\",\"type\":\"int\",\"key\":\"Capacity.Probe{i}\"}}");
                }

                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(eventsDir, "custom_events.json"),
                    $"[{{\"id\":\"Capacity.TooMany\",\"scope\":\"map\",\"params\":[{parameters}]}}]");

                var vfs = new Ludots.Core.Modding.VirtualFileSystem();
                vfs.Mount("Core", core);
                var pipeline = new Ludots.Core.Config.ConfigPipeline(
                    vfs,
                    new Ludots.Core.Modding.ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new TriggerManager()));
                var catalog = new Ludots.Core.Config.ConfigCatalog();
                catalog.Add(new Ludots.Core.Config.ConfigCatalogEntry(
                    CustomEventNameRegistry.ConfigPath,
                    Ludots.Core.Config.ConfigMergePolicy.ArrayById,
                    allowEmpty: true));

                var loader = new CustomEventCatalogLoader(pipeline);
                InvalidOperationException overflow = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
                Assert.That(overflow.Message, Does.Contain("at most 16"));
            }
            finally
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }
    }
}
