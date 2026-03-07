// Assets/StellarNetFramework/Server/Modules/UserModule.cs

using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Network.Router;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Session;

namespace StellarNet.Server.Modules
{
    // 用户模块，负责连接认证、会话签发与登出处理。
    // 是客户端建立连接后第一个必须经过的全局模块。
    // 认证成功后签发 SessionId 并通过 GlobalMessageSender 下发给客户端。
    // 认证失败时直接断开连接，不签发任何会话凭证。
    // 不负责重连逻辑，重连由 ReconnectModule 独立处理。
    public sealed class UserModule : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly ServerGlobalMessageRouter _globalRouter;
        private readonly ServerGlobalMessageSender _globalSender;

        // 认证委托：由业务层注入，框架不内置认证实现。
        // 参数1：ConnectionId 来源连接
        // 参数2：C2S 认证请求消息（业务层自定义协议，框架通过基类引用传入）
        // 返回 true 表示认证通过，false 表示认证失败
        private System.Func<ConnectionId, Shared.Protocol.Base.C2SGlobalMessage, bool> _authValidator;

        // 当前时间戳提供委托，由 GlobalInfrastructure 注入，保证时间源统一
        private System.Func<long> _nowUnixMsProvider;

        public UserModule(
            SessionManager sessionManager,
            ServerGlobalMessageRouter globalRouter,
            ServerGlobalMessageSender globalSender)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[UserModule] 初始化失败：sessionManager 不得为 null");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[UserModule] 初始化失败：globalRouter 不得为 null");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[UserModule] 初始化失败：globalSender 不得为 null");
                return;
            }

            _sessionManager = sessionManager;
            _globalRouter = globalRouter;
            _globalSender = globalSender;
        }

        // 注入认证委托，由业务层在 GlobalInfrastructure 装配阶段调用
        public void SetAuthValidator(
            System.Func<ConnectionId, Shared.Protocol.Base.C2SGlobalMessage, bool> validator)
        {
            if (validator == null)
            {
                Debug.LogError("[UserModule] SetAuthValidator 失败：validator 不得为 null");
                return;
            }

            _authValidator = validator;
        }

        // 注入时间戳提供委托
        public void SetNowUnixMsProvider(System.Func<long> provider)
        {
            if (provider == null)
            {
                Debug.LogError("[UserModule] SetNowUnixMsProvider 失败：provider 不得为 null");
                return;
            }

            _nowUnixMsProvider = provider;
        }

        // 注册认证协议 Handler，由 GlobalInfrastructure 在装配阶段调用
        // 参数 authMessageType：业务层自定义的认证协议类型
        public void RegisterAuthHandler(System.Type authMessageType)
        {
            if (authMessageType == null)
            {
                Debug.LogError("[UserModule] RegisterAuthHandler 失败：authMessageType 不得为 null");
                return;
            }

            _globalRouter.Register(authMessageType, OnAuthMessageReceived);
        }

        // 注销认证协议 Handler，由 GlobalInfrastructure 在关停阶段调用
        public void UnregisterAuthHandler(System.Type authMessageType)
        {
            if (authMessageType == null)
                return;

            _globalRouter.Unregister(authMessageType);
        }

        // 认证协议处理入口
        private void OnAuthMessageReceived(
            ConnectionId connectionId,
            Shared.Protocol.Base.C2SGlobalMessage message)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[UserModule] OnAuthMessageReceived：ConnectionId 无效，已忽略。" +
                    $"ConnectionId={connectionId}");
                return;
            }

            // 检查该连接是否已持有有效会话，防止重复认证
            var existingSession = _sessionManager.GetSessionByConnection(connectionId);
            if (existingSession != null && existingSession.IsOnline)
            {
                Debug.LogWarning(
                    $"[UserModule] 重复认证警告：ConnectionId={connectionId} 已持有有效会话 " +
                    $"SessionId={existingSession.SessionId}，本次认证请求已忽略。");
                return;
            }

            // 执行业务层认证校验
            if (_authValidator == null)
            {
                Debug.LogError(
                    $"[UserModule] 认证失败：认证委托未注入，ConnectionId={connectionId}，" +
                    $"请确认业务层已在装配阶段注入 AuthValidator。");
                return;
            }

            var authPassed = _authValidator.Invoke(connectionId, message);
            if (!authPassed)
            {
                Debug.LogWarning(
                    $"[UserModule] 认证失败：业务层认证校验未通过，ConnectionId={connectionId}，" +
                    $"连接将被保留，由业务层决定是否主动断开。");
                return;
            }

            // 认证通过，签发新会话
            var nowMs = _nowUnixMsProvider != null
                ? _nowUnixMsProvider.Invoke()
                : System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var sessionData = _sessionManager.CreateSession(connectionId, nowMs);
            if (sessionData == null)
            {
                Debug.LogError(
                    $"[UserModule] 会话签发失败：SessionManager.CreateSession 返回 null，" +
                    $"ConnectionId={connectionId}");
                return;
            }

            // 触发会话签发成功回调，由业务层决定向客户端下发何种协议
            _onSessionCreated?.Invoke(connectionId, sessionData.SessionId);
        }

        // 会话签发成功回调，由业务层注入，用于向客户端下发认证结果协议
        private System.Action<ConnectionId, SessionId> _onSessionCreated;

        public void SetOnSessionCreatedCallback(System.Action<ConnectionId, SessionId> callback)
        {
            if (callback == null)
            {
                Debug.LogError("[UserModule] SetOnSessionCreatedCallback 失败：callback 不得为 null");
                return;
            }

            _onSessionCreated = callback;
        }
    }
}
