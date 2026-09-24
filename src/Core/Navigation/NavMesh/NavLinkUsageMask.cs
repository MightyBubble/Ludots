using System;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// Link 的 profile 使用掩码。按位标记允许使用某条 Link 的 navmesh profile 下标，
    /// 避免每条 Link 携带一份字符串集合。
    /// </summary>
    public readonly struct NavLinkUsageMask
    {
        private const int WordBits = 64;

        private readonly ulong _low;
        private readonly ulong _high;

        public NavLinkUsageMask(ulong low, ulong high)
        {
            _low = low;
            _high = high;
        }

        public static NavLinkUsageMask None => new NavLinkUsageMask(0UL, 0UL);

        public bool IsNone => _low == 0UL && _high == 0UL;

        public static NavLinkUsageMask ForProfile(int profileIndex)
        {
            return profileIndex switch
            {
                < 0 => throw new InvalidOperationException("NavLink usage mask requires a non-negative profile index."),
                < WordBits => new NavLinkUsageMask(1UL << profileIndex, 0UL),
                < WordBits * 2 => new NavLinkUsageMask(0UL, 1UL << (profileIndex - WordBits)),
                _ => throw new InvalidOperationException(
                    $"NavLink usage mask supports at most {WordBits * 2} navmesh profiles, got index {profileIndex}.")
            };
        }

        public NavLinkUsageMask With(int profileIndex)
        {
            NavLinkUsageMask bit = ForProfile(profileIndex);
            return new NavLinkUsageMask(_low | bit._low, _high | bit._high);
        }

        public bool Allows(int profileIndex)
        {
            return profileIndex switch
            {
                < 0 => false,
                < WordBits => (_low & (1UL << profileIndex)) != 0UL,
                < WordBits * 2 => (_high & (1UL << (profileIndex - WordBits))) != 0UL,
                _ => false
            };
        }

        public bool AllowsAny(NavLinkUsageMask other)
        {
            return ((_low & other._low) | (_high & other._high)) != 0UL;
        }
    }
}
