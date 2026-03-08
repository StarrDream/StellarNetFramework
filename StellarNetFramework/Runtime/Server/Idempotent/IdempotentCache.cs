using System.Collections.Generic;
using UnityEngine;

namespace StellarNet.Server.Idempotent
{
    /// <summary>
    /// 幂等防重缓存，负责不可重复请求的防重处理。
    /// 不承担业务状态机职责，只负责 Token 去重与结果缓存。
    /// 以建房为典型场景：C2S_CreateRoom 必须携带客户端本地生成的幂等 Token。
    /// Token 在 TTL 内重复请求时直接返回原始响应，过期后视为新请求。
    /// 同一 Token 在 TTL 内重复请求时必须直接复用原始响应，不进入幂等查询逻辑。
    /// 幂等缓存仅用于去重，不持有房间重量级业务对象引用。
    /// </summary>
    public sealed class IdempotentCache
    {
        /// <summary>
        /// 单条幂等缓存记录，保存 Token 对应的处理结果与过期时间。
        /// </summary>
        private sealed class IdempotentRecord
        {
            /// <summary>
            /// 缓存的处理结果，由业务层写入，框架不解析其内容。
            /// 允许缓存成功结果，也允许缓存明确失败结果，统一受同一 TTL 管理。
            /// </summary>
            public object Result;

            /// <summary>
            /// 记录过期时间（Unix 毫秒时间戳）。
            /// </summary>
            public long ExpireUnixMs;

            /// <summary>
            /// 当前记录是否已有处理结果（区分"正在处理中"与"已有结果"）。
            /// </summary>
            public bool HasResult;
        }

        // Token → 幂等记录
        private readonly Dictionary<string, IdempotentRecord> _cache
            = new Dictionary<string, IdempotentRecord>();

        private float _ttlSeconds;
        private float _cleanupIntervalSeconds;
        private float _lastCleanupTime;

        public IdempotentCache(float ttlSeconds, float cleanupIntervalSeconds)
        {
            _ttlSeconds = ttlSeconds;
            _cleanupIntervalSeconds = cleanupIntervalSeconds;
            _lastCleanupTime = UnityEngine.Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 更新 TTL 与清理间隔配置，由 NetConfigManager 热重载后调用。
        /// 只对后续新判定生效，不追溯已在缓存中的记录。
        /// </summary>
        public void UpdateConfig(float ttlSeconds, float cleanupIntervalSeconds)
        {
            _ttlSeconds = ttlSeconds;
            _cleanupIntervalSeconds = cleanupIntervalSeconds;
        }

        /// <summary>
        /// 检查 Token 是否已存在有效缓存结果。
        /// Token 为空时直接返回 false，服务端必须直接返回失败结果，不进入幂等查询逻辑。
        /// 命中时必须实时判定 TTL，过期记录视为未命中。
        /// </summary>
        public bool TryGetResult(string token, out object result)
        {
            result = null;

            if (string.IsNullOrEmpty(token))
            {
                Debug.LogError("[IdempotentCache] TryGetResult 失败：token 为空，Token 必须为非空字符串，服务端应直接返回失败结果。");
                return false;
            }

            if (!_cache.TryGetValue(token, out var record))
            {
                return false;
            }

            // 命中时必须实时判定 TTL，过期记录视为未命中
            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nowMs > record.ExpireUnixMs)
            {
                // 过期后同一 Token 重试视为新请求，移除过期记录
                _cache.Remove(token);
                return false;
            }

            if (!record.HasResult)
            {
                // Token 存在但尚未有处理结果（正在处理中），返回 false 让调用方等待
                return false;
            }

            result = record.Result;
            return true;
        }

        /// <summary>
        /// 占位 Token，表示该请求正在处理中，防止并发重复触发。
        /// Token 为空时直接报错阻断，不进入占位逻辑。
        /// </summary>
        public bool TryOccupy(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogError("[IdempotentCache] TryOccupy 失败：token 为空，Token 必须为非空字符串。");
                return false;
            }

            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (_cache.TryGetValue(token, out var existing))
            {
                // Token 已存在且未过期，拒绝重复占位
                if (nowMs <= existing.ExpireUnixMs)
                {
                    return false;
                }
                // 已过期则允许重新占位
                _cache.Remove(token);
            }

            long expireMs = nowMs + (long)(_ttlSeconds * 1000f);
            _cache[token] = new IdempotentRecord
            {
                Result = null,
                ExpireUnixMs = expireMs,
                HasResult = false
            };

            return true;
        }

        /// <summary>
        /// 写入 Token 对应的处理结果。
        /// 允许缓存成功结果，也允许缓存明确失败结果，统一受同一 TTL 管理。
        /// Token 不存在时（可能已过期被清理）输出 Warning，不视为 Error。
        /// </summary>
        public void SetResult(string token, object result)
        {
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogError("[IdempotentCache] SetResult 失败：token 为空。");
                return;
            }

            if (!_cache.TryGetValue(token, out var record))
            {
                Debug.LogWarning($"[IdempotentCache] SetResult 警告：token={token} 不存在于缓存中，可能已过期被清理，结果写入跳过。");
                return;
            }

            record.Result = result;
            record.HasResult = true;
        }

        /// <summary>
        /// 主循环 Tick，执行过期记录的后台巡检清理。
        /// 后台巡检只负责清理回收，不负责逻辑有效性判定（逻辑有效性由 TryGetResult 实时判定）。
        /// 由 GlobalInfrastructure.Tick() 驱动。
        /// </summary>
        public void Tick()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastCleanupTime < _cleanupIntervalSeconds)
            {
                return;
            }
            _lastCleanupTime = now;
            CleanupExpired();
        }

        /// <summary>
        /// 清理所有已过期的幂等缓存记录。
        /// </summary>
        private void CleanupExpired()
        {
            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var expiredTokens = new List<string>();

            foreach (var kv in _cache)
            {
                if (nowMs > kv.Value.ExpireUnixMs)
                {
                    expiredTokens.Add(kv.Key);
                }
            }

            foreach (var token in expiredTokens)
            {
                _cache.Remove(token);
            }

            if (expiredTokens.Count > 0)
            {
                Debug.Log($"[IdempotentCache] 后台巡检清理过期记录 {expiredTokens.Count} 条。");
            }
        }
    }
}
