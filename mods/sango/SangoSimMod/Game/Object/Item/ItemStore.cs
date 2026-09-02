using System;
using System.Collections.Generic;

namespace Sango.Core
{
    public class ItemStore : IAarryDataObject
    {
        public Dictionary<int, int> Items = new Dictionary<int, int>();
        public int TotalNumber { get; private set; }

        public IAarryDataObject FromArray(int[] values)
        {
            TotalNumber = 0;
            Items.Clear();
            if (values == null || values.Length == 0) return this;
            for (int i = 0; i < values.Length; i += 2)
            {
                int storeKindId = values[i];
                int number = values[i + 1];
                TotalNumber += number;
                ItemType itemType = Scenario.Cur.CommonData.ItemTypes.Get(storeKindId);
                if (itemType == null) continue;
                Items.Add(storeKindId, number);
            }
            return this;
        }

        public int[] ToArray()
        {
            List<int> ints = new List<int>();
            foreach (int storeKindId in Items.Keys)
            {
                int number = Items[storeKindId];
                if (number > 0)
                {
                    ints.Add(storeKindId);
                    ints.Add(number);
                }
            }
            return ints.ToArray();
        }

        public ItemStore Copy()
        {
            ItemStore copy = new ItemStore();
            foreach (int storeKindId in Items.Keys)
            {
                int number = Items[storeKindId];
                if (number > 0)
                    copy.Items.Add(storeKindId, number);
            }
            copy.TotalNumber = TotalNumber;
            return copy;
        }

        public int Set(ItemType itemType, int number)
        {
            return Set(itemType.storeKind, number);
        }

        public int Set(int storeKindId, int number)
        {
            if (Items.TryGetValue(storeKindId, out int has))
            {
                TotalNumber -= has;
                Items[storeKindId] = number;
                TotalNumber += number;
                return number;
            }

            TotalNumber += number;
            Items.Add(storeKindId, number);
            return number;
        }

        public int Add(ItemType itemType, int number)
        {
            return Add(itemType.storeKind, number);
        }

        public int Add(int storeKindId, int number)
        {
            if (Items.TryGetValue(storeKindId, out int has))
            {
                has += number;
                Items[storeKindId] = has;
                TotalNumber += number;
                return has;
            }

            TotalNumber += number;
            Items.Add(storeKindId, number);
            return number;
        }


        public void Add(ItemStore itemStore)
        {
            if (itemStore == null) return;
            foreach (int storeKindId in itemStore.Items.Keys)
            {
                int number = itemStore.Items[storeKindId];
                if (number > 0)
                    Add(storeKindId, number);
            }
        }

        public int Remove(ItemType itemType)
        {
            return Remove(itemType.storeKind);
        }

        public int Remove(int storeKindId)
        {
            if (Items.TryGetValue(storeKindId, out int has))
            {
                Items.Remove(storeKindId);
                TotalNumber -= has;
                return has;
            }
            return 0;
        }

        public int Remove(ItemType itemType, int number)
        {
            return Remove(itemType.storeKind, number);
        }

        public int Remove(int storeKindId, int number)
        {
            if (Items.TryGetValue(storeKindId, out int has))
            {
                number = Math.Min(has, number);
                has -= number;
                Items[storeKindId] = has;
                TotalNumber -= number;
                return has;
            }
            return 0;
        }

        public int Remove(ItemStore itemStore)
        {
            if (itemStore == null) return TotalNumber;

            foreach (int storeKindId in itemStore.Items.Keys)
            {
                int number = itemStore.Items[storeKindId];
                if (number > 0)
                    Remove(storeKindId, number);
            }
            return TotalNumber;
        }

        public int GetNumber(int storeKindId)
        {
            if (Items.TryGetValue(storeKindId, out int has))
                return has;
            return 0;
        }

        public int GetNumber(int [] storeKindId)
        {
            int total = 0;
            for(int i = 0; i < storeKindId.Length; i++)
                total += GetNumber(storeKindId[i]);
            return total;
        }

        public int GetNumber(ItemType itemType)
        {
            if (Items.TryGetValue(itemType.storeKind, out int has))
                return has;
            return 0;
        }

        public int this[ItemType itemType]
        {
            get { return GetNumber(itemType.storeKind); }
            set { Add(itemType.storeKind, value); }
        }

        public int this[int storeKindId]
        {
            get { return GetNumber(storeKindId); }
            set { Add(storeKindId, value); }
        }

        public bool CheckItemEnough(int[] cost, int number)
        {
            if (cost == null || cost.Length == 0) return true;
            for (int i = 0; i < cost.Length; i += 2)
            {
                int storeKindId = cost[i];
                int costN = cost[i + 1];
                int have = GetNumber(storeKindId);
                int need = costN * number;
                if (need % 1000 == 0)
                    need = need / 1000;
                else
                    need = need / 1000 + 1;

                if (have < need)
                    return false;
            }
            return true;
        }

        public void Cost(int[] cost, int number)
        {
            if (cost == null || cost.Length == 0) return;
            for (int i = 0; i < cost.Length; i += 2)
            {
                int storeKindId = cost[i];
                int costN = cost[i + 1] * number / 1000;
                Remove(storeKindId, costN);
            }
        }

        public void Gain(int[] cost, int number)
        {
            if (cost == null || cost.Length == 0) return;
            for (int i = 0; i < cost.Length; i += 2)
            {
                int storeKindId = cost[i];
                int costN = cost[i + 1] * number / 1000;
                Add(storeKindId, costN);
            }
        }

        public int CheckCostMin(int[] cost, int number)
        {
            if (cost == null || cost.Length == 0) return number;
            for (int i = 0; i < cost.Length; i += 2)
            {
                int storeKindId = cost[i];
                int costN = cost[i + 1];
                int have = GetNumber(storeKindId) * 1000 / costN;
                number = Math.Min(number, have);
            }
            return number;
        }

        /// <summary>
        /// 分割一部分, part: 1到100的整数
        /// </summary>
        /// <param name="part"></param>
        /// <returns></returns>
        public ItemStore Split(int part, bool keep = false)
        {
            ItemStore itemStore = new ItemStore();
            if (part <= 0) return itemStore;
            if (part > 100) part = 100;
            foreach (int storeKindId in Items.Keys)
            {
                int number = Items[storeKindId];
                if (number > 0)
                {
                    int partNum = number * part / 100;
                    itemStore.Add(storeKindId, partNum);
                }
            }

            if (!keep)
            {
                foreach (int storeKindId in itemStore.Items.Keys)
                {
                    int number = Items[storeKindId];
                    if (number > 0)
                    {
                        Items[storeKindId] = Items[storeKindId] - number;
                        TotalNumber -= number;
                    }
                }
            }
            return itemStore;
        }

        public void Clear()
        {
            Items.Clear();
            TotalNumber = 0;
        }

    }
}
