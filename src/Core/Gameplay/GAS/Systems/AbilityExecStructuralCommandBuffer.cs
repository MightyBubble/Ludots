using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    internal sealed class AbilityExecStructuralCommandBuffer
    {
        private const byte AddCommand = 1;
        private const byte RemoveCommand = 2;

        private readonly Entity[] _entities;
        private readonly AbilityExecInstance[] _instances;
        private readonly byte[] _commands;

        public AbilityExecStructuralCommandBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _entities = new Entity[capacity];
            _instances = new AbilityExecInstance[capacity];
            _commands = new byte[capacity];
        }

        public int Count { get; private set; }

        public int Capacity => _entities.Length;

        public void Add(Entity entity, in AbilityExecInstance instance)
        {
            EnsureCapacity(1);
            int index = Count++;
            _entities[index] = entity;
            _instances[index] = instance;
            _commands[index] = AddCommand;
        }

        public void Remove(Entity entity)
        {
            EnsureCapacity(1);
            int index = Count++;
            _entities[index] = entity;
            _instances[index] = default;
            _commands[index] = RemoveCommand;
        }

        public void Playback(World world)
        {
            if (Count == 0)
            {
                return;
            }

            for (int i = 0; i < Count; i++)
            {
                Entity entity = _entities[i];
                if (!world.IsAlive(entity))
                {
                    throw new InvalidOperationException(
                        $"{AbilityExecSystem.StructuralCommandTargetDeadError}: entity={entity.Id}.");
                }

                switch (_commands[i])
                {
                    case AddCommand:
                        if (world.Has<AbilityExecInstance>(entity))
                        {
                            throw new InvalidOperationException(
                                $"{AbilityExecSystem.StructuralCommandDuplicateAddError}: entity={entity.Id}.");
                        }

                        world.Add(entity, in _instances[i]);
                        break;
                    case RemoveCommand:
                        if (!world.Has<AbilityExecInstance>(entity))
                        {
                            throw new InvalidOperationException(
                                $"{AbilityExecSystem.StructuralCommandMissingRemoveError}: entity={entity.Id}.");
                        }

                        world.Remove<AbilityExecInstance>(entity);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"GAS.ABILITY_EXEC.ERR.UnknownStructuralCommand: command={_commands[i]}, index={i}.");
                }
            }

            Count = 0;
        }

        private void EnsureCapacity(int required)
        {
            if (Count > Capacity - required)
            {
                throw new InvalidOperationException(
                    $"{AbilityExecSystem.StructuralCommandCapacityExceededError}: required={Count + required}, capacity={Capacity}.");
            }
        }
    }
}
