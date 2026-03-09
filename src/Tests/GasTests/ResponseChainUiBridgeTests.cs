using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Primitives;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ResponseChainUiBridgeTests
    {
        [Test]
        public void ResponseChainDirectorSystem_AppliesQueuedPrompt_AndClosesOnWindowClosedTelemetry()
        {
            using var world = World.Create();

            var actor = world.Create();
            var target = world.Create();
            var orderRequests = new OrderRequestQueue();
            var telemetry = new ResponseChainTelemetryBuffer();
            var ui = new ResponseChainUiState();
            var commands = new PresentationCommandBuffer();
            var prefabs = new PrefabRegistry();
            prefabs.Register(WellKnownPrefabKeys.CueMarker, default);

            var request = new OrderRequest
            {
                RequestId = 7,
                PromptTagId = 99,
                PlayerId = 1,
                Actor = actor,
                Target = target,
                TargetContext = default,
                AllowedCount = 0
            };
            request.AddAllowed(1);
            request.AddAllowed(2);
            orderRequests.TryEnqueue(request);

            var system = new ResponseChainDirectorSystem(world, orderRequests, telemetry, ui, commands, prefabs);
            system.Update(0f);

            That(ui.Visible, Is.True);
            That(ui.RootId, Is.EqualTo(7));
            That(ui.PlayerId, Is.EqualTo(1));
            That(ui.PromptTagId, Is.EqualTo(99));
            That(ui.Actor, Is.EqualTo(actor));
            That(ui.Target, Is.EqualTo(target));
            That(ui.AllowedCount, Is.EqualTo(2));

            telemetry.TryAdd(new ResponseChainTelemetryEvent
            {
                Kind = ResponseChainTelemetryKind.WindowClosed,
                RootId = 7
            });

            system.Update(0f);

            That(ui.Visible, Is.False);
        }

        [Test]
        public void ResponseChainHumanOrderSourceSystem_UsesSharedInteractionBindings()
        {
            using var world = World.Create();

            var input = new PlayerInputHandler(new NullInputBackend(), CreateInputConfig());
            var globals = new Dictionary<string, object>
            {
                [Ludots.Core.Scripting.CoreServiceKeys.InputHandler.Name] = input,
                [Ludots.Core.Scripting.CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings
                {
                    ResponseChainPassActionId = "Pass",
                    ResponseChainNegateActionId = "Negate",
                    ResponseChainActivateActionId = "Activate",
                },
            };

            var actor = world.Create();
            var target = world.Create();
            var ui = new ResponseChainUiState();
            var request = new OrderRequest
            {
                RequestId = 1,
                PromptTagId = 321,
                PlayerId = 1,
                Actor = actor,
                Target = target,
                TargetContext = default,
                AllowedCount = 0
            };
            request.AddAllowed(TestResponseChainOrderTypeIds.ChainPass);
            request.AddAllowed(TestResponseChainOrderTypeIds.ChainNegate);
            request.AddAllowed(TestResponseChainOrderTypeIds.ChainActivateEffect);
            ui.ApplyRequest(request);

            var chainOrders = new OrderQueue();
            var system = new ResponseChainHumanOrderSourceSystem(globals, ui, chainOrders);

            input.InjectButtonPress("Pass");
            input.Update();
            system.Update(0f);
            That(chainOrders.TryDequeue(out var pass), Is.True);
            That(pass.OrderTypeId, Is.EqualTo(TestResponseChainOrderTypeIds.ChainPass));
            That(pass.Actor, Is.EqualTo(actor));
            input.Update();

            input.InjectButtonPress("Negate");
            input.Update();
            system.Update(0f);
            That(chainOrders.TryDequeue(out var negate), Is.True);
            That(negate.OrderTypeId, Is.EqualTo(TestResponseChainOrderTypeIds.ChainNegate));
            input.Update();

            input.InjectButtonPress("Activate");
            input.Update();
            system.Update(0f);
            That(chainOrders.TryDequeue(out var activate), Is.True);
            That(activate.OrderTypeId, Is.EqualTo(TestResponseChainOrderTypeIds.ChainActivateEffect));
            That(activate.Args.I0, Is.EqualTo(321));
        }

        private static InputConfigRoot CreateInputConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Pass", Name = "Pass", Type = InputActionType.Button },
                    new() { Id = "Negate", Name = "Negate", Type = InputActionType.Button },
                    new() { Id = "Activate", Name = "Activate", Type = InputActionType.Button },
                }
            };
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
