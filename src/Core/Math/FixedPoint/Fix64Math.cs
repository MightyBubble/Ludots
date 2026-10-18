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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Sqrt(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.Sqrt(value.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 SqrtFast(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.SqrtFast(value.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 SqrtFastest(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.SqrtFastest(value.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 RSqrt(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.RSqrt(value.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 RSqrtFast(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.RSqrtFast(value.RawValue));
        }

        #endregion

        #region Trigonometry

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Sin(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.Sin(angle.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 SinFast(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.SinFast(angle.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 SinFastest(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.SinFastest(angle.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Cos(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.Cos(angle.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 CosFast(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.CosFast(angle.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 CosFastest(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.CosFastest(angle.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Tan(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.Tan(angle.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 TanFast(Fix64 angle)
        {
            return Fix64.FromRaw(Fixed64.TanFast(angle.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Atan2(Fix64 y, Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Atan2(y.RawValue, x.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Atan2Fast(Fix64 y, Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Atan2Fast(y.RawValue, x.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Asin(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.Asin(value.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Acos(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.Acos(value.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Atan(Fix64 value)
        {
            return Fix64.FromRaw(Fixed64.Atan(value.RawValue));
        }

        #endregion

        #region Exponential / Logarithm

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Exp2(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Exp2(x.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Exp(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Exp(x.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Log(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Log(x.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Log2(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Log2(x.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Pow(Fix64 x, Fix64 exponent)
        {
            return Fix64.FromRaw(Fixed64.Pow(x.RawValue, exponent.RawValue));
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 DivPrecise(Fix64 a, Fix64 b)
        {
            return Fix64.FromRaw(Fixed64.DivPrecise(a.RawValue, b.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 DivFast(Fix64 a, Fix64 b)
        {
            return Fix64.FromRaw(Fixed64.DivFast(a.RawValue, b.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Rcp(Fix64 x)
        {
            return Fix64.FromRaw(Fixed64.Rcp(x.RawValue));
        }

        #endregion

        #region Utility

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Mod(Fix64 a, Fix64 b)
        {
            return Fix64.FromRaw(Fixed64.Mod(a.RawValue, b.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 VectorLength(Fix64 x, Fix64 y)
        {
            return Sqrt(x * x + y * y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 VectorLengthFast(Fix64 x, Fix64 y)
        {
            return SqrtFast(x * x + y * y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (Fix64 x, Fix64 y) Normalize(Fix64 x, Fix64 y)
        {
            var invLen = RSqrt(x * x + y * y);
            return (x * invLen, y * invLen);
        }

        #endregion
    }
}
