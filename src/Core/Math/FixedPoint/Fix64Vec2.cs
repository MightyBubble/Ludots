using System;
using System.Runtime.CompilerServices;
using System.Numerics;

namespace Ludots.Core.Mathematics.FixedPoint
{
    /// <summary>
    /// 2D 定点数向量，用于确定性物理和位置计算。
    /// 
    /// 使用 Fix64 (Q31.32) 作为分量类型。
    /// </summary>
    public readonly struct Fix64Vec2 : IEquatable<Fix64Vec2>
    {
        public readonly Fix64 X;
        public readonly Fix64 Y;

        #region Constants

        public static readonly Fix64Vec2 Zero = new Fix64Vec2(Fix64.Zero, Fix64.Zero);
        public static readonly Fix64Vec2 One = new Fix64Vec2(Fix64.OneValue, Fix64.OneValue);
        public static readonly Fix64Vec2 UnitX = new Fix64Vec2(Fix64.OneValue, Fix64.Zero);
        public static readonly Fix64Vec2 UnitY = new Fix64Vec2(Fix64.Zero, Fix64.OneValue);

        #endregion

        #region Constructors

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fix64Vec2(Fix64 x, Fix64 y)
        {
            X = x;
            Y = y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fix64Vec2(int x, int y)
        {
            X = Fix64.FromInt(x);
            Y = Fix64.FromInt(y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 FromInt(int x, int y) => new Fix64Vec2(Fix64.FromInt(x), Fix64.FromInt(y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 FromFloat(float x, float y) => new Fix64Vec2(Fix64.FromFloat(x), Fix64.FromFloat(y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 FromVector2(Vector2 v) => new Fix64Vec2(Fix64.FromFloat(v.X), Fix64.FromFloat(v.Y));

        #endregion

        #region Conversions

        /// <summary>
        /// 转换为整数向量（截断）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (int x, int y) ToInt() => (X.ToInt(), Y.ToInt());

        /// <summary>
        /// 转换为整数向量（四舍五入）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (int x, int y) RoundToInt() => (X.RoundToInt(), Y.RoundToInt());

        /// <summary>
        /// 转换为浮点向量
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 ToVector2() => new Vector2(X.ToFloat(), Y.ToFloat());

        /// <summary>
        /// 转换为 WorldCmInt2（整数厘米）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public WorldCmInt2 ToWorldCmInt2() => new WorldCmInt2(X.RoundToInt(), Y.RoundToInt());

        #endregion

        #region Operators - Arithmetic

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator +(Fix64Vec2 a, Fix64Vec2 b) => new Fix64Vec2(a.X + b.X, a.Y + b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator -(Fix64Vec2 a, Fix64Vec2 b) => new Fix64Vec2(a.X - b.X, a.Y - b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator -(Fix64Vec2 a) => new Fix64Vec2(-a.X, -a.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator *(Fix64Vec2 a, Fix64Vec2 b) => new Fix64Vec2(a.X * b.X, a.Y * b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator *(Fix64Vec2 a, Fix64 s) => new Fix64Vec2(a.X * s, a.Y * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator *(Fix64 s, Fix64Vec2 a) => new Fix64Vec2(a.X * s, a.Y * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator *(Fix64Vec2 a, int s) => new Fix64Vec2(a.X * s, a.Y * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator *(int s, Fix64Vec2 a) => new Fix64Vec2(a.X * s, a.Y * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator /(Fix64Vec2 a, Fix64Vec2 b) => new Fix64Vec2(a.X / b.X, a.Y / b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator /(Fix64Vec2 a, Fix64 s) => new Fix64Vec2(a.X / s, a.Y / s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 operator /(Fix64Vec2 a, int s) => new Fix64Vec2(a.X / s, a.Y / s);

        #endregion

        #region Operators - Comparison

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Fix64Vec2 a, Fix64Vec2 b) => a.X == b.X && a.Y == b.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Fix64Vec2 a, Fix64Vec2 b) => a.X != b.X || a.Y != b.Y;

        #endregion

        #region Vector Operations

        /// <summary>
        /// 点积
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Dot(Fix64Vec2 a, Fix64Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>
        /// 2D 叉积（返回标量 z 分量）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Cross(Fix64Vec2 a, Fix64Vec2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>
        /// 长度的平方（避免开方）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fix64 LengthSquared() => X * X + Y * Y;

        /// <summary>
        /// 长度（需要开方，相对较慢）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fix64 Length() => Fix64Math.Sqrt(LengthSquared());

        /// <summary>
        /// 归一化
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fix64Vec2 Normalized()
        {
            var len = Length();
            if (len == Fix64.Zero) return Zero;
            return new Fix64Vec2(X / len, Y / len);
        }

        /// <summary>
        /// 距离的平方
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 DistanceSquared(Fix64Vec2 a, Fix64Vec2 b) => (b - a).LengthSquared();

        /// <summary>
        /// 距离
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64 Distance(Fix64Vec2 a, Fix64Vec2 b) => (b - a).Length();

        /// <summary>
        /// 线性插值
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Lerp(Fix64Vec2 a, Fix64Vec2 b, Fix64 t)
        {
            return new Fix64Vec2(
                Fix64.Lerp(a.X, b.X, t),
                Fix64.Lerp(a.Y, b.Y, t)
            );
        }

        /// <summary>
        /// 分量最小值
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Min(Fix64Vec2 a, Fix64Vec2 b)
        {
            return new Fix64Vec2(Fix64.Min(a.X, b.X), Fix64.Min(a.Y, b.Y));
        }

        /// <summary>
        /// 分量最大值
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Max(Fix64Vec2 a, Fix64Vec2 b)
        {
            return new Fix64Vec2(Fix64.Max(a.X, b.X), Fix64.Max(a.Y, b.Y));
        }

        /// <summary>
        /// 分量钳制
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Clamp(Fix64Vec2 value, Fix64Vec2 min, Fix64Vec2 max)
        {
            return new Fix64Vec2(
                Fix64.Clamp(value.X, min.X, max.X),
                Fix64.Clamp(value.Y, min.Y, max.Y)
            );
        }

        /// <summary>
        /// 垂直向量（逆时针旋转90度）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fix64Vec2 Perpendicular() => new Fix64Vec2(-Y, X);

        /// <summary>
        /// 反射向量
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 Reflect(Fix64Vec2 direction, Fix64Vec2 normal)
        {
            var dot2 = Dot(direction, normal) * 2;
            return direction - normal * dot2;
        }

        #endregion

        #region IEquatable

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Fix64Vec2 other) => X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is Fix64Vec2 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X.RawValue, Y.RawValue);

        #endregion

        #region ToString

        public override string ToString() => $"({X}, {Y})";

        #endregion
    }
}
