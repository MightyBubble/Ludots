using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Input.Interaction;
using FormationCapabilityShowcaseMod.Runtime;

namespace FormationCapabilityShowcaseMod.Systems;

internal sealed class FormationCommandActorExpander : ICommandActorExpander
{
    private static readonly QueryDescription MembersQuery = new QueryDescription()
        .WithAll<FormationMemberState>()
        .WithNone<SuspendedTag>();

    private readonly World _world;

    public FormationCommandActorExpander(
        World world,
        int maxMembersPerFormation,
        int maxExpandedActorCount)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        if (maxMembersPerFormation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMembersPerFormation));
        }

        MaxExpandedActorsPerSource = maxMembersPerFormation;
        if (maxExpandedActorCount < maxMembersPerFormation)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExpandedActorCount));
        }

        MaxExpandedActorCount = maxExpandedActorCount;
    }

    public int MaxExpandedActorsPerSource { get; }
    public int MaxExpandedActorCount { get; }

    public int Expand(Entity source, Span<Entity> destination)
    {
        if (!_world.IsAlive(source) || !_world.TryGet(source, out FormationAnchorState anchor))
        {
            destination[0] = source;
            return 1;
        }

        destination.Fill(Entity.Null);
        if (anchor.SlotCount <= 0 || anchor.SlotCount > MaxExpandedActorsPerSource)
        {
            throw new InvalidOperationException(
                $"Formation {anchor.FormationIndex} declares {anchor.SlotCount} slots, outside command expansion capacity {MaxExpandedActorsPerSource}.");
        }

        if (destination.Length < anchor.SlotCount)
        {
            throw new InvalidOperationException(
                $"Formation {anchor.FormationIndex} requires {anchor.SlotCount} command slots, but destination capacity is {destination.Length}.");
        }

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

                if (destination[slotIndex] != Entity.Null)
                {
                    throw new InvalidOperationException(
                        $"Formation {anchor.FormationIndex} has duplicate live member slot {slotIndex}.");
                }

                destination[slotIndex] = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                resolved++;
            }
        }

        if (resolved <= 0)
        {
            throw new InvalidOperationException(
                $"Formation {anchor.FormationIndex} has no live command members.");
        }

        int written = 0;
        for (int slotIndex = 0; slotIndex < destination.Length; slotIndex++)
        {
            Entity member = destination[slotIndex];
            if (member != Entity.Null)
            {
                destination[written++] = member;
            }
        }

        destination.Slice(written).Fill(Entity.Null);
        return written;
    }
}
