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
        public static string Create(params string[] slotActionIds)
        {
            string root = Path.Combine(Path.GetTempPath(), "ludots-ecp-slot-actions-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "assets", "UI"));
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
                System.Text.Json.JsonSerializer.Serialize(new { slotActionIds }));
            return root;
        }
    }
}
