using System.Collections.Generic;

namespace StellarNet.Server.GlobalModules.User
{
    /// <summary>
    /// 用户模块 Model，保存服务端维护的在线用户运行时状态。
    /// Model 只承载数据，不承载业务逻辑，不直接驱动任何网络发送。
    /// AccountId 与 SessionId 的映射关系由此 Model 维护，防止同一账号重复登录。
    /// 同一账号重复登录时，旧 Session 必须被踢下线，新 Session 接管。
    /// </summary>
    public sealed class UserModel
    {
        // SessionId → AccountId 映射，用于快速通过 SessionId 查询账号
        private readonly Dictionary<string, string> _sessionToAccount
            = new Dictionary<string, string>();

        // AccountId → SessionId 映射，用于防止同一账号重复登录
        private readonly Dictionary<string, string> _accountToSession
            = new Dictionary<string, string>();

        /// <summary>
        /// 注册账号与 Session 的双向映射关系。
        /// 若该账号已存在旧 Session 映射，返回旧 SessionId 供调用方执行踢下线流程。
        /// </summary>
        public string RegisterAccountSession(string accountId, string sessionId)
        {
            string existingSessionId = null;

            if (_accountToSession.TryGetValue(accountId, out var oldSessionId))
            {
                // 同一账号已有 Session，返回旧 SessionId 供调用方踢下线
                existingSessionId = oldSessionId;
                _sessionToAccount.Remove(oldSessionId);
            }

            _accountToSession[accountId] = sessionId;
            _sessionToAccount[sessionId] = accountId;

            return existingSessionId;
        }

        /// <summary>
        /// 注销指定 SessionId 的账号映射关系，在会话销毁时调用。
        /// </summary>
        public void UnregisterSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            if (_sessionToAccount.TryGetValue(sessionId, out var accountId))
            {
                _accountToSession.Remove(accountId);
                _sessionToAccount.Remove(sessionId);
            }
        }

        /// <summary>
        /// 通过 SessionId 查询对应的 AccountId。
        /// 查询失败返回 null。
        /// </summary>
        public string GetAccountId(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }
            _sessionToAccount.TryGetValue(sessionId, out var accountId);
            return accountId;
        }

        /// <summary>
        /// 通过 AccountId 查询当前绑定的 SessionId。
        /// 查询失败返回 null。
        /// </summary>
        public string GetSessionId(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return null;
            }
            _accountToSession.TryGetValue(accountId, out var sessionId);
            return sessionId;
        }

        /// <summary>
        /// 判断指定账号当前是否已在线。
        /// </summary>
        public bool IsAccountOnline(string accountId)
        {
            return !string.IsNullOrEmpty(accountId) && _accountToSession.ContainsKey(accountId);
        }

        /// <summary>
        /// 获取当前在线用户数量。
        /// </summary>
        public int OnlineCount => _accountToSession.Count;
    }
}
