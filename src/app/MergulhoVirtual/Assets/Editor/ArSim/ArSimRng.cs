using UnityEngine;

namespace MergulhoVirtual.ArSim
{
    /// <summary>
    /// Deterministic xorshift PRNG + a stateless hash. UnityEngine.Random is
    /// global, shared and not contractually stable across versions; every random
    /// quantity in the simulator comes from here instead so a seeded run is
    /// reproducible on any machine and any Unity build.
    /// </summary>
    internal sealed class ArSimRng
    {
        uint s;

        public ArSimRng(int seed) { s = seed == 0 ? 0x9E3779B9u : (uint)seed; }

        public uint NextUInt()
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return s;
        }

        /// <summary>Uniform in [0,1).</summary>
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * Next01();

        /// <summary>Approximately standard normal (Irwin–Hall, 4 draws).</summary>
        public float NextGaussian()
        {
            float sum = Next01() + Next01() + Next01() + Next01();
            return (sum - 2f) * 1.7320508f;
        }

        // --- stateless helpers, for pure functions of time --------------------

        public static uint Hash(uint a, uint b)
        {
            uint h = a * 0x9E3779B9u ^ b * 0x85EBCA6Bu;
            h ^= h >> 15; h *= 0x2545F491u;
            h ^= h >> 13; h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }

        /// <summary>A reproducible ~N(0,1) draw indexed off a hash, so the same
        /// (hash, lane) always yields the same value.</summary>
        public static float GaussianFromHash(uint h, int lane)
        {
            float sum = 0f;
            for (int i = 0; i < 4; i++)
            {
                h = Hash(h, (uint)(lane * 977 + i * 31 + 7));
                sum += (h >> 8) * (1f / 16777216f);
            }
            return (sum - 2f) * 1.7320508f;
        }
    }
}
