using Arch.Core;
using Ludots.Core.Components;
using FormationCapabilityShowcaseMod.Runtime;

namespace FormationCapabilityShowcaseMod.Systems;

/// <summary>
/// A selected formation anchor stands for its live soldiers in slot order; any other selected
/// entity stands for itself.
/// </summary>
internal sealed class FormationMemberResolver
{
    private static readonly QueryDescription MembersQuery = new QueryDescription()
        .WithAll<FormationMemberState>()
        .WithNone<SuspendedTag>();

    private readonly World _world;

    public FormationMemberResolver(World world, int maxMembersPerFormation)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        if (maxMembersPerFormation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMembersPerFormation));
        }

        MaxMembersPerFormation = maxMembersPerFormation;
    }

    public int MaxMembersPerFormation { get; }

    public int Resolve(Entity source, Span<Entity> destination)
    {
        if (!_world.IsAlive(source) || !_world.TryGet(source, out FormationAnchorState anchor))
        {
            destination[0] = source;
            return 1;
        }

        if (anchor.SlotCount <= 0 || anchor.SlotCount > MaxMembersPerFormation)
        {
            throw new InvalidOperationException(
                $"Formation {anchor.FormationIndex} declares {anchor.SlotCount} slots, outside member capacity {MaxMembersPerFormation}.");
        }

        if (destination.Length < anchor.SlotCount)
        {
            throw new InvalidOperationException(
                $"Formation {anchor.FormationIndex} requires {anchor.SlotCount} member slots, but destination capacity is {destination.Length}.");
        }

        Span<Entity> slots = destination.Slice(0, anchor.SlotCount);
        slots.Fill(Entity.Null);
        int resolved = 0;
        foreach (ref var chunk in _world.Query(in MembersQuery))
        {
            Span<FormationMemberState> members = chunk.GetSpan<FormationMemberState>();
            ref Entity entityFirst = ref chunk.Entity(0);
            foreach (int index in chunk)
            {
                if (members[index].FormationIndex != anchor.FormationIndex)
                {
                    continue;
                }

                int slotIndex = members[index].SlotIndex;
                if ((uint)slotIndex >= (uint)anchor.SlotCount)
                {
                    throw new InvalidOperationException(
                        $"Formation {anchor.FormationIndex} member slot {slotIndex} exceeds the anchor-declared slot count {anchor.SlotCount}.");
                }

                if (slots[slotIndex] != Entity.Null)
                {
                    throw new InvalidOperationException(
                        $"Formation {anchor.FormationIndex} has duplicate live member slot {slotIndex}.");
                }

                slots[slotIndex] = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                resolved++;
            }
        }

        int written = 0;
        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            Entity member = slots[slotIndex];
            if (member != Entity.Null)
            {
                slots[written++] = member;
            }
        }

        slots.Slice(written).Fill(Entity.Null);
        return resolved;
    }
}
