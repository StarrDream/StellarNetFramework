namespace StellarNet.Client.State
{
    /// <summary>
    /// 客户端主状态机枚举，用于表达当前应用所处的核心业务阶段。
    /// 状态机用于控制协议过滤、UI 路由与生命周期边界。
    /// </summary>
    public enum ClientAppState
    {
        /// <summary>
        /// 未连接或已断开，不处理任何业务协议。
        /// </summary>
        Disconnected,
        
        /// <summary>
        /// 认证中，正在等待登录或重连结果。
        /// </summary>
        Authenticating,
        
        /// <summary>
        /// 在大厅中，可处理全局域协议，拒绝在线房间域协议。
        /// </summary>
        InLobby,
        
        /// <summary>
        /// 在房间中，可处理全局域与当前房间域协议。
        /// </summary>
        InRoom,
        
        /// <summary>
        /// 在回放模式中，拒绝所有在线房间域协议，仅允许白名单内的全局域协议。
        /// </summary>
        InReplay
    }
}