using System.Collections.Generic;
using StellarNet.Shared.Protocol.BuiltIn;

namespace StellarNet.Client.GlobalModules.LobbyChat
{
    /// <summary>
    /// 客户端大厅聊天模块 Model，维护本地聊天历史消息缓冲与发言冷却状态。
    /// 不承载业务逻辑，不直接驱动任何网络发送。
    /// 历史消息采用固定容量策略，超出容量时丢弃最旧消息，不扩容。
    /// 发言冷却状态由服务端权威判定，客户端只做本地预判以优化 UI 体验，不作为最终依据。
    /// </summary>
    public sealed class ClientLobbyChatModel
    {
        private readonly int _historyCapacity;
        private readonly List<LobbyChatHistoryItem> _history;

        /// <summary>
        /// 当前是否处于发言冷却中（本地预判，不作为最终依据）。
        /// </summary>
        public bool IsInCooldown { get; private set; }

        /// <summary>
        /// 冷却结束时间（Unix 毫秒时间戳），用于 UI 倒计时展示。
        /// </summary>
        public long CooldownEndMs { get; private set; }

        /// <summary>
        /// 最近一次被服务端拦截的原因。
        /// </summary>
        public string LastBlockReason { get; private set; }

        public ClientLobbyChatModel(int historyCapacity = 100)
        {
            _historyCapacity = historyCapacity > 0 ? historyCapacity : 100;
            _history = new List<LobbyChatHistoryItem>(_historyCapacity);
            IsInCooldown = false;
            CooldownEndMs = 0;
            LastBlockReason = string.Empty;
        }

        /// <summary>
        /// 追加一条聊天消息到历史缓冲，超出容量时丢弃最旧消息。
        /// </summary>
        public void AppendMessage(LobbyChatHistoryItem item)
        {
            if (item == null)
            {
                return;
            }

            if (_history.Count >= _historyCapacity)
            {
                _history.RemoveAt(0);
            }

            _history.Add(item);
        }

        /// <summary>
        /// 获取历史消息列表快照。
        /// </summary>
        public List<LobbyChatHistoryItem> GetHistory() => new List<LobbyChatHistoryItem>(_history);

        /// <summary>
        /// 设置历史消息列表（从服务端拉取历史后调用）。
        /// </summary>
        public void SetHistory(LobbyChatHistoryItem[] items)
        {
            _history.Clear();
            if (items == null)
            {
                return;
            }
            foreach (var item in items)
            {
                if (item != null)
                {
                    _history.Add(item);
                }
            }
        }

        /// <summary>
        /// 写入本地冷却状态，在收到 S2C_LobbyChatBlocked 时调用。
        /// </summary>
        public void SetCooldown(long cooldownEndMs)
        {
            IsInCooldown = true;
            CooldownEndMs = cooldownEndMs;
        }

        /// <summary>
        /// 写入被拦截原因。
        /// </summary>
        public void SetBlockReason(string reason)
        {
            LastBlockReason = reason ?? string.Empty;
        }

        /// <summary>
        /// 根据当前时间更新冷却状态，由 Tick 或 View 层主动调用。
        /// </summary>
        public void UpdateCooldown(long nowMs)
        {
            if (IsInCooldown && nowMs >= CooldownEndMs)
            {
                IsInCooldown = false;
            }
        }
    }
}
