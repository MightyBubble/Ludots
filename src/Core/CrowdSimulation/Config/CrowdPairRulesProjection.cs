using System;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 会话激活时把 pairRules 点名的关系类型投影成玩家对矩阵。
/// 查询只走 TeamRelationQuery：有边是 Yield / 共享，没边是 Hold / 不共享。
/// 不补同玩家、同队的暗规则。
/// </summary>
public static class CrowdPairRulesProjection
{
    public static CrowdPairProjection Project(CrowdSimulationConfig config, MapConfig map, TeamRelationQuery teamRelations)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(teamRelations);

        int yieldsTypeId = RequireRelationType(teamRelations.Types, "pairRules.yieldsTo.relation", config.PairRules.YieldsTo.Relation);
        int sharesTypeId = RequireRelationType(teamRelations.Types, "pairRules.sharesVisionWith.relation", config.PairRules.SharesVisionWith.Relation);
        RequireDeployedPlayersHaveTeams(config, map);

        int playerCount = map.Players.Count;
        var teamByIndex = new int[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            teamByIndex[i] = map.Players[i].TeamId;
        }

        var response = new CrowdPushResponse[playerCount * playerCount];
        var shares = new bool[playerCount * playerCount];
        uint revision = teamRelations.Revision;
        Fill(teamByIndex, teamRelations, yieldsTypeId, sharesTypeId, response, shares);
        if (teamRelations.Revision != revision)
        {
            throw new InvalidOperationException(
                "群体模拟投影关系时修订号变了。关系只在会话开始投影，投影过程中不能改边。");
        }

        var rules = CrowdPairRules.Matrices(playerCount, response, shares);
        rules.BindPlayerIndex(map);
        return new CrowdPairProjection(
            rules,
            new CrowdPairRulesSessionGuard(teamRelations, yieldsTypeId, sharesTypeId, teamByIndex, rules, revision));
    }

    public static int RequireRelationType(RelationshipTypeRegistry types, string path, string relationId)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (string.IsNullOrWhiteSpace(relationId) || !types.TryGetId(relationId, out int typeId))
        {
            throw new InvalidOperationException(
                $"CrowdSimulationConfig.json: {path} \"{relationId}\" 不是已登记的关系类型。");
        }

        return typeId;
    }

    /// <summary>写入调用方提供的矩阵。热路径检查用会话开始时分配的暂存，这里不再分配。</summary>
    public static void Fill(
        ReadOnlySpan<int> teamByIndex,
        TeamRelationQuery teamRelations,
        int yieldsTypeId,
        int sharesTypeId,
        Span<CrowdPushResponse> response,
        Span<bool> shares)
    {
        int playerCount = teamByIndex.Length;
        for (int a = 0; a < playerCount; a++)
        {
            for (int b = 0; b < playerCount; b++)
            {
                int cell = a * playerCount + b;
                response[cell] = teamRelations.Has(teamByIndex[a], teamByIndex[b], yieldsTypeId)
                    ? CrowdPushResponse.Yield
                    : CrowdPushResponse.Hold;
                shares[cell] = teamRelations.Has(teamByIndex[a], teamByIndex[b], sharesTypeId);
            }
        }
    }

    private static void RequireDeployedPlayersHaveTeams(CrowdSimulationConfig config, MapConfig map)
    {
        var teamByPlayerId = new System.Collections.Generic.Dictionary<int, int>(map.Players.Count);
        foreach (var player in map.Players)
        {
            teamByPlayerId[player.PlayerId] = player.TeamId;
        }

        foreach (var basis in config.Deploy.Bases)
        {
            if (!teamByPlayerId.TryGetValue(basis.PlayerId, out int teamId) || teamId > 0)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Maps/{map.Id}.json: 玩家 {basis.PlayerId} 出现在 deploy.bases，TeamId = {teamId}。有群体单位的玩家必须有队伍（TeamId > 0）。");
        }
    }
}

public sealed class CrowdPairProjection
{
    public CrowdPairProjection(CrowdPairRules rules, CrowdPairRulesSessionGuard guard)
    {
        Rules = rules;
        Guard = guard;
    }

    public CrowdPairRules Rules { get; }
    public CrowdPairRulesSessionGuard Guard { get; }
}
