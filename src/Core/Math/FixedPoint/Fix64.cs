using System;
using System.Runtime.CompilerServices;
using FixPointCS;

namespace Ludots.Core.Mathematics.FixedPoint
{
    /// <summary>
    /// Q31.32 格式的64位定点数。
    /// 
    /// 范围: ±2,147,483,647 (约 ±21亿)
    /// 精度: 约 2.3e-10 (1 / 2^32)
    /// 
    /// 内部表示: long (RawValue)
    /// - 高32位: 整数部分 (含符号)
    /// - 低32位: 小数部分
    /// 
    /// 使用场景:
    /// - 确定性物理模拟
    /// - 帧同步游戏
    /// - 录像回放
    /// 
    /// 底层实现委托给 FixPointCS.Fixed64 高精度库。
    /// </summary>
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public readonly long RawValue;

        #region Constants

        public const int FractionalBits = 32;
        public const long One = Fixed64.One;                     // 4294967296 = 1L << 32
        public const long Half = Fixed64.Half;                   // 2147483648
        private const long MaxRaw = Fixed64.MaxValue;
        private const long MinRaw = Fixed64.MinValue;

        public static readonly Fix64 Zero = new Fix64(Fixed64.Zero);
        public static readonly Fix64 OneValue = new Fix64(Fixed64.One);
        public static readonly Fix64 HalfValue = new Fix64(Fixed64.Half);
        public static readonly Fix64 MinValue = new Fix64(MinRaw);
        public static readonly Fix64 MaxValue = new Fix64(MaxRaw);
        public static readonly Fix64 Pi = new Fix64(Fixed64.Pi);           // π ≈ 3.14159265359
        public static readonly Fix64 TwoPi = new Fix64(Fixed64.Pi2);       // 2π
        public static readonly Fix64 HalfPi = new Fix64(Fixed64.PiHalf);   // π/2
        public static readonly Fix64 E = new Fix64(Fixed64.E);             // e ≈ 2.71828182846
        public static readonly Fix64 Deg2Rad = new Fix64(74961321L);       // π/180
        public static readonly Fix64 Rad2Deg = new Fix64(246083499208L);   // 180/π
        
        /// <summary>
        /// 厘米到米的转换系数 (0.01)
        /// </summary>
        public static readonly Fix64 CmToMeter = new Fix64(42949673L);     // 0.01 * One

        #endregion

        #region Constructors

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Fix64(long rawValue)
        {
            RawValue = rawValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromRaw(long rawValue) => new Fix64(rawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromInt(int value) => new Fix64((long)value << FractionalBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromInt(long value) => new Fix64(value << FractionalBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromFloat(float value) => new Fix64((long)(value * One));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 FromDouble(double value) => new Fix64((long)(value * One));

        #endregion

        #region Conversions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ToInt() => (int)(RawValue >> FractionalBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ToLong() => RawValue >> FractionalBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat() => RawValue / (float)One;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ToDouble() => RawValue / (double)One;

        /// <summary>
        /// 四舍五入到最近整数
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int RoundToInt()
        {
            long rounded = RawValue + Half;
            return (int)(rounded >> FractionalBits);
        }

        /// <summary>
        /// 向下取整
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FloorToInt() => (int)(RawValue >> FractionalBits);

        /// <summary>
        /// 向上取整
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CeilToInt()
        {
            long mask = One - 1;
            if ((RawValue & mask) == 0) return (int)(RawValue >> FractionalBits);
            return (int)((RawValue >> FractionalBits) + 1);
        }

        #endregion

        #region Operators - Basic Arithmetic

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64(a.RawValue + b.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64(a.RawValue - b.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator -(Fix64 a) => new Fix64(-a.RawValue);

        /// <summary>
        /// 乘法 - 委托给 Fixed64 高精度实现
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            return new Fix64(Fixed64.Mul(a.RawValue, b.RawValue));
        }

        /// <summary>
        /// 除法 - 委托给 Fixed64 高精度实现
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator /(Fix64 a, Fix64 b)
        {
            return new Fix64(Fixed64.DivPrecise(a.RawValue, b.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator %(Fix64 a, Fix64 b) => new Fix64(a.RawValue % b.RawValue);

        #endregion

        #region Operators - Integer Shortcuts

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator *(Fix64 a, int b) => new Fix64(a.RawValue * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator *(int a, Fix64 b) => new Fix64(b.RawValue * a);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 operator /(Fix64 a, int b) => new Fix64(a.RawValue / b);

        #endregion

        #region Operators - Comparison

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Fix64 a, Fix64 b) => a.RawValue == b.RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Fix64 a, Fix64 b) => a.RawValue != b.RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Fix64 a, Fix64 b) => a.RawValue < b.RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Fix64 a, Fix64 b) => a.RawValue <= b.RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Fix64 a, Fix64 b) => a.RawValue > b.RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Fix64 a, Fix64 b) => a.RawValue >= b.RawValue;

        #endregion

        #region Operators - Implicit/Explicit Conversions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Fix64(int value) => FromInt(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Fix64(long value) => FromInt(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Fix64(float value) => FromFloat(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Fix64(double value) => FromDouble(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator int(Fix64 value) => value.ToInt();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator long(Fix64 value) => value.ToLong();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator float(Fix64 value) => value.ToFloat();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator double(Fix64 value) => value.ToDouble();

        #endregion

        #region Static Math Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Abs(Fix64 value) => value.RawValue < 0 ? new Fix64(-value.RawValue) : value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Min(Fix64 a, Fix64 b) => a.RawValue < b.RawValue ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Max(Fix64 a, Fix64 b) => a.RawValue > b.RawValue ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
        {
            if (value.RawValue < min.RawValue) return min;
            if (value.RawValue > max.RawValue) return max;
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(Fix64 value)
        {
            if (value.RawValue > 0) return 1;
            if (value.RawValue < 0) return -1;
            return 0;
        }

        /// <summary>
        /// 线性插值
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Lerp(Fix64 a, Fix64 b, Fix64 t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// 向下取整
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Floor(Fix64 value)
        {
            return new Fix64(value.RawValue & ~(One - 1));
        }

        /// <summary>
        /// 向上取整
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Ceiling(Fix64 value)
        {
            long mask = One - 1;
            if ((value.RawValue & mask) == 0) return value;
            return new Fix64((value.RawValue & ~mask) + One);
        }

        /// <summary>
        /// 四舍五入
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Round(Fix64 value)
        {
            return new Fix64((value.RawValue + Half) & ~(One - 1));
        }

        #endregion

        #region IEquatable / IComparable

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Fix64 other) => RawValue == other.RawValue;

        public override bool Equals(object? obj) => obj is Fix64 other && Equals(other);

        public override int GetHashCode() => RawValue.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(Fix64 other) => RawValue.CompareTo(other.RawValue);

        #endregion

        #region ToString

        public override string ToString() => ToDouble().ToString("F6");

        public string ToString(string format) => ToDouble().ToString(format);

        #endregion
    }
}
