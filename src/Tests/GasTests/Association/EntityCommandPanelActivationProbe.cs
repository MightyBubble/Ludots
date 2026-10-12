using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using EntityCommandPanelMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Tests.GAS
{
    internal sealed class EntityCommandPanelActivationProbe
    {
        private readonly GameEngine _engine;
        private readonly Entity _owner;

        private EntityCommandPanelActivationProbe(GameEngine engine, Entity owner)
        {
            _engine = engine;
            _owner = owner;
        }

        public List<(int Slot, Entity[] Members)> Fired { get; } = new();

        public static EntityCommandPanelActivationProbe Attach(GameEngine engine, Entity owner)
        {
            engine.Start();
            if (engine.CurrentMapSession == null)
            {
                engine.LoadMap(MapLoadRequest.FromMapId("entry", new MapLaunchContext()));
            }

            var probe = new EntityCommandPanelActivationProbe(engine, owner);
            engine.TriggerManager.RegisterEventHandler(
                new EventKey(EntityCommandPanelSlotActivations.EventName),
                context =>
                {
                    probe.Record(context);
                    return Task.CompletedTask;
                });
            return probe;
        }

        public void Dispatch()
        {
            int targetTick = _engine.GameSession.CurrentTick + 2;
            for (int frame = 0; frame < 120 && _engine.GameSession.CurrentTick < targetTick; frame++)
            {
                _engine.Tick(1f / 60f);
            }

            if (_engine.GameSession.CurrentTick < targetTick)
            {
                throw new InvalidOperationException("Simulation did not advance while dispatching panel activations.");
            }
        }

        private void Record(ScriptContext context)
        {
            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            var buffer = new Entity[64];
            int count = collections.CopyEntities(_owner, EntityCommandPanelSlotActivations.MembersCollectionKey, buffer);
            Fired.Add((context.Get<int>(EntityCommandPanelSlotActivations.SlotPayloadKey), buffer[..count]));
        }
    }

    internal static class EntityCommandPanelSlotActionsTestMod
    {
        public static string Create(params string[] actionIds)
        {
            string root = Path.Combine(Path.GetTempPath(), "ludots-ecp-slot-actions-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "assets", "UI"));
            Directory.CreateDirectory(Path.Combine(root, "assets", "Input"));
            File.WriteAllText(
                Path.Combine(root, "mod.json"),
                """
                {
                  "name": "EntityCommandPanelSlotActionsTestMod",
                  "version": "1.0.0",
                  "priority": 500,
                  "dependencies": { "EntityCommandPanelMod": "^1.0.0" }
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "assets", "UI", "entity_command_panel_slot_actions.json"),
                WriteSlotConfig(actionIds));
            File.WriteAllText(
                Path.Combine(root, "assets", "Input", "default_input.json"),
                WriteInputConfig(actionIds));
            return root;
        }

        private static string WriteSlotConfig(string[] actionIds)
        {
            var slots = new System.Text.StringBuilder();
            slots.Append("{\"slots\":[");
            for (int i = 0; i < actionIds.Length; i++)
            {
                if (i > 0)
                {
                    slots.Append(',');
                }

                slots.Append("{\"action\":\"").Append(actionIds[i]).Append("\"}");
            }

            slots.Append("]}");
            return slots.ToString();
        }

        private static string WriteInputConfig(string[] actionIds)
        {
            const string keys = "qwertyuiop";
            var json = new System.Text.StringBuilder();
            json.Append("{\"actions\":[");
            for (int i = 0; i < actionIds.Length; i++)
            {
                if (i > 0)
                {
                    json.Append(',');
                }

                json.Append("{\"id\":\"").Append(actionIds[i]).Append("\",\"name\":\"").Append(actionIds[i]).Append("\",\"type\":\"Button\"}");
            }

            json.Append("],\"contexts\":[{\"id\":\"EntityCommandPanelSlotActionsTest.Controls\",\"name\":\"Slot actions test\",\"priority\":10,\"bindings\":[");
            for (int i = 0; i < actionIds.Length; i++)
            {
                if (i >= keys.Length)
                {
                    throw new InvalidOperationException($"Slot action test mod supports at most {keys.Length} actions.");
                }

                if (i > 0)
                {
                    json.Append(',');
                }

                json.Append("{\"actionId\":\"").Append(actionIds[i]).Append("\",\"path\":\"<Keyboard>/").Append(keys[i]).Append("\"}");
            }

            json.Append("]}]}");
            return json.ToString();
        }
    }
}
