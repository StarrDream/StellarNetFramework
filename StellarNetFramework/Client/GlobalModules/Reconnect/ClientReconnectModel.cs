namespace StellarNet.Client.GlobalModules.Reconnect
{
    /// <summary>
    /// 客户端重连模块 Model，保存自动重连状态机的运行时状态。
    /// 不承载业务逻辑，不直接驱动任何网络操作。
    /// 重连状态机的阶段由此 Model 维护，ClientReconnectHandle 读取此状态驱动重连流程。
    /// 达到最大重连次数后进入 Failed 状态，上层 View 应引导用户手动重新登录。
    /// </summary>
    public sealed class ClientReconnectModel
    {
        /// <summary>
        /// 重连状态机阶段枚举。
        /// </summary>
        public enum ReconnectPhase
        {
            /// <summary>
            /// 空闲，当前无重连流程在进行。
            /// </summary>
            Idle,

            /// <summary>
            /// 等待重连间隔计时结束，即将发起下一次重连尝试。
            /// </summary>
            WaitingInterval,

            /// <summary>
            /// 正在发起重连请求，等待服务端响应。
            /// </summary>
            Connecting,

            /// <summary>
            /// 重连成功，已恢复到正常在线状态。
            /// </summary>
            Succeeded,

            /// <summary>
            /// 重连失败，已达到最大重连次数，停止自动重连。
            /// </summary>
            Failed
        }

        /// <summary>
        /// 当前重连状态机阶段。
        /// </summary>
        public ReconnectPhase Phase { get; private set; }

        /// <summary>
        /// 当前已尝试重连次数。
        /// </summary>
        public int AttemptCount { get; private set; }

        /// <summary>
        /// 最大重连尝试次数，由 ClientNetConfig 配置。
        /// </summary>
        public int MaxAttempts { get; private set; }

        /// <summary>
        /// 当前重连间隔计时剩余秒数。
        /// </summary>
        public float IntervalRemaining { get; private set; }

        /// <summary>
        /// 最近一次重连失败的原因。
        /// </summary>
        public string LastFailReason { get; private set; }

        public ClientReconnectModel(int maxAttempts)
        {
            MaxAttempts = maxAttempts > 0 ? maxAttempts : 3;
            Phase = ReconnectPhase.Idle;
            AttemptCount = 0;
            IntervalRemaining = 0f;
            LastFailReason = string.Empty;
        }

        public void SetPhase(ReconnectPhase phase) => Phase = phase;

        public void IncrementAttempt() => AttemptCount++;

        public void ResetAttempts() => AttemptCount = 0;

        public void SetIntervalRemaining(float seconds) => IntervalRemaining = seconds;

        public void TickInterval(float deltaTime)
        {
            IntervalRemaining -= deltaTime;
            if (IntervalRemaining < 0f)
            {
                IntervalRemaining = 0f;
            }
        }

        public void SetLastFailReason(string reason) => LastFailReason = reason ?? string.Empty;

        /// <summary>
        /// 判断是否已达到最大重连次数。
        /// </summary>
        public bool IsMaxAttemptsReached => AttemptCount >= MaxAttempts;

        /// <summary>
        /// 重置重连状态机到初始状态，在重连成功或手动取消后调用。
        /// </summary>
        public void Reset()
        {
            Phase = ReconnectPhase.Idle;
            AttemptCount = 0;
            IntervalRemaining = 0f;
            LastFailReason = string.Empty;
        }
    }
}
