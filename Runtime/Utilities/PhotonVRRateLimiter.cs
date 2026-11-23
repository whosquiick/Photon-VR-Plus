using System.Collections.Generic;
using UnityEngine;

namespace Photon.VR
{
    /// <summary>Token bucket: allows bursts up to <c>capacity</c>, refilling at <c>perSecond</c>.</summary>
    public sealed class PhotonVRRateLimiter
    {
        private float capacity;
        private float perSecond;
        private float tokens;
        private float lastRefill;

        public PhotonVRRateLimiter(float perSecond, float capacity)
        {
            Configure(perSecond, capacity);
            tokens = this.capacity;
            lastRefill = Now;
        }

        // Unity's clock when available; plain tests can drive it through Clock.
        public static System.Func<float> Clock;
        private static float Now => Clock != null ? Clock() : Time.realtimeSinceStartup;

        public void Configure(float perSecond, float capacity)
        {
            this.perSecond = Mathf.Max(0.01f, perSecond);
            this.capacity = Mathf.Max(1f, capacity);
        }

        public bool TryConsume(float amount = 1f)
        {
            float now = Now;
            tokens = Mathf.Min(capacity, tokens + (now - lastRefill) * perSecond);
            lastRefill = now;
            if (tokens < amount) return false;
            tokens -= amount;
            return true;
        }
    }

    /// <summary>One <see cref="PhotonVRRateLimiter"/> per actor and key, e.g. ("grab", actor 3).</summary>
    public sealed class PhotonVRRateLimiterTable
    {
        private readonly Dictionary<(int, string), PhotonVRRateLimiter> limiters = new Dictionary<(int, string), PhotonVRRateLimiter>();

        public bool TryConsume(int actor, string key, float perSecond, float burst)
        {
            if (!limiters.TryGetValue((actor, key), out PhotonVRRateLimiter limiter))
            {
                limiter = new PhotonVRRateLimiter(perSecond, burst);
                limiters[(actor, key)] = limiter;
            }
            return limiter.TryConsume();
        }

        public void Forget(int actor)
        {
            var remove = new List<(int, string)>();
            foreach (var k in limiters.Keys)
                if (k.Item1 == actor) remove.Add(k);
            foreach (var k in remove) limiters.Remove(k);
        }

        public void Clear() => limiters.Clear();
    }
}
