using StellarNet.Server.Network;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.GlobalModules.User
{
    /// <summary>
    /// 用户模块 Handle，处理登录请求与账号踢下线流程。
    /// 职责严格限定为：接收 C2S_Login、执行鉴权、创建/接管 Session、维护 UserModel、下发 S2C_LoginResult。
    /// 鉴权逻辑通过虚方法 ValidateCredential 开放给开发者重写，框架默认实现直接通过（开发模式）。
    /// 同一账号重复登录时，旧 Session 必须被踢下线，旧连接收到 S2C_KickOut 后客户端清除本地 SessionId。
    /// RegisterAccountSession 的返回值是旧 SessionId，是踢人的唯一数据来源，不得重复查询或丢弃。
    /// </summary>
    public class UserHandle : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly UserModel _userModel;
        private readonly ServerGlobalMessageSender _globalSender;
        private readonly GlobalMessageRegistrar _registrar;

        public UserHandle(
            SessionManager sessionManager,
            UserModel userModel,
            ServerGlobalMessageSender globalSender,
            GlobalMessageRegistrar registrar)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[UserHandle] 构造失败：sessionManager 为 null。");
                return;
            }

            if (userModel == null)
            {
                Debug.LogError("[UserHandle] 构造失败：userModel 为 null。");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[UserHandle] 构造失败：globalSender 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[UserHandle] 构造失败：registrar 为 null。");
                return;
            }

            _sessionManager = sessionManager;
            _userModel = userModel;
            _globalSender = globalSender;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar.Register<C2S_Login>(OnC2S_Login);
        }

        public void UnregisterAll()
        {
            _registrar.Unregister<C2S_Login>();
        }

        /// <summary>
        /// 处理客户端登录请求。
        /// 执行顺序：鉴权 → 创建 Session → 注册账号映射（同时获取旧 SessionId）→ 踢下旧连接 → 下发结果。
        /// RegisterAccountSession 返回旧 SessionId 是踢人的唯一数据来源，必须在注册后立即处理，
        /// 不允许在注册前单独调用 GetSessionId 查询再注册，防止查询与注册之间出现竞态窗口。
        /// </summary>
        private void OnC2S_Login(ConnectionId connectionId, C2S_Login message)
        {
            if (string.IsNullOrEmpty(message.AccountId))
            {
                Debug.LogError($"[UserHandle] 登录失败：AccountId 为空，ConnectionId={connectionId}。");
                SendLoginFail(connectionId, "AccountId 不能为空");
                return;
            }

            if (string.IsNullOrEmpty(message.Credential))
            {
                Debug.LogError(
                    $"[UserHandle] 登录失败：Credential 为空，AccountId={message.AccountId}，ConnectionId={connectionId}。");
                SendLoginFail(connectionId, "Credential 不能为空");
                return;
            }

            // 鉴权校验，开发者可重写 ValidateCredential 实现具体鉴权逻辑
            bool authPassed = ValidateCredential(message.AccountId, message.Credential);
            if (!authPassed)
            {
                Debug.LogError($"[UserHandle] 登录失败：鉴权不通过，AccountId={message.AccountId}，ConnectionId={connectionId}。");
                SendLoginFail(connectionId, "账号或密码错误");
                return;
            }

            // 创建新 Session，必须在注册账号映射之前完成，确保 SessionId 有效
            var newSession = _sessionManager.CreateSession(connectionId);
            if (newSession == null)
            {
                Debug.LogError(
                    $"[UserHandle] 登录失败：Session 创建失败，AccountId={message.AccountId}，ConnectionId={connectionId}。");
                SendLoginFail(connectionId, "服务器内部错误");
                return;
            }

            // 注册账号与 Session 双向映射，返回值为被顶替的旧 SessionId（若同账号已登录）。
            // 必须使用返回值驱动踢人流程，不允许在此之前单独调用 GetSessionId 查询，
            // 防止查询与注册之间出现并发竞态窗口导致旧 Session 漏踢。
            string oldSessionId = _userModel.RegisterAccountSession(message.AccountId, newSession.SessionId);
            if (!string.IsNullOrEmpty(oldSessionId))
            {
                KickOutOldSession(oldSessionId, "账号在其他设备登录");
            }

            // 下发登录成功结果
            var result = new S2C_LoginResult
            {
                Success = true,
                SessionId = newSession.SessionId,
                FailReason = string.Empty
            };
            _globalSender.SendToSession(newSession.SessionId, result);
            Debug.Log(
                $"[UserHandle] 登录成功，AccountId={message.AccountId}，SessionId={newSession.SessionId}，ConnectionId={connectionId}。");
        }

        /// <summary>
        /// 踢下旧 Session 对应的连接，下发 S2C_KickOut 后由客户端清除本地 SessionId。
        /// 踢人消息必须在 SessionManager.DestroySession 之前发送，
        /// 销毁后 SessionManager 无法再通过 SessionId 找到对应连接，消息将无法投递。
        /// </summary>
        private void KickOutOldSession(string oldSessionId, string reason)
        {
            // 必须先发送踢人消息，再销毁 Session
            // 若先销毁，SendToSession 内部查询 Session 时会找不到记录，导致踢人消息无法投递
            var kickMsg = new S2C_KickOut { Reason = reason };
            _globalSender.SendToSession(oldSessionId, kickMsg);
            _userModel.UnregisterSession(oldSessionId);
            _sessionManager.DestroySession(oldSessionId);
            Debug.Log($"[UserHandle] 旧 Session 已踢下线，SessionId={oldSessionId}，原因={reason}。");
        }

        /// <summary>
        /// 下发登录失败结果。
        /// 通过创建临时 Session 完成单次响应投递，投递完成后立即销毁临时 Session，不污染 Session 表。
        /// </summary>
        private void SendLoginFail(ConnectionId connectionId, string reason)
        {
            var tempSession = _sessionManager.CreateSession(connectionId);
            if (tempSession == null)
            {
                Debug.LogError($"[UserHandle] 无法下发登录失败结果：临时 Session 创建失败，ConnectionId={connectionId}，原因={reason}。");
                return;
            }

            var result = new S2C_LoginResult
            {
                Success = false,
                SessionId = string.Empty,
                FailReason = reason
            };
            _globalSender.SendToSession(tempSession.SessionId, result);
            _sessionManager.DestroySession(tempSession.SessionId);
        }

        /// <summary>
        /// 鉴权逻辑，开发者可重写此方法实现具体鉴权。
        /// 框架默认实现直接返回 true（开发模式），生产环境必须重写。
        /// </summary>
        protected virtual bool ValidateCredential(string accountId, string credential)
        {
            return true;
        }
    }
}