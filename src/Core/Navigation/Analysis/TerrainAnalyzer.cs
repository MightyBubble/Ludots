using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Navigation.Analysis
{
    public enum TerrainWalkability
    {
        Walkable,   // Flat or gentle slope
        Blocked,    // Obstacle or invalid
        Cliff,      // Too steep, but might support climbing
        Water       // Requires swimming
    }

    public readonly struct TerrainTypeProperties
    {
        public readonly bool IsBlocked;
        public readonly bool IsWater;

        public TerrainTypeProperties(bool isBlocked, bool isWater)
        {
            IsBlocked = isBlocked;
            IsWater = isWater;
        }
    }

    public interface ITerrainTypeLookup
    {
        TerrainTypeProperties Get(byte terrainType);
    }

    public sealed class DefaultTerrainTypeLookup : ITerrainTypeLookup
    {
        public static readonly DefaultTerrainTypeLookup Instance = new DefaultTerrainTypeLookup();

        private readonly TerrainTypeProperties[] _props;

        private DefaultTerrainTypeLookup()
        {
            _props = new TerrainTypeProperties[16];
            for (int i = 0; i < _props.Length; i++)
            {
                _props[i] = new TerrainTypeProperties(isBlocked: false, isWater: false);
            }
        }

        public TerrainTypeProperties Get(byte terrainType)
        {
            int idx = terrainType & 0x0F;
            return _props[idx];
        }
    }

    public static class TerrainAnalyzer
    {
        /// <summary>
        /// Analyzes a triangle defined by 3 vertices to determine its walkability.
        /// Logic:
        /// - Max height diff == 0 -> Flat (Walkable)
        /// - Max height diff == 1 -> Slope (Walkable)
        /// - Max height diff >= 2 -> Cliff (Blocked/Climbable)
        /// </summary>
        public static TerrainWalkability AnalyzeTriangle(
            byte h1, byte t1, 
            byte h2, byte t2, 
            byte h3, byte t3)
        {
            return AnalyzeTriangle(h1, t1, h2, t2, h3, t3, DefaultTerrainTypeLookup.Instance);
        }

        public static TerrainWalkability AnalyzeTriangle(
            byte h1, byte t1,
            byte h2, byte t2,
            byte h3, byte t3,
            ITerrainTypeLookup lookup)
        {
            if (lookup != null)
            {
                TerrainTypeProperties p1 = lookup.Get(t1);
                TerrainTypeProperties p2 = lookup.Get(t2);
                TerrainTypeProperties p3 = lookup.Get(t3);

                if (p1.IsBlocked || p2.IsBlocked || p3.IsBlocked) return TerrainWalkability.Blocked;
                if (p1.IsWater || p2.IsWater || p3.IsWater) return TerrainWalkability.Water;
            }

            int diff1 = Math.Abs(h1 - h2);
            int diff2 = Math.Abs(h2 - h3);
            int diff3 = Math.Abs(h3 - h1);
            
            int maxDiff = Math.Max(diff1, Math.Max(diff2, diff3));

            if (maxDiff == 0) return TerrainWalkability.Walkable; // Flat
            if (maxDiff == 1) return TerrainWalkability.Walkable; // Slope
            
            // MaxDiff >= 2
            return TerrainWalkability.Cliff;
        }

        public static bool IsWalkable(TerrainWalkability type)
        {
            return type == TerrainWalkability.Walkable;
        }
    }
}
