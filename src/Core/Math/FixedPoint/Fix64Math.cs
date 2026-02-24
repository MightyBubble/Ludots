using System;
using System.Runtime.CompilerServices;
using FixPointCS;

namespace Ludots.Core.Mathematics.FixedPoint
{
    /// <summary>
    /// Fix64 数学函数库。
    /// 
    /// 委托给 FixPointCS 的 Fixed64 高精度实现。
    /// 所有函数都是确定性的，在所有平台上产生相同结果。
    /// </summary>
    public static class Fix64Math
    {
        #region Square Root

        /// <summary>
        /// 平方根
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Sqrt(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.Sqrt(value.RawValue));
        }

        /// <summary>
        /// 快速平方根（精度略低但更快）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 SqrtFast(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.SqrtFast(value.RawValue));
        }

        /// <summary>
        /// 最快平方根（精度最低但最快）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 SqrtFastest(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.SqrtFastest(value.RawValue));
        }

        /// <summary>
        /// 倒数平方根 (1/sqrt(x))
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 RSqrt(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.RSqrt(value.RawValue));
        }

        /// <summary>
        /// 快速倒数平方根
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 RSqrtFast(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.RSqrtFast(value.RawValue));
        }

        #endregion

        #region Trigonometry

        /// <summary>
        /// 正弦函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Sin(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.Sin(angle.RawValue));
        }

        /// <summary>
        /// 快速正弦函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 SinFast(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.SinFast(angle.RawValue));
        }

        /// <summary>
        /// 最快正弦函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 SinFastest(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.SinFastest(angle.RawValue));
        }

        /// <summary>
        /// 余弦函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Cos(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.Cos(angle.RawValue));
        }

        /// <summary>
        /// 快速余弦函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 CosFast(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.CosFast(angle.RawValue));
        }

        /// <summary>
        /// 最快余弦函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 CosFastest(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.CosFastest(angle.RawValue));
        }

        /// <summary>
        /// 正切函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Tan(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.Tan(angle.RawValue));
        }

        /// <summary>
        /// 快速正切函数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 TanFast(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.TanFast(angle.RawValue));
        }

        /// <summary>
        /// 反正切 (atan2)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Atan2(Fix64 y, Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Atan2(y.RawValue, x.RawValue));
        }

        /// <summary>
        /// 快速反正切 (atan2)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Atan2Fast(Fix64 y, Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Atan2Fast(y.RawValue, x.RawValue));
        }

        /// <summary>
        /// 反正弦 (asin)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Asin(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.Asin(value.RawValue));
        }

        /// <summary>
        /// 反余弦 (acos)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Acos(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.Acos(value.RawValue));
        }

        /// <summary>
        /// 反正切 (atan)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Atan(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.Atan(value.RawValue));
        }

        #endregion

        #region Exponential / Logarithm

        /// <summary>
        /// 2的指数幂 (2^x)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Exp2(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Exp2(x.RawValue));
        }

        /// <summary>
        /// 自然指数 (e^x)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Exp(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Exp(x.RawValue));
        }

        /// <summary>
        /// 自然对数 (ln)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Log(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Log(x.RawValue));
        }

        /// <summary>
        /// 以2为底的对数 (log2)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Log2(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Log2(x.RawValue));
        }

        /// <summary>
        /// 幂运算 (x^exponent)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Pow(Fix64 x, Fix64 exponent)
        {
            return Fix64.FromRaw(Fixed64.Pow(x.RawValue, exponent.RawValue));
        }

        /// <summary>
        /// 2的整数幂
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Pow2(int exponent)
        {
            if (exponent >= 31) return Fix64.MaxValue;
            if (exponent < -32) return Fix64.Zero;
            
            if (exponent >= 0)
            {
                return Fix64.FromRaw(Fix64.One << exponent);
            }
            else
            {
                return Fix64.FromRaw(Fix64.One >> (-exponent));
            }
        }

        #endregion

        #region Division (Precise)

        /// <summary>
        /// 精确除法
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 DivPrecise(Fix64 a, Fix64 b)
        {
            return Fix64.FromRaw(Fixed64.DivPrecise(a.RawValue, b.RawValue));
        }

        /// <summary>
        /// 快速除法
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 DivFast(Fix64 a, Fix64 b)
        {
            return Fix64.FromRaw(Fixed64.DivFast(a.RawValue, b.RawValue));
        }

        /// <summary>
        /// 倒数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Rcp(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Rcp(x.RawValue));
        }

        #endregion

        #region Utility

        /// <summary>
        /// 取模运算
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Mod(Fix64 a, Fix64 b)
        {
            return Fix64.FromRaw(Fixed64.Mod(a.RawValue, b.RawValue));
        }

        /// <summary>
        /// 向量长度（使用 Sqrt）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 VectorLength(Fix64 x, Fix64 y)
        {
            return Sqrt(x * x + y * y);
        }

        /// <summary>
        /// 快速向量长度
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 VectorLengthFast(Fix64 x, Fix64 y)
        {
            return SqrtFast(x * x + y * y);
        }

        /// <summary>
        /// 归一化向量（返回单位向量）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Fix64 x, Fix64 y) Normalize(Fix64 x, Fix64 y)
        {
            var invLen = RSqrt(x * x + y * y);
            return (x * invLen, y * invLen);
        }

        #endregion
    }
}
