using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    /// <summary>
    /// #620: ImmediateCommand path for selected-actor attribute set/add via AttributeMutationOps.
    /// Fail-closed: missing selection, dead entity, missing buffer, unknown attribute → throw.
    /// </summary>
    public sealed class LiveAttributeCommandExecutor : ILiveAttributeCommandSink
    {
        private readonly World _world;
        private readonly TagOps _tagOps;
        private Entity _selected;

        public LiveAttributeCommandExecutor(World world, TagOps tagOps)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            _selected = Entity.Null;
        }

        public Entity SelectedEntity => _selected;

        public void SetSelectedEntity(Entity entity)
        {
            if (!_world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    "Live attribute command requires a live selected entity.");
            }

            _selected = entity;
        }

        public void ClearSelection() => _selected = Entity.Null;

        public void Apply(in LiveDebugPatchOperation operation)
        {
            if (operation.Kind != LiveDebugPatchOperationKind.SelectedActorAttribute)
            {
                throw new InvalidOperationException(
                    $"LiveAttributeCommandExecutor only accepts SelectedActorAttribute, got '{operation.Kind}'.");
            }

            Entity target = ResolveTarget(in operation);
            if (!_world.IsAlive(target))
            {
                throw new InvalidOperationException(
                    "Selected actor attribute edit rejected: target entity is not alive.");
            }

            if (!_world.Has<AttributeBuffer>(target))
            {
                throw new InvalidOperationException(
                    $"Selected actor attribute edit rejected: entity {target.Id} has no AttributeBuffer.");
            }

            string attributeName = operation.AttributeName
                ?? throw new InvalidOperationException("Attribute name is required.");
            int attributeId = AttributeRegistry.GetId(attributeName);
            if (attributeId == AttributeRegistry.InvalidId)
            {
                throw new InvalidOperationException(
                    $"Unknown attribute '{attributeName}' for ImmediateCommand (not registered).");
            }

            float value = (float)operation.NumericValue;
            switch (operation.AttributeMutation)
            {
                case ActorAttributeMutationKind.Set:
                    AttributeMutationOps.SetCurrent(_world, target, attributeId, value, _tagOps);
                    break;
                case ActorAttributeMutationKind.Add:
                    AttributeMutationOps.AddCurrent(_world, target, attributeId, value, _tagOps);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported attribute mutation '{operation.AttributeMutation}'.");
            }
        }

        private Entity ResolveTarget(in LiveDebugPatchOperation operation)
        {
            if (operation.ActorTarget.EntityIdSurrogate is int surrogate)
            {
                // Showcase/tests may pin a surrogate; prefer explicit selection when set.
                if (_selected != Entity.Null && _selected.Id == surrogate)
                {
                    return _selected;
                }

                if (_selected != Entity.Null)
                {
                    return _selected;
                }

                throw new InvalidOperationException(
                    $"Entity id surrogate {surrogate} was provided but no selected entity is bound in the executor.");
            }

            if (_selected == Entity.Null)
            {
                throw new InvalidOperationException(
                    "Selected actor attribute edit rejected: no selection (ambiguous/empty).");
            }

            return _selected;
        }
    }
}
