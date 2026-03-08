namespace StellarNet.Shared.Registry
{
    /// <summary>
    /// 消息方向枚举，由 MessageRegistry 从四协议基类直接推导，不允许手工声明覆盖。
    /// C2S 表示客户端上行，S2C 表示服务端下行。
    /// </summary>
    public enum MessageDirection
    {
        /// <summary>
        /// 客户端上行，即客户端发往服务端的协议。
        /// </summary>
        C2S,

        /// <summary>
        /// 服务端下行，即服务端发往客户端的协议。
        /// </summary>
        S2C
    }
}