using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Pure-data enum catalog (#1125): Enums/enums.json loads through the strict parser,
    /// member values are declaration-order indices frozen at first declaration, later
    /// mods may only append members, and every malformed shape fails closed. Event
    /// param enumType annotations validate against the same catalog.
    /// </summary>
    [TestFixture]
    [Category("ci-gate")]
    public sealed class EnumCatalogTests
    {
        private static EnumCatalogLoader LoaderFor(string json, out string root)
        {
            root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Ludots_EnumCatalog",
                Guid.NewGuid().ToString("N"));
            string core = System.IO.Path.Combine(root, "Core");
            string enumsDir = System.IO.Path.Combine(core, "Enums");
            System.IO.Directory.CreateDirectory(enumsDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(enumsDir, "enums.json"), json);

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(
                vfs,
                new Ludots.Core.Modding.ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new TriggerManager()));
            return new EnumCatalogLoader(pipeline);
        }

        private static ConfigCatalog CatalogWithEnumsEntry()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry(
                EnumCatalogLoader.ConfigPath,
                ConfigMergePolicy.ArrayById,
                idField: "id",
                arrayAppendFields: new[] { "members" },
                allowEmpty: true));
            return catalog;
        }

        private static Ludots.Core.Scripting.EnumCatalog Load(string json)
        {
            string root;
            EnumCatalogLoader loader = LoaderFor(json, out root);
            try
            {
                return loader.Load(CatalogWithEnumsEntry());
            }
            finally
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }

        private static Ludots.Core.Scripting.EnumCatalog LoadAppend(string modA, string modB)
        {
            var fragments = new List<ConfigFragment>
            {
                new(JsonNode.Parse(modA), "Core:Enums/enums.json"),
                new(JsonNode.Parse(modB), "ModB:Enums/enums.json"),
            };
            ConfigCatalogEntry entry = new(
                EnumCatalogLoader.ConfigPath,
                ConfigMergePolicy.ArrayById,
                idField: "id",
                arrayAppendFields: new[] { "members" });
            IReadOnlyList<MergedConfigEntry> merged = ConfigMerger.MergeArrayByIdToEntries(fragments, in entry);

            var builder = new Ludots.Core.Scripting.EnumCatalog.Builder();
            for (int i = 0; i < merged.Count; i++)
            {
                builder.AddOrAppend((JsonObject)merged[i].Node, $"{EnumCatalogLoader.ConfigPath} entry '{merged[i].Id}'");
            }

            return builder.ToCatalog();
        }

        [Test]
        public void Load_DeclarationOrderIsTheValue()
        {
            Ludots.Core.Scripting.EnumCatalog catalog = Load(
                @"[
                  { ""id"": ""Mod.Team"", ""members"": [""Red"", ""Blue"", ""Green""] },
                  { ""id"": ""Mod.CombatState"", ""description"": ""ai stance"", ""members"": [""Idle"", ""Combat""] }
                ]");

            That(catalog.All.Count, Is.EqualTo(2));
            That(catalog.TryGet("Mod.Team", out Ludots.Core.Scripting.EnumSchema team), Is.True);
            That(team.Members, Is.EqualTo(new[] { "Red", "Blue", "Green" }));
            That(team.TryGetValue("Red", out int red), Is.True);
            That(red, Is.EqualTo(0));
            That(team.TryGetValue("Green", out int green), Is.True);
            That(green, Is.EqualTo(2));
            That(team.TryGetValue("Purple", out _), Is.False, "unknown member has no value");
            That(team.TryGetName(1, out string blue), Is.True);
            That(blue, Is.EqualTo("Blue"));
            That(team.TryGetName(3, out _), Is.False);
            That(catalog.TryGet("Mod.CombatState", out Ludots.Core.Scripting.EnumSchema combat), Is.True);
            That(combat.TryGetValue("Combat", out int combatValue), Is.True);
            That(combatValue, Is.EqualTo(1));
        }

        [Test]
        public void Load_UndeclaredCatalogPath_YieldsEmptyCatalog()
        {
            string root;
            EnumCatalogLoader loader = LoaderFor(@"[{ ""id"": ""Mod.Team"", ""members"": [""Red""] }]", out root);
            try
            {
                Ludots.Core.Scripting.EnumCatalog catalog = loader.Load(new ConfigCatalog());
                That(catalog.All, Is.Empty);
                That(catalog.TryGet("Mod.Team", out _), Is.False);
            }
            finally
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Append_ModBAppendsMembers_EarlierValuesFrozen()
        {
            Ludots.Core.Scripting.EnumCatalog catalog = LoadAppend(
                @"[{ ""id"": ""Mod.Team"", ""members"": [""Red"", ""Blue"", ""Green""] }]",
                @"[{ ""id"": ""Mod.Team"", ""members"": [""Alpha"", ""Beta""] }]");

            That(catalog.TryGet("Mod.Team", out Ludots.Core.Scripting.EnumSchema team), Is.True);
            That(team.Members, Is.EqualTo(new[] { "Red", "Blue", "Green", "Alpha", "Beta" }));
            That(team.TryGetValue("Red", out int red) && red == 0, Is.True);
            That(team.TryGetValue("Blue", out int blue) && blue == 1, Is.True);
            That(team.TryGetValue("Green", out int green) && green == 2, Is.True, "append must not renumber earlier members");
            That(team.TryGetValue("Alpha", out int alpha) && alpha == 3, Is.True);
            That(team.TryGetValue("Beta", out int beta) && beta == 4, Is.True);
        }

        [Test]
        public void Append_SameMemberAgain_FailsClosedNamingMember()
        {
            // Merged path (engine loader): the appended member lands in the merged entry and
            // the strict parser rejects the duplicate, naming the enum and the member.
            InvalidOperationException merged = Throws<InvalidOperationException>(() => LoadAppend(
                @"[{ ""id"": ""Mod.Team"", ""members"": [""Red"", ""Blue"", ""Green""] }]",
                @"[{ ""id"": ""Mod.Team"", ""members"": [""Blue""] }]"))!;
            That(merged.Message, Does.Contain("Mod.Team"));
            That(merged.Message, Does.Contain("'Blue' more than once"));

            // Per-fragment path (Bridge aggregation): the Builder names the value-change attempt.
            var builder = new Ludots.Core.Scripting.EnumCatalog.Builder();
            builder.AddOrAppend(
                (JsonObject)JsonNode.Parse(@"{ ""id"": ""Mod.Team"", ""members"": [""Red"", ""Blue""] }")!,
                "ModA");
            InvalidOperationException appended = Throws<InvalidOperationException>(() => builder.AddOrAppend(
                (JsonObject)JsonNode.Parse(@"{ ""id"": ""Mod.Team"", ""members"": [""Blue""] }")!,
                "ModB"))!;
            That(appended.Message, Does.Contain("Mod.Team"));
            That(appended.Message, Does.Contain("'Blue'"));
            That(appended.Message, Does.Contain("cannot change an existing member's value"));
        }

        [Test]
        public void Parse_UnknownField_FailsClosed()
        {
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Scripting.EnumEntryParser.Parse(
                    (JsonObject)JsonNode.Parse(@"{ ""id"": ""Mod.X"", ""members"": [""A""], ""values"": [7] }")!,
                    "test"))!;
            That(ex.Message, Does.Contain("unknown field 'values'"));
        }

        [Test]
        public void Parse_MissingId_FailsClosed()
        {
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Scripting.EnumEntryParser.Parse(
                    (JsonObject)JsonNode.Parse(@"{ ""members"": [""A""] }")!,
                    "test"))!;
            That(ex.Message, Does.Contain("non-empty 'id'"));
        }

        [TestCase("9lives")]
        [TestCase("has-dash")]
        [TestCase("has.dot")]
        [TestCase("")]
        public void Parse_InvalidMemberName_FailsClosed(string member)
        {
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Scripting.EnumEntryParser.Parse(
                    (JsonObject)JsonNode.Parse($@"{{ ""id"": ""Mod.X"", ""members"": [""A"", ""{member}""] }}")!,
                    "test"))!;
            That(ex.Message, Does.Contain("members[1]"));
        }

        [Test]
        public void Parse_DuplicateMemberInOneEntry_FailsClosed()
        {
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Scripting.EnumEntryParser.Parse(
                    (JsonObject)JsonNode.Parse(@"{ ""id"": ""Mod.X"", ""members"": [""A"", ""A""] }")!,
                    "test"))!;
            That(ex.Message, Does.Contain("'A' more than once"));
        }

        [Test]
        public void Parse_MissingMembers_FailsClosed()
        {
            Throws<InvalidOperationException>(() =>
                Ludots.Core.Scripting.EnumEntryParser.Parse(
                    (JsonObject)JsonNode.Parse(@"{ ""id"": ""Mod.X"" }")!,
                    "test"));
        }

        [Test]
        public void CustomEventSchema_EnumType_RoundTripsThroughParser()
        {
            JsonObject node = (JsonObject)JsonNode.Parse(@"
{
  ""id"": ""ModA.Stance"",
  ""params"": [
    { ""name"": ""stance"", ""type"": ""int"", ""key"": ""ModA.StanceValue"", ""enumType"": ""Mod.CombatState"" },
    { ""name"": ""plain"", ""type"": ""int"", ""key"": ""ModA.PlainValue"" }
  ]
}")!;
            Ludots.Core.Scripting.EventSchema schema = Ludots.Core.Gameplay.MapTriggers.CustomEventSchemaParser
                .TryParse(node, "ModA.Stance", "test")!;
            That(schema.Params[0].EnumType, Is.EqualTo("Mod.CombatState"));
            That(schema.Params[1].EnumType, Is.Null, "un-annotated params stay null");
        }

        [Test]
        public void CustomEventSchema_EnumTypeOnNonIntParam_FailsClosed()
        {
            JsonObject node = (JsonObject)JsonNode.Parse(@"
{
  ""id"": ""ModA.X"",
  ""params"": [ { ""name"": ""a"", ""type"": ""float"", ""key"": ""ModA.A"", ""enumType"": ""Mod.CombatState"" } ]
}")!;
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Gameplay.MapTriggers.CustomEventSchemaParser.TryParse(node, "ModA.X", "test"))!;
            That(ex.Message, Does.Contain("int parameters only"));
        }

        [Test]
        public void CustomEventCatalogLoad_EnumTypeNotRegistered_FailsClosed()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Ludots_EnumCatalogEvents",
                Guid.NewGuid().ToString("N"));
            string core = System.IO.Path.Combine(root, "Core");
            string eventsDir = System.IO.Path.Combine(core, "Events");
            System.IO.Directory.CreateDirectory(eventsDir);
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(eventsDir, "custom_events.json"),
                    @"[{ ""id"": ""ModA.Stance"", ""params"": [ { ""name"": ""stance"", ""type"": ""int"", ""key"": ""ModA.StanceValue"", ""enumType"": ""Mod.Missing"" } ] }]");

                var vfs = new Ludots.Core.Modding.VirtualFileSystem();
                vfs.Mount("Core", core);
                var pipeline = new ConfigPipeline(
                    vfs,
                    new Ludots.Core.Modding.ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new TriggerManager()));
                var catalog = new ConfigCatalog();
                catalog.Add(new ConfigCatalogEntry(
                    Ludots.Core.Gameplay.MapTriggers.CustomEventNameRegistry.ConfigPath,
                    ConfigMergePolicy.ArrayById,
                    allowEmpty: true));

                var loader = new Ludots.Core.Gameplay.MapTriggers.CustomEventCatalogLoader(pipeline);
                InvalidOperationException ex = Throws<InvalidOperationException>(() => loader.Load(catalog, enums: null))!;
                That(ex.Message, Does.Contain("Mod.Missing"));
                That(ex.Message, Does.Contain("not registered"));
            }
            finally
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void EventParamType_HasNoEnumMember_RegressionGuard()
        {
            string[] names = Enum.GetNames(typeof(Ludots.Core.Scripting.EventParamType));
            That(names, Does.Not.Contain("Enum"),
                "enum annotations ride on int params via EventParamSchema.EnumType; a payload-type Enum member would fork the contract");
            That(names, Is.EqualTo(new[] { "Entity", "Int", "Float", "String" }));
        }
    }
}
