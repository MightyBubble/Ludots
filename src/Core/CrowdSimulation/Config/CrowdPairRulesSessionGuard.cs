using System;
using Arch.Core;
using ArchWorld = Arch.Core.World;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 关系只在会话开始投影。每个 tick 边界先把每个玩家代表的 Team 读进开局分配的暂存：
/// 和开局队伍号不同就报错。修订号变了再重算矩阵，矩阵不同就报错，已投影的表不动。
/// 修订号没变就不重算。成功路径不分配。
/// Team 是这里读的组件：MemberOf 改边会抬修订号，并由 SyncTeam 把队伍号写进 Team；
/// 只改 Team 不抬修订号，所以不能只等修订号。
/// </summary>
public sealed class CrowdPairRulesSessionGuard
{
    private readonly TeamRelationQuery _teamRelations;
    private readonly ArchWorld _world;
    private readonly Entity[] _playerReps;
    private readonly int[] _playerIds;
    private readonly int[] _teamByIndex;
    private readonly int _yieldsTypeId;
    private readonly int _sharesTypeId;
    private readonly CrowdPushResponse[] _response;
    private readonly bool[] _shares;
    private readonly int[] _scratchTeams;
    private readonly CrowdPushResponse[] _scratchResponse;
    private readonly bool[] _scratchShares;
    private uint _revision;

    internal CrowdPairRulesSessionGuard(
        TeamRelationQuery teamRelations,
        ArchWorld world,
        Entity[] playerReps,
        int[] playerIds,
        int[] teamByIndex,
        int yieldsTypeId,
        int sharesTypeId,
        CrowdPairRules projected,
        uint revision)
    {
        _teamRelations = teamRelations;
        _world = world;
        _playerReps = playerReps;
        _playerIds = playerIds;
        _teamByIndex = teamByIndex;
        _yieldsTypeId = yieldsTypeId;
        _sharesTypeId = sharesTypeId;
        _response = projected.ResponseByPair;
        _shares = projected.SharesVisionByPair;
        _scratchTeams = new int[teamByIndex.Length];
        _scratchResponse = new CrowdPushResponse[_response.Length];
        _scratchShares = new bool[_shares.Length];
        _revision = revision;
    }

    public uint Revision => _revision;

    public void CheckAtTickBoundary()
    {
        ReadTeams(_scratchTeams);
        for (int i = 0; i < _scratchTeams.Length; i++)
        {
            if (_scratchTeams[i] != _teamByIndex[i])
            {
                throw new InvalidOperationException(
                    $"群体模拟会话期间玩家 {_playerIds[i]} 的队伍从 {_teamByIndex[i]} 变成 {_scratchTeams[i]}。关系只在会话开始投影。");
            }
        }

        uint revision = _teamRelations.Revision;
        if (revision == _revision)
        {
            return;
        }

        CrowdPairRulesProjection.Fill(
            _scratchTeams,
            _teamRelations,
            _yieldsTypeId,
            _sharesTypeId,
            _scratchResponse,
            _scratchShares);
        if (!Same(_scratchResponse, _response) || !Same(_scratchShares, _shares))
        {
            throw new InvalidOperationException(
                "群体模拟会话期间关系变了，按会话开始时的规则重算后矩阵不同。关系只在会话开始投影。");
        }

        _revision = revision;
    }

    private void ReadTeams(Span<int> destination)
    {
        for (int i = 0; i < _playerReps.Length; i++)
        {
            Entity rep = _playerReps[i];
            destination[i] = _world.IsAlive(rep) && _world.TryGet(rep, out Team team) ? team.Id : 0;
        }
    }

    private static bool Same(CrowdPushResponse[] left, CrowdPushResponse[] right)
    {
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(bool[] left, bool[] right)
    {
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
