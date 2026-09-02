using Sango.Render;
using System.Collections.Generic;
using TKNewtonsoft.Json;
using UnityEngine;

namespace Sango.Core
{
    [JsonObject(MemberSerialization.OptIn)]

    public class BuffManager
    {
        [JsonProperty]
        public List<BuffInstance> _buffs = new List<BuffInstance>();

        public class BuffEffectInfo
        {
            public string name;
            public int refCount;
            public Vector3 offset;
            public GameObject instanceObj;

            public void CreateAsset(SangoObject target)
            {
                if (instanceObj != null)
                    return;

                ObjectRender objectRender = target.GetRender();
                if (!objectRender.IsVisible()) return;

                instanceObj = PoolManager.Create(name);
                if (instanceObj != null)
                {
                    instanceObj.transform.SetParent(objectRender.GetTransform(), false);
                    instanceObj.transform.localPosition = offset;
                }
            }

            public void ClearAsset()
            {
                if (instanceObj != null)
                {
                    PoolManager.Recycle(instanceObj);
                    instanceObj = null;
                }
            }

        }

        Dictionary<string, BuffEffectInfo> assetRef = new Dictionary<string, BuffEffectInfo>();

        public Troop Master { get; private set; }

        public void Init(Troop master)
        {
            Master = master;
            foreach (BuffInstance ins in _buffs)
                ins.Init(this, ins.Buff, ins.Master);
        }

        public void OnModelLoaded(GameObject model)
        {
            foreach (BuffEffectInfo info in assetRef.Values)
                info.CreateAsset(Master);
        }

        public void OnModelClear()
        {
            foreach (BuffEffectInfo info in assetRef.Values)
                info.ClearAsset();
        }


        public void AddBuff(int id, int turnCount, Troop srcTroop)
        {
            Buff buff = Scenario.Cur.GetObject<Buff>(id);
            BuffInstance buffInstance = new BuffInstance()
            {
                leftCounter = turnCount,
            };
            buffInstance.Init(this, buff, srcTroop);
            _buffs.Add(buffInstance);
        }

        public void RemoveBuff(int id)
        {
            for (int i = _buffs.Count - 1; i >= 0; i--)
            {
                BuffInstance buff = _buffs[i];
                if (buff.Buff.Id == id)
                {
                    buff.Clear();
                    _buffs.RemoveAt(i);
                }
            }
        }

        public void RemoveBuffByKind(int kind)
        {
            for (int i = _buffs.Count - 1; i >= 0; i--)
            {
                BuffInstance buff = _buffs[i];
                if(buff.Buff.kind == kind)
                {
                    buff.Clear();
                    _buffs.RemoveAt(i);
                }
            }
        }

        public bool HasControlBuff()
        {
            for (int i = 0; i < _buffs.Count; i++)
            {
                BuffInstance buff = _buffs[i];
                if (buff.IsControlBuff())
                    return true;
            }
            return false;
        }

        public void OnForceTurnStart(Scenario scenario)
        {
            for (int i = 0; i < _buffs.Count; i++)
            {
                BuffInstance buff = _buffs[i];
                buff.TurnUpdate();
            }

            _buffs.RemoveAll(x => x.leftCounter < 0);
        }

        public bool HasControlState()
        {
            return false;
        }

        public void CreateAsset(string asset, Vector3 offset)
        {
            if (assetRef.TryGetValue(asset, out BuffEffectInfo refInfo))
            {
                refInfo.refCount++;
                return;
            }
            BuffEffectInfo buffEffectInfo = new BuffEffectInfo()
            {
                name = asset,
                offset = offset,
                refCount = 1
            };
            assetRef[asset] = buffEffectInfo;

            buffEffectInfo.CreateAsset(Master);
        }

        public void ReleaseAsset(string asset)
        {
            if (assetRef.TryGetValue(asset, out BuffEffectInfo refInfo))
            {
                refInfo.refCount--;
                if(refInfo.refCount == 0)
                {
                    refInfo.ClearAsset();
                    assetRef.Remove(asset);
                }
            }
        }
    }
}
