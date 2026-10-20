using System.Text.Json.Nodes;
using EntityCommandPanelMod.Runtime;
using Ludots.Core.Input.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class EntityCommandPanelSlotBindingTests
    {
        [Test]
        public void Parse_RejectsBareActionId()
        {
            JsonObject json = ParseObject("""{"slots":["Aim.Slot"],"modeHintContexts":{}}""");
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityCommandPanelSlotActionConfig.Parse(json))!;
            Assert.That(ex.Message, Does.Contain("slots[0]"));
            Assert.That(ex.Message, Does.Contain("object"));
        }

        [Test]
        public void Parse_RejectsLegacySlotActionIds()
        {
            JsonObject json = ParseObject("""{"slotActionIds":["Aim.Slot"],"slots":[],"modeHintContexts":{}}""");
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityCommandPanelSlotActionConfig.Parse(json))!;
            Assert.That(ex.Message, Does.Contain("slotActionIds"));
        }

        [Test]
        public void Parse_RejectsNonIntegerBinding()
        {
            JsonObject json = ParseObject("""{"slots":[{"action":"Aim.Slot","int":1.5}],"modeHintContexts":{}}""");
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EntityCommandPanelSlotActionConfig.Parse(json))!;
            Assert.That(ex.Message, Does.Contain("integer"));
        }

        [Test]
        public void RequireSlotBinding_UnknownAction_Throws()
        {
            InputConfigRoot config = Button("SkillQ", "<Keyboard>/q", null);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InputBindingKeyLookup.RequireSlotBinding(config, "Aim.Slot", null, "slots[0]"))!;
            Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_BINDING_KEY_ACTION_UNKNOWN"));
        }

        [Test]
        public void RequireSlotBinding_IntWhenBindingsCarryNone_Throws()
        {
            InputConfigRoot config = Button("SkillQ", "<Keyboard>/q", null);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InputBindingKeyLookup.RequireSlotBinding(config, "SkillQ", 0, "slots[0]"))!;
            Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_BINDING_KEY_INT_UNEXPECTED"));
        }

        [Test]
        public void RequireSlotBinding_OmittedIntWhenBindingsCarryInt_Throws()
        {
            InputConfigRoot config = Button("Aim.Slot", "<Keyboard>/q", 0);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InputBindingKeyLookup.RequireSlotBinding(config, "Aim.Slot", null, "slots[0]"))!;
            Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_BINDING_KEY_INT_REQUIRED"));
        }

        [Test]
        public void RequireSlotBinding_IntNotOnAnyBinding_Throws()
        {
            InputConfigRoot config = AimKeyboard();
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InputBindingKeyLookup.RequireSlotBinding(config, "Aim.Slot", 9, "slots[0]"))!;
            Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_BINDING_KEY_INT_UNBOUND"));
        }

        [Test]
        public void Resolve_DataOnlyFifthAndSixthSlots_LabelTAndY()
        {
            InputConfigRoot config = AimKeyboard();
            string[] contexts = { "Aim.Kbm" };
            Assert.That(InputBindingKeyLookup.Resolve(config, contexts, "control scheme 'scheme.aim.kbm'", "Aim.Slot", 0), Is.EqualTo("Q"));
            Assert.That(InputBindingKeyLookup.Resolve(config, contexts, "control scheme 'scheme.aim.kbm'", "Aim.Slot", 4), Is.EqualTo("T"));
            Assert.That(InputBindingKeyLookup.Resolve(config, contexts, "control scheme 'scheme.aim.kbm'", "Aim.Slot", 5), Is.EqualTo("Y"));
        }

        [Test]
        public void Resolve_BindingAbsentFromNamedContexts_Throws()
        {
            InputConfigRoot config = Button("SkillQ", "<Keyboard>/q", null, "Rts_Gameplay");
            config.Contexts.Add(Context("Aim.Kbm", 100));
            InputBindingKeyLookup.RequireSlotBinding(config, "SkillQ", null, "slots[0]");
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InputBindingKeyLookup.Resolve(config, new[] { "Aim.Kbm" }, "control scheme 'scheme.aim.kbm'", "SkillQ", null))!;
            Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_BINDING_KEY_ABSENT"));
        }

        [Test]
        public void Resolve_SchemeContexts_LabelSkillWithoutInt()
        {
            var config = new InputConfigRoot();
            config.Actions.Add(new InputActionDef { Id = "SkillQ", Type = InputActionType.Button });
            config.Contexts.Add(Context("Camera.Rig", 0, new InputBindingDef { ActionId = "Select.Begin", Path = "<Mouse>/leftButton" }));
            config.Contexts.Add(Context("Rts_Gameplay", 50, new InputBindingDef { ActionId = "SkillQ", Path = "<Keyboard>/q" }));
            Assert.That(
                InputBindingKeyLookup.Resolve(config, new[] { "Camera.Rig", "Rts_Gameplay" }, "control scheme 'scheme.default'", "SkillQ", null),
                Is.EqualTo("Q"));
        }

        [Test]
        public void Resolve_TwoPathsInWinningContext_Throws()
        {
            var config = new InputConfigRoot();
            config.Actions.Add(new InputActionDef { Id = "Aim.Slot", Type = InputActionType.Button });
            config.Contexts.Add(Context(
                "Aim.Kbm",
                100,
                new InputBindingDef { ActionId = "Aim.Slot", Path = "<Keyboard>/q", IntValue = 0 },
                new InputBindingDef { ActionId = "Aim.Slot", Path = "<Keyboard>/t", IntValue = 0 }));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InputBindingKeyLookup.Resolve(config, new[] { "Aim.Kbm" }, "control scheme 'scheme.aim.kbm'", "Aim.Slot", 0))!;
            Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_BINDING_KEY_AMBIGUOUS"));
        }

        private static JsonObject ParseObject(string json)
        {
            return JsonNode.Parse(json)!.AsObject();
        }

        private static InputConfigRoot Button(string actionId, string path, int? bindingInt, string contextId = "Aim.Kbm")
        {
            var config = new InputConfigRoot();
            config.Actions.Add(new InputActionDef { Id = actionId, Type = InputActionType.Button });
            config.Contexts.Add(Context(contextId, 100, new InputBindingDef
            {
                ActionId = actionId,
                Path = path,
                IntValue = bindingInt
            }));
            return config;
        }

        private static InputConfigRoot AimKeyboard()
        {
            var config = new InputConfigRoot();
            config.Actions.Add(new InputActionDef { Id = "Aim.Slot", Type = InputActionType.Button });
            var bindings = new InputBindingDef[6];
            string keys = "qwerty";
            for (int i = 0; i < bindings.Length; i++)
            {
                bindings[i] = new InputBindingDef
                {
                    ActionId = "Aim.Slot",
                    Path = "<Keyboard>/" + keys[i],
                    IntValue = i
                };
            }

            config.Contexts.Add(Context("Aim.Kbm", 100, bindings));
            config.Contexts.Add(Context("Aim.Gamepad", 100, new InputBindingDef
            {
                ActionId = "Aim.Slot",
                Path = "<Gamepad>/buttonSouth",
                IntValue = 0
            }));
            return config;
        }

        private static InputContextDef Context(string id, int priority, params InputBindingDef[] bindings)
        {
            var context = new InputContextDef
            {
                Id = id,
                Priority = priority
            };
            context.Bindings.AddRange(bindings);
            return context;
        }
    }
}
