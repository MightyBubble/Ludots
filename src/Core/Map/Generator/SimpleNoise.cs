using System;

namespace Ludots.Core.Map.Generator
{
    /// <summary>
    /// A simple implementation of Perlin Noise-like algorithm for MVP.
    /// Not optimized for speed, but sufficient for 256x256 generation.
    /// </summary>
    public static class SimpleNoise
    {
        private static readonly int[] Permutation = new int[512];

        static SimpleNoise()
        {
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;

            // Shuffle
            var rnd = new Random(12345);
            for (int i = 0; i < 256; i++)
            {
                int j = rnd.Next(256);
                (p[i], p[j]) = (p[j], p[i]);
            }

            for (int i = 0; i < 512; i++)
            {
                Permutation[i] = p[i & 255];
            }
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double t, double a, double b) => a + t * (b - a);
        private static double Grad(int hash, double x, double y, double z)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public static double Noise(double x, double y, double z = 0)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;
            int Z = (int)Math.Floor(z) & 255;

            x -= Math.Floor(x);
            y -= Math.Floor(y);
            z -= Math.Floor(z);

            double u = Fade(x);
            double v = Fade(y);
            double w = Fade(z);

            int A = Permutation[X] + Y;
            int AA = Permutation[A] + Z;
            int AB = Permutation[A + 1] + Z;
            int B = Permutation[X + 1] + Y;
            int BA = Permutation[B] + Z;
            int BB = Permutation[B + 1] + Z;

            return Lerp(w, Lerp(v, Lerp(u, Grad(Permutation[AA], x, y, z),
                                           Grad(Permutation[BA], x - 1, y, z)),
                                   Lerp(u, Grad(Permutation[AB], x, y - 1, z),
                                           Grad(Permutation[BB], x - 1, y - 1, z))),
                           Lerp(v, Lerp(u, Grad(Permutation[AA + 1], x, y, z - 1),
                                           Grad(Permutation[BA + 1], x - 1, y, z - 1)),
                                   Lerp(u, Grad(Permutation[AB + 1], x, y - 1, z - 1),
                                           Grad(Permutation[BB + 1], x - 1, y - 1, z - 1))));
        }
    }
}
