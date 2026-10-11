using System;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 关系只在会话开始投影。tick 边界比对修订号：没变就返回；
/// 变了则重算到会话开始时分配的暂存里，矩阵不同就报错，相同则记下新修订号。
/// 已投影矩阵保持不动，热路径不分配。
/// </summary>
public sealed class CrowdPairRulesSessionGuard
{
    private readonly TeamRelationQuery _teamRelations;
    private readonly int _yieldsTypeId;
    private readonly int _sharesTypeId;
    private readonly int[] _teamByIndex;
    private readonly CrowdPushResponse[] _response;
    private readonly bool[] _shares;
    private readonly CrowdPushResponse[] _scratchResponse;
    private readonly bool[] _scratchShares;
    private uint _revision;

    internal CrowdPairRulesSessionGuard(
        TeamRelationQuery teamRelations,
        int yieldsTypeId,
        int sharesTypeId,
        int[] teamByIndex,
        CrowdPairRules projected,
        uint revision)
    {
        _teamRelations = teamRelations;
        _yieldsTypeId = yieldsTypeId;
        _sharesTypeId = sharesTypeId;
        _teamByIndex = teamByIndex;
        _response = projected.ResponseByPair;
        _shares = projected.SharesVisionByPair;
        _scratchResponse = new CrowdPushResponse[_response.Length];
        _scratchShares = new bool[_shares.Length];
        _revision = revision;
    }

    public uint Revision => _revision;

    public void CheckAtTickBoundary()
    {
        uint revision = _teamRelations.Revision;
        if (revision == _revision)
        {
            return;
        }

        CrowdPairRulesProjection.Fill(
            _teamByIndex,
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
