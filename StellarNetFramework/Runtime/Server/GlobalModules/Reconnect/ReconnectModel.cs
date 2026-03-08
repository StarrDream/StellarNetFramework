namespace StellarNet.Server.GlobalModules.Reconnect
{
    /// <summary>
    /// 断线重连模块 Model，不承载业务逻辑，只保存重连流程所需的运行时状态。
    /// 重连流程的核心状态来自 SessionManager 与 GlobalRoomManager，
    /// 此 Model 只保存重连流程的辅助状态（如正在重连中的 SessionId 集合）。
    /// 防止重连请求在流程未完成时被重复触发。
    /// </summary>
    public sealed class ReconnectModel
    {
        // 正在执行重连流程的 SessionId 集合，防止并发重复触发
        private readonly System.Collections.Generic.HashSet<string> _reconnectingSessionIds
            = new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// 标记指定 SessionId 正在执行重连流程。
        /// 已在重连中的 SessionId 不允许重复进入，返回 false。
        /// </summary>
        public bool TryMarkReconnecting(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }
            return _reconnectingSessionIds.Add(sessionId);
        }

        /// <summary>
        /// 清除指定 SessionId 的重连中标记，在重连流程完成（成功或失败）后调用。
        /// </summary>
        public void ClearReconnecting(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }
            _reconnectingSessionIds.Remove(sessionId);
        }

        /// <summary>
        /// 判断指定 SessionId 当前是否正在执行重连流程。
        /// </summary>
        public bool IsReconnecting(string sessionId)
        {
            return !string.IsNullOrEmpty(sessionId) && _reconnectingSessionIds.Contains(sessionId);
        }
    }
}