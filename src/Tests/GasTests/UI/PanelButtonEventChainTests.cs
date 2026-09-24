using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Input;
using Ludots.UI.Panels;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// Button event chain (#1585 U2): loader strictness (button ↔ event wiring both ways,
    /// tap-only gestures, payload field coverage), payload baking, and the bridge — audience
    /// admission before injection, eventId-即-action-id enforcement, and the press edge
    /// landing in the firing seat's handler with a scheduled release.
    /// </summary>
    [TestFixture]
    public sealed class PanelButtonEventChainTests
    {
        private const string TemplateId = "tests.panel.dialog";
        private const string ChooseActionId = "Ui.DialogChoose";

        // ── Loader: Button ↔ event wiring ──

        [Test]
        public void Loader_ButtonWithEventAndPayload_Parses()
        {
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson());
            Assert.That(template.Events.Count, Is.EqualTo(1));
            Assert.That(template.Events[0].Control, Is.EqualTo("chooseA"));
            PanelLayoutControl button = RequireButton(template);
            Assert.That(button.ControlName, Is.EqualTo("chooseA"));
            Assert.That(button.EventPayload.ContainsKey("option"), Is.True);
        }

        [Test]
        public void Loader_ButtonWithoutEvent_FailsClosed()
        {
            string json = ButtonTemplateJson(events: "[]");
            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("not referenced by any declared event"));
        }

        [Test]
        public void Loader_EventReferencingUnknownControl_FailsClosed()
        {
            string json = ButtonTemplateJson(eventControl: "missingButton");
            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("no button with that control name"));
        }

        [Test]
        public void Loader_NonTapGestureOnButton_FailsClosed()
        {
            string json = ButtonTemplateJson(gesture: "change");
            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("tap"));
        }

        [Test]
        public void Loader_ButtonPayloadFieldNotInEventSchema_FailsClosed()
        {
            string json = ButtonTemplateJson(payload: "{ \"option\": \"1\", \"extra\": \"2\" }");
            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("not declared by event"));
        }

        [Test]
        public void Loader_ControlNameOnNonButton_FailsClosed()
        {
            string json = ButtonTemplateJson(extraControls: "{ \"type\": \"label\", \"text\": \"说明\", \"control\": \"notAButton\" },");
            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("only button controls carry a control name"));
        }

        [Test]
        public void Loader_ButtonWithoutLabel_FailsClosed()
        {
            string json = ButtonTemplateJson(buttonExtra: "");
            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("requires a label"));
        }

        // ── Payload baking ──

        [Test]
        public void PayloadBaker_LiteralAndBind_Succeed()
        {
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson(
                payload: "{ \"option\": \"7\" }"));
            PanelLayoutControl button = RequireButton(template);
            var scope = new PanelBindingScope(new PanelVariableSet(template.Id, new Dictionary<string, float>(), revision: 0));

            JsonObject payload = PanelButtonPayloadBaker.Bake(
                template, template.Events[0], button, scope);

            Assert.That(payload["option"]!.GetValue<int>(), Is.EqualTo(7));
        }

        [Test]
        public void PayloadBaker_UnparseableSource_FailsNamed()
        {
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson(
                payload: "{ \"option\": \"notANumberOrBind\" }"));
            PanelLayoutControl button = RequireButton(template);
            var scope = new PanelBindingScope(new PanelVariableSet(template.Id, new Dictionary<string, float>(), revision: 0));

            Assert.That(
                () => PanelButtonPayloadBaker.Bake(template, template.Events[0], button, scope),
                Throws.InvalidOperationException.With.Message.Contains("option"));
        }

        // ── Bridge: admission, injection, release ──

        [Test]
        public void Bridge_AdmittedFire_InjectsPressIntoSeatHandlerThenReleases()
        {
            using SeatHarness harness = SeatHarness.Create(actionId: ChooseActionId);
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson(payload: "{ \"option\": \"1\" }"));
            var bridge = new PanelEventActionBridge(
                harness.Activation,
                () => harness.Runtime,
                () => harness.Seats,
                () => null);
            JsonObject payload = new() { ["option"] = 1 };

            PanelEventFireResult result = bridge.FireFromSeat(template, ChooseActionId, payload, "seat.0");

            Assert.That(result.Admitted, Is.True);
            Assert.That(harness.HandlerZero.IsInjectionActive(ChooseActionId), Is.True,
                "admitted panel event must land as a held synthetic press in the firing seat's channel");
            Assert.That(harness.HandlerOne.IsInjectionActive(ChooseActionId), Is.False,
                "the other seat's channel stays untouched");

            bridge.Update();
            bridge.Update();
            Assert.That(harness.HandlerZero.IsInjectionActive(ChooseActionId), Is.False,
                "the synthetic press is released after the edge could be observed");
        }

        [Test]
        public void Bridge_AudienceRefusal_NoInjectionAndReasonSurfaces()
        {
            using SeatHarness harness = SeatHarness.Create(actionId: ChooseActionId);
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson(
                audience: "[\"seat.1\"]", payload: "{ \"option\": \"1\" }"));
            var bridge = new PanelEventActionBridge(
                harness.Activation,
                () => harness.Runtime,
                () => harness.Seats,
                () => null);
            JsonObject payload = new() { ["option"] = 1 };

            PanelEventFireResult result = bridge.FireFromSeat(template, ChooseActionId, payload, "seat.0");

            Assert.That(result.Admitted, Is.False);
            Assert.That(result.Reason, Does.Contain("seat.0"));
            Assert.That(bridge.LastRefusalReason, Is.EqualTo(result.Reason));
            Assert.That(harness.HandlerZero.IsInjectionActive(ChooseActionId), Is.False,
                "a refused event must never reach the input stream");
        }

        [Test]
        public void Bridge_UndeclaredAction_FailsNamed()
        {
            using SeatHarness harness = SeatHarness.Create(actionId: "Some.OtherAction");
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson(payload: "{ \"option\": \"1\" }"));
            var bridge = new PanelEventActionBridge(
                harness.Activation,
                () => harness.Runtime,
                () => harness.Seats,
                () => null);
            JsonObject payload = new() { ["option"] = 1 };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => bridge.FireFromSeat(template, ChooseActionId, payload, "seat.0"));

            Assert.That(error!.Message, Does.Contain("PANEL.EVENT.ERR.ActionNotDeclared"));
        }

        // ── generic control tooltips ──

        [Test]
        public void Loader_TipLiteralAndBind_ParseAndAttach()
        {
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson(
                tip: "{ \"title\": \"火球术\", \"textBind\": \"title\" }"));
            PanelLayoutControl button = RequireButton(template);
            Assert.That(button.Tip, Is.Not.Null);
            Assert.That(button.Tip!.Title, Is.EqualTo("火球术"));
            Assert.That(button.Tip.TextBind, Is.EqualTo("title"));
        }

        [Test]
        public void Loader_TipWithoutAnyContent_FailsClosed()
        {
            Assert.That(
                () => PanelTemplateLoader.Load(ButtonTemplateJson(tip: "{ }")),
                Throws.InvalidOperationException.With.Message.Contains("at least one of title/text/titleBind/textBind"));
        }

        [Test]
        public void Loader_TipUnknownBind_FailsClosed()
        {
            Assert.That(
                () => PanelTemplateLoader.Load(ButtonTemplateJson(tip: "{ \"titleBind\": \"missing\" }")),
                Throws.InvalidOperationException.With.Message.Contains("tip.titleBind"));
        }

        // ── companion payload events (in-tick dispatch) ──

        [Test]
        public void Bridge_PayloadFire_QueuesCompanionDispatchedOnceInTick()
        {
            using SeatHarness harness = SeatHarness.Create(actionId: ChooseActionId);
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson(payload: "{ \"option\": \"3\" }"));
            var bridge = new PanelEventActionBridge(harness.Activation, () => harness.Runtime, () => harness.Seats, () => null);
            bridge.FireFromSeat(template, ChooseActionId, new JsonObject { ["option"] = 3 }, "seat.0");

            var customEvents = new Ludots.Core.Gameplay.MapTriggers.CustomEventNameRegistry();
            customEvents.Register(PanelEventActionBridge.CompanionEventPrefix + ChooseActionId);
            var system = new PanelEventDispatchSystem(
                () => bridge,
                new Ludots.Core.Scripting.TriggerManager(),
                customEvents,
                () => new Ludots.Core.Map.MapId("tests.map"),
                () => new ScriptContext());

            system.Update(default);
            Assert.That(system.LastDispatched, Is.EqualTo(1), "payload-carrying fire dispatches panel.<eventId> in-tick");
            system.Update(default);
            Assert.That(system.LastDispatched, Is.EqualTo(0), "the queue drains once per fire");
        }

        [Test]
        public void Bridge_CompanionUndeclaredEvent_FailsNamedAtDispatch()
        {
            using SeatHarness harness = SeatHarness.Create(actionId: ChooseActionId);
            PanelTemplate template = PanelTemplateLoader.Load(ButtonTemplateJson(payload: "{ \"option\": \"3\" }"));
            var bridge = new PanelEventActionBridge(harness.Activation, () => harness.Runtime, () => harness.Seats, () => null);
            bridge.FireFromSeat(template, ChooseActionId, new JsonObject { ["option"] = 3 }, "seat.0");

            var system = new PanelEventDispatchSystem(
                () => bridge,
                new Ludots.Core.Scripting.TriggerManager(),
                new Ludots.Core.Gameplay.MapTriggers.CustomEventNameRegistry(),
                () => new Ludots.Core.Map.MapId("tests.map"),
                () => new ScriptContext());

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => system.Update(default));
            Assert.That(error!.Message, Does.Contain("panel." + ChooseActionId));
            Assert.That(error.Message, Does.Contain("not a declared custom event"));
        }

        // ── helpers ──

        private static PanelLayoutControl RequireButton(PanelTemplate template)
        {
            foreach (PanelLayoutControl control in template.Layout!.Controls)
            {
                if (control.Type == PanelLayoutControlType.Button)
                {
                    return control;
                }
            }

            throw new AssertionException("template has no button control");
        }

        private static string ButtonTemplateJson(
            string events = null!,
            string control = "chooseA",
            string eventControl = null!,
            string gesture = "tap",
            string payload = null!,
            string audience = "\"all-seats\"",
            string labelExtra = "",
            string buttonExtra = null!,
            string extraControls = "",
            string tip = null!)
        {
            string payloadJson = payload ?? "{ \"option\": \"1\" }";
            string resolvedEventControl = eventControl ?? control;
            string tipJson = tip == null! ? "" : ", \"tip\": " + tip;
            string eventsJson = events ?? $$"""
            [
              { "eventId": "{{ChooseActionId}}", "control": "{{resolvedEventControl}}", "gesture": "{{gesture}}", "payload": { "option": "Int" } }
            ]
            """;
            string buttonLabel = buttonExtra == null! ? ", \"text\": \"选择\"" : buttonExtra;
            return $$"""
            {
              "id": "{{TemplateId}}",
              "graph": "tests.graph.dialog",
              "audienceSeats": {{audience}},
              "pins": [
                { "name": "title", "key": "tests.panel.dialog.title", "mode": "realtime", "default": 0 }
              ],
              "events": {{eventsJson}},
              "layout": {
                "controls": [
                  {{extraControls}}
                  { "type": "button", "control": "{{control}}"{{buttonLabel}}, "payload": {{payloadJson}}{{labelExtra}}{{tipJson}} }
                ]
              }
            }
            """;
        }

        private sealed class SeatHarness : IDisposable
        {
            private const string SchemeId = "scheme.tests.perseat";
            private const string InputContextId = "imc.tests";

            public World World = null!;
            public ClientLocalSeatRegistry Seats = null!;
            public ClientLocalSeatInputRuntime Runtime = null!;
            public UiPanelActivationStore Activation = null!;
            public PlayerInputHandler HandlerZero = null!;
            public PlayerInputHandler HandlerOne = null!;

            public static SeatHarness Create(string actionId)
            {
                var world = World.Create();
                var harness = new SeatHarness
                {
                    World = world,
                    Activation = new UiPanelActivationStore(),
                };

                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = 2 });

                var inputConfig = new InputConfigRoot
                {
                    Actions = new List<InputActionDef>
                    {
                        new() { Id = actionId, Type = InputActionType.Button },
                    },
                    Contexts = new List<InputContextDef>
                    {
                        new()
                        {
                            Id = InputContextId,
                            Priority = 1,
                            Bindings = new List<InputBindingDef>(),
                        },
                    },
                };

                var schemes = new ControlSchemeRuntime(
                    new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                    orderTypes,
                    inputConfig: inputConfig);
                schemes.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        new() { Id = SchemeId, InputContexts = new List<string> { InputContextId } },
                    },
                });

                harness.Seats = new ClientLocalSeatRegistry();
                var globals = new Dictionary<string, object>
                {
                    [CoreServiceKeys.ClientLocalSeatRegistry.Name] = harness.Seats,
                    [CoreServiceKeys.ControlSchemeRuntime.Name] = schemes,
                };
                harness.Runtime = new ClientLocalSeatInputRuntime(globals, schemes, inputConfig);

                harness.Seats.Add(new ClientLocalSeat("seat.0", SchemeId) { PossessedPlayerId = 7, PossessedRep = world.Create() });
                harness.Seats.Add(new ClientLocalSeat("seat.1", SchemeId) { PossessedPlayerId = 8, PossessedRep = world.Create() });
                harness.Runtime.PublishSeats(harness.Seats);

                harness.HandlerZero = harness.RequireHandler("seat.0");
                harness.HandlerOne = harness.RequireHandler("seat.1");
                return harness;
            }

            private PlayerInputHandler RequireHandler(string seatId)
            {
                Assert.That(Runtime.TryGetChannel(seatId, out ClientLocalSeatInputChannel channel), Is.True,
                    $"seat {seatId} must have a per-seat channel on a multi-seat table");
                return channel.Handler;
            }

            public void Dispose() => World.Dispose();
        }
    }
}
