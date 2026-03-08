using System.Collections.Generic;
using StellarNet.Shared.Protocol.BuiltIn;

namespace StellarNet.Server.GlobalModules.LobbyChat
{
    /// <summary>
    /// 大厅全局聊天模块 Model，维护聊天历史消息缓冲与基础频率限制状态。
    /// 不承载业务逻辑，不直接驱动任何网络发送。
    /// 历史消息采用固定容量环形缓冲策略：超出容量时丢弃最旧消息，不扩容。
    /// 频率限制状态以 SessionId 为粒度维护，记录最近一次发言时间。
    /// </summary>
    public sealed class LobbyChatModel
    {
        // 历史消息固定容量，超出时丢弃最旧消息
        private readonly int _historyCapacity;

        // 历史消息队列，按时间从旧到新排列
        private readonly Queue<LobbyChatHistoryItem> _history;

        // SessionId → 最近发言时间（Unix 毫秒时间戳），用于频率限制判定
        private readonly Dictionary<string, long> _lastSendTimeMs
            = new Dictionary<string, long>();

        // 发言最小间隔（毫秒），0 表示不限制
        private long _minIntervalMs;

        public LobbyChatModel(int historyCapacity = 100, long minIntervalMs = 1000)
        {
            _historyCapacity = historyCapacity > 0 ? historyCapacity : 100;
            _minIntervalMs = minIntervalMs >= 0 ? minIntervalMs : 0;
            _history = new Queue<LobbyChatHistoryItem>(_historyCapacity);
        }

        /// <summary>
        /// 追加一条历史消息，超出容量时丢弃最旧消息。
        /// </summary>
        public void AppendHistory(LobbyChatHistoryItem item)
        {
            if (item == null)
            {
                return;
            }

            if (_history.Count >= _historyCapacity)
            {
                _history.Dequeue();
            }

            _history.Enqueue(item);
        }

        /// <summary>
        /// 获取最近 count 条历史消息，按时间从旧到新排列。
        /// </summary>
        public List<LobbyChatHistoryItem> GetHistory(int count)
        {
            var all = new List<LobbyChatHistoryItem>(_history);
            int start = System.Math.Max(0, all.Count - count);
            return all.GetRange(start, all.Count - start);
        }

        /// <summary>
        /// 检查指定 Session 是否满足发言频率限制。
        /// 满足限制（可以发言）返回 true，不满足返回 false。
        /// </summary>
        public bool CheckRateLimit(string sessionId, long nowMs)
        {
            if (_minIntervalMs <= 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }

            if (!_lastSendTimeMs.TryGetValue(sessionId, out var lastMs))
            {
                return true;
            }

            return (nowMs - lastMs) >= _minIntervalMs;
        }

        /// <summary>
        /// 更新指定 Session 的最近发言时间。
        /// </summary>
        public void UpdateSendTime(string sessionId, long nowMs)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                _lastSendTimeMs[sessionId] = nowMs;
            }
        }

        /// <summary>
        /// 获取指定 Session 的频率限制冷却结束时间（Unix 毫秒时间戳）。
        /// 用于客户端 UI 展示冷却倒计时。
        /// </summary>
        public long GetCooldownEndMs(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || !_lastSendTimeMs.TryGetValue(sessionId, out var lastMs))
            {
                return 0;
            }
            return lastMs + _minIntervalMs;
        }

        /// <summary>
        /// 清除指定 Session 的频率限制状态，在 Session 销毁时调用。
        /// </summary>
        public void ClearSession(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                _lastSendTimeMs.Remove(sessionId);
            }
        }
    }
}
