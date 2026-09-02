using System;
using System.Reflection;

namespace Sango.Core
{
    public static class GameRandom
    {
        static Random random;

        public static void Init()
        {
            random = new Random(Guid.NewGuid().GetHashCode());
        }

        // PLAN D7:移植线随机必须可种子化(确定性重放的根基);内核内禁止绕行此入口的环境随机。
        public static void Init(int seed)
        {
            random = new Random(seed);
        }
        public static double Random()
        {
            return random.NextDouble();
        }

        /// <summary>
        /// 给定一个基础数值,随机一定浮动比例
        /// </summary>
        /// <param name="baseV"></param>
        /// <param name="floatV"></param>
        /// <returns></returns>
        public static int Random(int baseV, float floatP)
        {
            if (baseV <= 0) return 0;
            int b = baseV;
            if (floatP < 1.0f)
                b = (int)(baseV * (1.0f - floatP));
            return b + Range((int)(baseV * floatP) * 2);
        }

        /// <summary>
        /// 随机一个概率1-99
        /// </summary>
        /// <param name="percent"></param>
        /// <returns></returns>
        public static bool Chance(int chance)
        {
            return Chance(chance, 100);
        }

        public static bool Chance(int chance, int root)
        {
            if (chance >= root) return true;
            else if (chance <= 0) return false; 
            else
            {
                int rs = random.Next(root);
                return rs < chance;
            }
        }

        /// <summary>
        /// 返回指定范围内的随机整数（包含起始值，不包含结束值）
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static int Range(int min, int max)
        {
            if (min > max)
                return random.Next(max, min);
            else
                return random.Next(min, max);
        }
        public static int Range(int maxValue)
        {
            return random.Next(maxValue);
        }

        public static float Range(float min, float max)
        {
            if (min > max)
                return (float)random.Next((int)max * 10000, (int)min * 10000) / 10000f;
            else
                return (float)random.Next((int)min * 10000, (int)max * 10000) / 10000f;
        }
        public static float Range(float maxValue)
        {
            return (float)random.Next((int)maxValue * 10000) / 10000f;
        }

        public static int RandomGaussian(double mean, double var)
        {
            double u1 = Random();
            double u2 = Random();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                         Math.Sin(2.0 * Math.PI * u2);
            return (int)Math.Round(mean + (var / 3) * randStdNormal);
        }
        public static int RandomGaussianRange(int lo, int hi)
        {
            return RandomGaussian((hi + lo) / 2.0, Math.Abs(hi - lo) / 2.0);
        }

        public static int RandomWeightIndex(int[] weightValue, int maxValue)
        {
            int v = Range(maxValue);
            for (int i = 0; i < weightValue.Length; i++)
            {
                if (v > weightValue[i])
                {
                    v -= weightValue[i];
                    continue;
                }
                else
                    return i;
            }
            return weightValue.Length - 1;
        }

        public static int RandomWeightIndex(int[] weightValue)
        {
            int maxValue = weightValue[0];
            for(int i = 1; i < weightValue.Length; i++)
                maxValue = maxValue + weightValue[i];

            int v = Range(maxValue);
            for (int i = 0; i < weightValue.Length; i++)
            {
                if (v > weightValue[i])
                {
                    v -= weightValue[i];
                    continue;
                }
                else
                    return i;
            }
            return weightValue.Length - 1;
        }

        // ---- 存档域状态导出/导入(M1.d,D7)----
        // System.Random 无公开状态存取 API;本类只经 Init(seed) 构造,种子化实例在
        // .NET 9 固定落到 Net5CompatSeedImpl → CompatPrng{56×int 种子数组 + 双游标}
        // (CompatPrng 是结构体,必须装箱改完整体写回)。布局不符时抛错而不是猜位:
        // 错位等于换一条随机流续跑,只能靠确定性验收当场暴露。
        // 布局为 net9.0 运行时实测契约(global.json 锁 9.x),升级运行时需重验。

        const int CompatSeedArrayLength = 56;

        /// <summary>导出为 [inext, inextp, seedArray×56] 的扁平数组。</summary>
        public static int[] ExportState()
        {
            object impl = RequireCompatImpl(out FieldInfo prngField);
            object prng = prngField.GetValue(impl)!;
            var state = new int[CompatSeedArrayLength + 2];
            state[0] = (int)GetField(prng, "_inext").GetValue(prng)!;
            state[1] = (int)GetField(prng, "_inextp").GetValue(prng)!;
            var seedArray = (int[])GetField(prng, "_seedArray").GetValue(prng)!;
            if (seedArray.Length != CompatSeedArrayLength)
                throw LayoutMismatch();
            Array.Copy(seedArray, 0, state, 2, CompatSeedArrayLength);
            return state;
        }

        public static void ImportState(int[] state)
        {
            if (state == null || state.Length != CompatSeedArrayLength + 2)
                throw new ArgumentException(
                    $"GameRandom state must be a flat array of {CompatSeedArrayLength + 2} ints, got {(state == null ? "null" : state.Length.ToString())}.");
            // 未初始化时构造载体(同样落 Net5CompatSeedImpl);随后状态被整体覆盖。
            random ??= new Random(0);
            object impl = RequireCompatImpl(out FieldInfo prngField);
            object prng = prngField.GetValue(impl)!;
            var seedArray = new int[CompatSeedArrayLength];
            Array.Copy(state, 2, seedArray, 0, CompatSeedArrayLength);
            GetField(prng, "_seedArray").SetValue(prng, seedArray);
            GetField(prng, "_inext").SetValue(prng, state[0]);
            GetField(prng, "_inextp").SetValue(prng, state[1]);
            prngField.SetValue(impl, prng);
        }

        static object RequireCompatImpl(out FieldInfo prngField)
        {
            if (random == null)
                throw new InvalidOperationException("GameRandom is not initialized; Init(seed) must run first.");
            object impl = GetField(random, "_impl").GetValue(random)!;
            if (impl.GetType().Name != "Net5CompatSeedImpl")
                throw LayoutMismatch();
            prngField = GetField(impl, "_prng");
            return impl;
        }

        static FieldInfo GetField(object instance, string name)
        {
            return instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw LayoutMismatch();
        }

        static InvalidOperationException LayoutMismatch() => new(
            "System.Random private layout does not match the pinned net9.0 Net5CompatSeedImpl contract; " +
            "GameRandom state export/import must be re-validated against the current runtime.");
    }
}
