using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>批量部署的复用缓冲。挂在会话上，重复部署不再 new 临时集合。</summary>
internal sealed class CrowdDeploymentBuffers
{
    public List<CrowdNavGroupSet.Group>[] PerPlayer = [];
    public readonly Dictionary<int, List<int>> Centers = new(32);
    public readonly Dictionary<int, int> TypeOf = new(32);
    private readonly List<List<int>> _centerLists = new(32);
    private int _centerListsRented;

    public int PooledCenterLists => _centerLists.Count;

    /// <summary>开始一次部署。上一次借出的中心列表在这里全部收回，不论是否进了 <see cref="Centers"/>。</summary>
    public void BeginSpawn(int playerCount)
    {
        if (PerPlayer.Length != playerCount)
        {
            PerPlayer = new List<CrowdNavGroupSet.Group>[playerCount];
            for (int p = 0; p < playerCount; p++)
            {
                PerPlayer[p] = new List<CrowdNavGroupSet.Group>(16);
            }
        }
        else
        {
            for (int p = 0; p < playerCount; p++)
            {
                PerPlayer[p].Clear();
            }
        }

        _centerListsRented = 0;
        Centers.Clear();
        TypeOf.Clear();
    }

    public List<int> RentCenterList()
    {
        if (_centerListsRented == _centerLists.Count) _centerLists.Add(new List<int>(8));
        List<int> list = _centerLists[_centerListsRented++];
        list.Clear();
        return list;
    }
}
