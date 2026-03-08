using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.BuiltIn
{
    // ────────────────────────────────────────────────────────────────
    // 认证模块内置协议聚合脚本
    // 框架保留号段：0 - 999
    // 覆盖：登录请求、登录结果
    // 客户端上行协议命名：C2S_XXX
    // 服务端下行协议命名：S2C_XXX
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 客户端发起登录请求。
    /// 属于全局域上行协议，不依赖房间上下文。
    /// 客户端在底层连接建立后、本地无有效 SessionId 时发送此协议。
    /// </summary>
    [MessageId(0)]
    public sealed class C2S_Login : C2SGlobalMessage
    {
        /// <summary>
        /// 账号标识，由开发者业务层定义具体含义（用户名、UID、Token 等）。
        /// </summary>
        public string AccountId;

        /// <summary>
        /// 登录凭证，由开发者业务层定义具体鉴权方式（密码、Token、第三方凭证等）。
        /// </summary>
        public string Credential;
    }

    /// <summary>
    /// 服务端返回登录结果。
    /// 属于全局域下行协议，在 Authenticating 状态下接收。
    /// 登录成功时服务端签发新 SessionId，客户端必须保存用于后续重连。
    /// </summary>
    [MessageId(1)]
    public sealed class S2C_LoginResult : S2CGlobalMessage
    {
        /// <summary>
        /// 登录是否成功。
        /// </summary>
        public bool Success;

        /// <summary>
        /// 服务端签发的会话标识，仅在 Success=true 时有效。
        /// 客户端必须持久化此值用于断线重连，不得自行修改其内容。
        /// </summary>
        public string SessionId;

        /// <summary>
        /// 失败原因描述，仅在 Success=false 时有效，供 UI 层展示。
        /// </summary>
        public string FailReason;
    }

    /// <summary>
    /// 服务端主动通知客户端会话已被踢下线。
    /// 属于全局域下行协议，服务端在同一账号异地登录或管理员踢人时下发。
    /// 客户端收到后应清除本地 SessionId 并回到登录界面。
    /// </summary>
    [MessageId(2)]
    public sealed class S2C_KickOut : S2CGlobalMessage
    {
        /// <summary>
        /// 踢出原因，供 UI 层展示。
        /// </summary>
        public string Reason;
    }
}