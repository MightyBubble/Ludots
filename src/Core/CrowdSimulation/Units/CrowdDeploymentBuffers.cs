using System.Collections.Generic;

namespace Ludots.Core.CrowdSimulation.Units;

/// <summary>批量部署的复用缓冲。挂在会话上，重复部署不再 new 临时集合。</summary>
internal sealed class CrowdDeploymentBuffers
{
    public List<CrowdNavGroupSet.Group>[] PerPlayer = [];
    public readonly Dictionary<int, List<int>> Centers = new(32);
    public readonly Dictionary<int, int> TypeOf = new(32);
    private readonly List<List<int>> _centerListPool = new(32);
    private int _centerListPoolTop;

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

        ReturnCenterLists();
        Centers.Clear();
        TypeOf.Clear();
    }

    public List<int> RentCenterList()
    {
        if (_centerListPoolTop > 0)
        {
            List<int> list = _centerListPool[--_centerListPoolTop];
            list.Clear();
            return list;
        }

        return new List<int>(8);
    }

    private void ReturnCenterLists()
    {
        foreach (List<int> list in Centers.Values)
        {
            _centerListPool.Add(list);
            _centerListPoolTop++;
        }
    }
}
