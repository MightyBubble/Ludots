using System;
using Arch.Core;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.CrowdSimulation.Presentation;

public static class CrowdPlayerSelection
{
    public static void Clear(CrowdSimSession session, int playerId)
    {
        (CollectionApplier applier, Entity rep, int keyId) = Writer(session, playerId);
        applier.Apply(rep, keyId, CollectionWriteOp.Replace, ReadOnlySpan<Entity>.Empty);
    }

    public static void ClearForSwitch(CrowdSimSession session, int fromPlayerId, int toPlayerId)
    {
        Clear(session, fromPlayerId);
        if (toPlayerId != fromPlayerId)
        {
            Clear(session, toPlayerId);
        }
    }

    /// <summary>只收入当前层过滤还看得见的本方单位。已经选中的成员不在这里重滤。</summary>
    public static void SelectVisible(CrowdSimSession session, int playerId, Span<Entity> destination)
    {
        (CollectionApplier applier, Entity rep, int keyId) = Writer(session, playerId);
        int hiddenTag = session.Presentation.HiddenTemplateTagId;
        int n = 0;
        int count = session.Units.Count;
        for (int i = 0; i < count; i++)
        {
            Entity entity = session.Units.EntityAt(i);
            if (session.World.Get<PlayerOwner>(entity).PlayerId != playerId)
            {
                continue;
            }

            if (hiddenTag > 0 &&
                session.World.Has<GameplayTagContainer>(entity) &&
                session.World.Get<GameplayTagContainer>(entity).HasTag(hiddenTag))
            {
                continue;
            }

            if (n >= destination.Length)
            {
                throw new InvalidOperationException("order: 成员缓冲装不下本方单位。");
            }

            destination[n++] = entity;
        }

        applier.Apply(rep, keyId, CollectionWriteOp.Replace, destination[..n]);
    }

    private static (CollectionApplier Applier, Entity Rep, int KeyId) Writer(CrowdSimSession session, int playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        CrowdSimPresentationWiring wiring = session.Presentation;
        CollectionApplier applier = wiring.CollectionApplier
            ?? throw new InvalidOperationException("选中集合没有写者。");
        Entity[] reps = wiring.PlayerReps
            ?? throw new InvalidOperationException("会话没有玩家代表。");
        if ((uint)playerId >= (uint)reps.Length)
        {
            throw new InvalidOperationException($"玩家 {playerId} 没有代表。");
        }

        Entity rep = reps[playerId];
        if (rep == Entity.Null || !session.World.IsAlive(rep))
        {
            throw new InvalidOperationException($"玩家 {playerId} 的代表不存在。");
        }

        return (applier, rep, wiring.SelectedCollectionKeyId);
    }
}
