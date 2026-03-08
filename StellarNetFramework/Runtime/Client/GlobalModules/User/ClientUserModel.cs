namespace StellarNet.Client.GlobalModules.User
{
    /// <summary>
    /// 客户端用户模块 Model，保存本地用户状态。
    /// 不承载业务逻辑，不直接驱动任何网络发送。
    /// AccountId 与 SessionId 均由服务端权威下发，客户端只做本地缓存。
    /// 被踢下线后 IsLoggedIn 变为 false，上层 View 通过轮询或事件驱动读取此状态。
    /// </summary>
    public sealed class ClientUserModel
    {
        /// <summary>
        /// 当前登录的账号 ID，登录成功后写入，登出或被踢后清空。
        /// </summary>
        public string AccountId { get; private set; }

        /// <summary>
        /// 当前是否处于已登录状态。
        /// </summary>
        public bool IsLoggedIn { get; private set; }

        /// <summary>
        /// 最近一次登录失败的原因，供 View 层展示。
        /// 登录成功后清空。
        /// </summary>
        public string LastLoginFailReason { get; private set; }

        /// <summary>
        /// 最近一次被踢下线的原因，供 View 层展示。
        /// </summary>
        public string LastKickReason { get; private set; }

        public ClientUserModel()
        {
            AccountId = string.Empty;
            IsLoggedIn = false;
            LastLoginFailReason = string.Empty;
            LastKickReason = string.Empty;
        }

        /// <summary>
        /// 写入登录成功状态，由 ClientUserHandle 在收到 S2C_LoginResult(Success=true) 时调用。
        /// </summary>
        public void SetLoggedIn(string accountId)
        {
            AccountId = accountId ?? string.Empty;
            IsLoggedIn = true;
            LastLoginFailReason = string.Empty;
        }

        /// <summary>
        /// 写入登录失败状态，由 ClientUserHandle 在收到 S2C_LoginResult(Success=false) 时调用。
        /// </summary>
        public void SetLoginFailed(string reason)
        {
            IsLoggedIn = false;
            LastLoginFailReason = reason ?? string.Empty;
        }

        /// <summary>
        /// 写入被踢下线状态，由 ClientUserHandle 在收到 S2C_KickOut 时调用。
        /// 被踢后清空 AccountId，上层 View 应引导用户重新登录。
        /// </summary>
        public void SetKickedOut(string reason)
        {
            IsLoggedIn = false;
            AccountId = string.Empty;
            LastKickReason = reason ?? string.Empty;
        }

        /// <summary>
        /// 主动登出，清空所有登录状态。
        /// </summary>
        public void SetLoggedOut()
        {
            IsLoggedIn = false;
            AccountId = string.Empty;
            LastLoginFailReason = string.Empty;
        }
    }
}
