// M3.c 回灌装载线的 SangoObjectList 序不对称修复:
// 原版列表序列化(SangoObjectListIDConverter/SangoObjectListConverter)WriteJson 一律经
// SangoObjectList.ForEach 倒序写出,ReadJson 正序回填——每次"存→读"把列表的内存序镜像
// 一次。原版 Unity 侧无 bit 级续跑约束,镜像无感;本移植的跨存档边界确定性(实录链 vs
// 回灌链逐回合 digest 逐位相等)要求回灌后的内存序与捕获时点完全一致:Force.Techniques
// 的序进入 Force.Init 的 actionList 构建序与 AITechniques 的遍历序,镜像错位会在若干
// 回合后让随机消耗错序,决策分叉(M3.a 在案的 Replay_MidChainSaveAndLoad 分岔根因)。
// 修法:回灌装载线(Scenario.IsRestoreLoad 窗口内,OnWorldLoaded 顶部——arrayDataCache
// 延迟绑定已在 LoadBaseContent 完成、Prepare/Init 尚未按序重建派生态)对全部对象池成员
// 上带 ForEach 倒序 converter 的 SangoObjectList 原地反转一次,抵消镜像;Boot 装载
// (剧本资产)不走此线,活世界语义零改动。
// 界定:CommonData 侧表驱动对象(Official/Region/Province 等)不经存档 JSON 往返,两链
// 同源同序,不在反转面;城内"不入档"名单(allPersons 等)无 [JsonProperty],由
// SangoCityPersonOrder 按捕获序重放,与本文件判据(成员级倒序 converter)不相交。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Sango.Core;
using TKNewtonsoft.Json;

namespace Sango.Runtime
{
    /// <summary>
    /// 回灌装载线的列表镜像抵消:对七个对象池与 Scenario 自身的一级成员中,带
    /// SangoObjectListIDConverter/SangoObjectListConverter(倒序写出)的 SangoObjectList
    /// 成员做一次原地反转,使回灌世界的列表内存序与捕获时点逐位一致。
    /// </summary>
    public static class SangoRestoreListMirror
    {
        public static void Apply(Scenario scenario)
        {
            ArgumentNullException.ThrowIfNull(scenario);

            UnmirrorDeclaredLists(scenario);
            UnmirrorPool(scenario.forceSet);
            UnmirrorPool(scenario.corpsSet);
            UnmirrorPool(scenario.citySet);
            UnmirrorPool(scenario.personSet);
            UnmirrorPool(scenario.buildingSet);
            UnmirrorPool(scenario.troopsSet);
            UnmirrorPool(scenario.fireSet);
            UnmirrorPool(scenario.allianceSet);
        }

        static void UnmirrorPool<T>(SangoObjectSet<T> pool) where T : SangoObject, new()
        {
            pool.ForEach(obj => UnmirrorDeclaredLists(obj!));
        }

        /// <summary>只看池对象一级成员:嵌套对象(CommonData 共享实例等)不经存档往返,不进反转面。</summary>
        static void UnmirrorDeclaredLists(object target)
        {
            for (Type? type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                const BindingFlags memberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (FieldInfo field in type.GetFields(memberFlags))
                {
                    if (UsesMirroredListConverter(field.GetCustomAttribute<JsonConverterAttribute>(inherit: false)))
                    {
                        ReverseInPlace(field.GetValue(target));
                    }
                }

                foreach (PropertyInfo property in type.GetProperties(memberFlags))
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    {
                        continue;
                    }

                    if (UsesMirroredListConverter(property.GetCustomAttribute<JsonConverterAttribute>(inherit: false)))
                    {
                        ReverseInPlace(property.GetValue(target));
                    }
                }
            }
        }

        static bool UsesMirroredListConverter(JsonConverterAttribute? attribute)
        {
            if (attribute?.ConverterType is not { IsGenericType: true } converterType)
            {
                return false;
            }

            Type definition = converterType.GetGenericTypeDefinition();
            return definition == typeof(SangoObjectListIDConverter<>) || definition == typeof(SangoObjectListConverter<>);
        }

        static void ReverseInPlace(object? value)
        {
            // SangoObjectList<T> 不变式泛型,经公共 objects 背书列表原地交换
            // (List<T> 即 IList,IList 索引写回即原地反转)。
            if (value?.GetType() is not { IsGenericType: true } listType ||
                listType.GetGenericTypeDefinition() != typeof(SangoObjectList<>))
            {
                return;
            }

            if (ObjectsField(listType).GetValue(value) is not IList objects || objects.Count < 2)
            {
                return;
            }

            int left = 0;
            int right = objects.Count - 1;
            while (left < right)
            {
                (objects[left], objects[right]) = (objects[right], objects[left]);
                left++;
                right--;
            }
        }

        static readonly Dictionary<Type, FieldInfo> ObjectsFields = new();

        static FieldInfo ObjectsField(Type listType)
        {
            lock (ObjectsFields)
            {
                if (!ObjectsFields.TryGetValue(listType, out FieldInfo? field))
                {
                    field = listType.GetField("objects", BindingFlags.Instance | BindingFlags.Public)
                        ?? throw new InvalidOperationException(
                            $"SangoObjectList type {listType} lacks the public 'objects' backing list; the restore mirror cannot run.");
                    ObjectsFields[listType] = field;
                }

                return field;
            }
        }
    }
}
