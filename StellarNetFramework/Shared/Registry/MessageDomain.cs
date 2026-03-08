namespace StellarNet.Shared.Registry
{
    /// <summary>
    /// 消息域归属枚举，由 MessageRegistry 从四协议基类直接推导，不允许手工声明覆盖。
    /// Global 表示全局域，不依赖房间上下文。Room 表示房间域，必须绑定有效房间上下文。
    /// </summary>
    public enum MessageDomain
    {
        /// <summary>
        /// 全局域，不依赖当前房间归属上下文即可成立。
        /// </summary>
        Global,

        /// <summary>
        /// 房间域，必须绑定有效的房间运行时上下文才能成立。
        /// </summary>
        Room
    }
}