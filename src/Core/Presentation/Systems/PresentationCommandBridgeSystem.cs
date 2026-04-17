using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Perform;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Transfers performer commands still authored through PresentationCommandBuffer
    /// into the perform-domain command buffer consumed by PerformerRuntimeSystem.
    /// </summary>
    public sealed class PresentationCommandBridgeSystem : BaseSystem<World, float>
    {
        private readonly PresentationCommandBuffer _presentationCommands;
        private readonly PerformCommandBuffer _performCommands;
        private readonly PrefabRegistry _prefabs;
        private readonly TransientMarkerBuffer _markers;

        public PresentationCommandBridgeSystem(
            World world,
            PresentationCommandBuffer presentationCommands,
            PerformCommandBuffer performCommands,
            PrefabRegistry prefabs,
            TransientMarkerBuffer markers)
            : base(world)
        {
            _presentationCommands = presentationCommands ?? throw new ArgumentNullException(nameof(presentationCommands));
            _performCommands = performCommands ?? throw new ArgumentNullException(nameof(performCommands));
            _prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
            _markers = markers ?? throw new ArgumentNullException(nameof(markers));
        }

        public override void Update(in float dt)
        {
            ReadOnlySpan<PresentationCommand> span = _presentationCommands.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationCommand command = ref span[i];
                switch (command.Kind)
                {
                    case PresentationCommandKind.None:
                        break;

                    case PresentationCommandKind.PlayOneShotPerformer:
                        HandlePlayOneShot(in command);
                        break;

                    case PresentationCommandKind.CreatePerformer:
                    case PresentationCommandKind.DestroyPerformer:
                    case PresentationCommandKind.DestroyPerformerScope:
                    case PresentationCommandKind.SetPerformerParam:
                        TransferPerformerCommand(in command);
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown PresentationCommandKind '{command.Kind}'.");
                }
            }

            _presentationCommands.Clear();
        }

        private void TransferPerformerCommand(in PresentationCommand command)
        {
            var perform = new PerformCommand
            {
                CommandKind = command.Kind,
                PerformerDefinitionId = command.IdA,
                PerformerHandle = command.IdA,
                ScopeId = ResolveScopeId(in command),
                ScopeSource = PerformerCommandScopeSource.Fixed,
                AnchorKind = command.AnchorKind,
                Source = command.Source,
                Target = command.Target,
                Position = command.Position,
                ParamKey = command.IdB,
                ParamValue = command.Param1,
                ParamGraphProgramId = 0,
            };

            if (!_performCommands.TryAdd(in perform))
            {
                throw new InvalidOperationException(
                    $"PerformCommandBuffer overflowed while bridging PresentationCommand kind={command.Kind} idA={command.IdA} idB={command.IdB}.");
            }
        }

        private static int ResolveScopeId(in PresentationCommand command)
        {
            return command.Kind == PresentationCommandKind.DestroyPerformerScope && command.IdB == 0
                ? command.IdA
                : command.IdB;
        }

        private void HandlePlayOneShot(in PresentationCommand command)
        {
            if (!_prefabs.TryGet(command.IdA, out PrefabDefinition prefab))
            {
                throw new InvalidOperationException($"PlayOneShotPerformer references unknown prefab id={command.IdA}.");
            }

            Vector4 color = command.Param0.W == 0f ? new Vector4(0f, 1f, 1f, 1f) : command.Param0;
            float lifetime = command.Param1 > 0f ? command.Param1 : 0.35f;
            var scale = new Vector3(prefab.BaseScale);

            bool follow = World.IsAlive(command.Target) && World.Has<VisualTransform>(command.Target);
            bool added = follow
                ? _markers.TryAddAnchoredPrefab(command.IdA, scale, color, lifetime, command.Target, new Vector3(0f, 0.2f, 0f))
                : _markers.TryAddPrefab(command.IdA, command.Position, scale, color, lifetime);

            if (!added)
            {
                throw new InvalidOperationException("TransientMarkerBuffer is full while bridging PlayOneShotPerformer.");
            }
        }
    }
}
