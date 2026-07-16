using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    /// <summary>
    /// 同じ SkillAction から展開された複数の兄弟判定が重なったとき、
    /// 1体へ重複ヒットするのを防ぐsource-aware共有レジストリ。
    /// 最初に命中したsource自身の正規多段だけを許し、cast寿命中は別sourceを拒否する。
    /// castId=0 は従来どおり共有制御なし。
    /// </summary>
    internal static class SkillCastHitRegistry
    {
        readonly struct Claim
        {
            public readonly int sourceId;
            public readonly float until;

            public Claim(int sourceId, float until)
            {
                this.sourceId = sourceId;
                this.until = until;
            }
        }

        static readonly Dictionary<(int castId, ulong targetId), Claim> s_locks =
            new Dictionary<(int castId, ulong targetId), Claim>();
        static int s_nextCastId;
        static int s_nextSourceId;
        static int s_claimCount;

        public static int NextCastId()
        {
            int id = Interlocked.Increment(ref s_nextCastId);
            if (id == 0) id = Interlocked.Increment(ref s_nextCastId);
            return id;
        }

        public static int NextSourceId()
        {
            int id = Interlocked.Increment(ref s_nextSourceId);
            if (id == 0) id = Interlocked.Increment(ref s_nextSourceId);
            return id;
        }

        public static bool TryClaim(int castId, Object target, Object source, float lockSeconds)
        {
            if (castId == 0 || target == null) return true;

            float now = Time.time;
            var key = (castId, EntityId.ToULong(target.GetEntityId()));
            int sourceId = source switch
            {
                Hitbox hitbox when hitbox.SharedSourceId != 0 => hitbox.SharedSourceId,
                Projectile projectile when projectile.SharedSourceId != 0 => projectile.SharedSourceId,
                _ => source != null ? source.GetEntityId().GetHashCode() : 0,
            };
            if (s_locks.TryGetValue(key, out Claim existing) && existing.until > now)
                return existing.sourceId == sourceId;

            s_locks[key] = new Claim(sourceId, now + Mathf.Max(0.02f, lockSeconds));
            if ((++s_claimCount & 127) == 0)
                RemoveExpired(now);
            return true;
        }

        static void RemoveExpired(float now)
        {
            if (s_locks.Count == 0) return;
            var expired = new List<(int castId, ulong targetId)>();
            foreach (var pair in s_locks)
                if (pair.Value.until <= now)
                    expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++)
                s_locks.Remove(expired[i]);
        }
    }
}
