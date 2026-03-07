// Assets/StellarNetFramework/Server/Infrastructure/IdempotentCache.cs

using System.Collections.Generic;
using UnityEngine;
using StellarNet.Server.Infrastructure.GlobalScope;

namespace StellarNet.Server.Infrastructure
{
    // 幂等缓存，用于防止客户端重复上行请求被重复处理。
    // 典型场景：建房、加房、购买等不可重复执行的关键业务请求。
    // 缓存 Key 由业务层自定义（如 SessionId + 请求序号），框架只负责存储与查询。
    // 容量上限与过期时长由 NetConfig 配置，超出容量时采用 LRU 淘汰策略。
    // 过期条目由 TickExpireCheck() 定期清理，不自驱动。
    public sealed class IdempotentCache : IGlobalService
    {
        // 缓存条目记录
        private sealed class CacheEntry
        {
            // 条目过期时间戳（Unix 毫秒）
            public long ExpireUnixMs { get; }

            // 最近一次访问时间戳，用于 LRU 排序
            public long LastAccessUnixMs { get; set; }

            // LRU 链表节点引用，用于 O(1) 移动到链表头
            public LinkedListNode<string> LruNode { get; set; }

            public CacheEntry(long expireUnixMs, long nowUnixMs)
            {
                ExpireUnixMs = expireUnixMs;
                LastAccessUnixMs = nowUnixMs;
            }
        }

        // Key → CacheEntry 主存储
        private readonly Dictionary<string, CacheEntry> _cache
            = new Dictionary<string, CacheEntry>();

        // LRU 访问顺序链表，链表头为最近访问，链表尾为最久未访问
        private readonly LinkedList<string> _lruOrder = new LinkedList<string>();

        // 缓存最大容量（条目数），超出时淘汰最久未访问的条目
        private int _maxSize;

        // 缓存条目过期时长（毫秒）
        private long _expireMs;

        public IdempotentCache(int maxSize = 10000, long expireMs = 300000)
        {
            if (maxSize <= 0)
            {
                Debug.LogError(
                    $"[IdempotentCache] 构造失败：maxSize 必须大于 0，当前值={maxSize}，" +
                    $"已使用默认值 10000。");
                maxSize = 10000;
            }

            if (expireMs <= 0)
            {
                Debug.LogError(
                    $"[IdempotentCache] 构造失败：expireMs 必须大于 0，当前值={expireMs}，" +
                    $"已使用默认值 300000ms。");
                expireMs = 300000;
            }

            _maxSize = maxSize;
            _expireMs = expireMs;
        }

        // 更新缓存配置，由 NetConfigManager 热重载时调用
        public void UpdateConfig(int maxSize, long expireMs)
        {
            if (maxSize > 0)
                _maxSize = maxSize;

            if (expireMs > 0)
                _expireMs = expireMs;
        }

        // 尝试记录一次请求。
        // 若 Key 已存在且未过期，返回 false 表示重复请求，调用方应拒绝处理。
        // 若 Key 不存在或已过期，写入缓存并返回 true 表示首次请求，调用方可正常处理。
        // 参数 key：幂等键，由业务层自定义，建议格式：{SessionId}_{RequestType}_{RequestSeq}
        // 参数 nowUnixMs：当前时间戳（Unix 毫秒），由调用方传入，保证时间源统一
        public bool TryRecord(string key, long nowUnixMs)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError(
                    $"[IdempotentCache] TryRecord 失败：key 不得为空，nowUnixMs={nowUnixMs}");
                return false;
            }

            // 若 Key 已存在，检查是否过期
            if (_cache.TryGetValue(key, out var existing))
            {
                if (nowUnixMs < existing.ExpireUnixMs)
                {
                    // 未过期，属于重复请求，更新访问时间并移动到 LRU 链表头
                    existing.LastAccessUnixMs = nowUnixMs;
                    _lruOrder.Remove(existing.LruNode);
                    _lruOrder.AddFirst(existing.LruNode);
                    return false;
                }

                // 已过期，移除旧条目，允许重新写入
                _lruOrder.Remove(existing.LruNode);
                _cache.Remove(key);
            }

            // 容量检查：超出上限时淘汰最久未访问的条目
            if (_cache.Count >= _maxSize)
            {
                EvictLruEntry();
            }

            // 写入新条目
            var expireUnixMs = nowUnixMs + _expireMs;
            var entry = new CacheEntry(expireUnixMs, nowUnixMs);
            var node = _lruOrder.AddFirst(key);
            entry.LruNode = node;
            _cache[key] = entry;

            return true;
        }

        // 查询指定 Key 是否存在有效（未过期）的缓存记录，不修改缓存状态
        public bool Contains(string key, long nowUnixMs)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            if (!_cache.TryGetValue(key, out var entry))
                return false;

            return nowUnixMs < entry.ExpireUnixMs;
        }

        // 主动移除指定 Key 的缓存记录，用于业务层主动撤销幂等保护
        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[IdempotentCache] Remove 失败：key 不得为空");
                return;
            }

            if (!_cache.TryGetValue(key, out var entry))
                return;

            _lruOrder.Remove(entry.LruNode);
            _cache.Remove(key);
        }

        // 过期条目巡检，由 GlobalInfrastructure.Tick() 定期调用
        // 每次巡检最多清理 maxCleanCount 条，避免单帧清理量过大影响主线程
        public void TickExpireCheck(long nowUnixMs, int maxCleanCount = 100)
        {
            if (_cache.Count == 0)
                return;

            // 从 LRU 链表尾部开始检查，尾部是最久未访问的条目，最可能已过期
            var cleaned = 0;
            var node = _lruOrder.Last;

            while (node != null && cleaned < maxCleanCount)
            {
                var prev = node.Previous;
                var key = node.Value;

                if (_cache.TryGetValue(key, out var entry) && nowUnixMs >= entry.ExpireUnixMs)
                {
                    _lruOrder.Remove(node);
                    _cache.Remove(key);
                    cleaned++;
                }
                else
                {
                    // 链表尾部条目未过期，说明更靠近头部的条目也未过期，提前退出
                    break;
                }

                node = prev;
            }
        }

        // 清空全部缓存，用于服务端关停阶段
        public void Clear()
        {
            _cache.Clear();
            _lruOrder.Clear();
        }

        // 当前缓存条目数，用于诊断
        public int Count => _cache.Count;

        // 淘汰 LRU 链表尾部（最久未访问）的条目
        private void EvictLruEntry()
        {
            var tail = _lruOrder.Last;
            if (tail == null)
                return;

            var key = tail.Value;
            _lruOrder.RemoveLast();
            _cache.Remove(key);
        }
    }
}
