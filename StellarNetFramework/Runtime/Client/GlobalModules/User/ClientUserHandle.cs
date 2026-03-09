using StellarNet.Client.Network;
using StellarNet.Client.Session;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.User
{
    /// <summary>
    /// 客户端用户模块 Handle，处理登录结果与踢下线协议。
    /// 登录成功后将服务端签发的 SessionId 写入 ClientSessionContext，这是 SessionId 的唯一写入点。
    /// 被踢下线后清空 ClientSessionContext 的 SessionId，引导客户端重新进入登录流程。
    /// Handle 不直接操作 View，通过 Model 状态变更驱动 View 刷新。
    /// </summary>
    public sealed class ClientUserHandle
    {
        private readonly ClientUserModel _model;
        public ClientUserModel Model => _model;
        private readonly ClientSessionContext _sessionContext;
        private readonly ClientGlobalMessageRegistrar _registrar;

        public event System.Action<string> OnLoginSuccess;
        public event System.Action<string> OnLoginFailed;
        public event System.Action<string> OnKickedOut;

        public ClientUserHandle(
            ClientUserModel model,
            ClientSessionContext sessionContext,
            ClientGlobalMessageRegistrar registrar)
        {
            if (model == null)
            {
                Debug.LogError("[ClientUserHandle] 构造失败：model 为 null。");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientUserHandle] 构造失败：sessionContext 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[ClientUserHandle] 构造失败：registrar 为 null。");
                return;
            }

            _model = model;
            _sessionContext = sessionContext;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar
                .Register<S2C_LoginResult>(OnS2C_LoginResult)
                .Register<S2C_KickOut>(OnS2C_KickOut);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<S2C_LoginResult>()
                .Unregister<S2C_KickOut>();
        }

        private void OnS2C_LoginResult(S2C_LoginResult message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientUserHandle] OnS2C_LoginResult 失败：message 为 null。");
                return;
            }

            if (!message.Success)
            {
                _model.SetLoginFailed(message.FailReason);
                OnLoginFailed?.Invoke(message.FailReason);
                Debug.Log($"[ClientUserHandle] 登录失败，原因={message.FailReason}。");
                return;
            }

            if (string.IsNullOrEmpty(message.SessionId))
            {
                Debug.LogError("[ClientUserHandle] 登录结果异常：Success=true 但 SessionId 为空，已忽略。");
                _model.SetLoginFailed("服务端返回 SessionId 为空");
                OnLoginFailed?.Invoke("服务端返回 SessionId 为空");
                return;
            }

            _sessionContext.SetSessionId(message.SessionId);

            // 当前内置 S2C_LoginResult 不包含 AccountId。
            // 这里按文档约束只维护登录态，不伪造额外字段。
            _model.SetLoggedIn(string.Empty);

            OnLoginSuccess?.Invoke(message.SessionId);
            Debug.Log($"[ClientUserHandle] 登录成功，SessionId={message.SessionId}。");
        }

        private void OnS2C_KickOut(S2C_KickOut message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientUserHandle] OnS2C_KickOut 失败：message 为 null。");
                return;
            }

            string reason = message.Reason ?? string.Empty;
            _sessionContext.ClearSession();
            _model.SetKickedOut(reason);
            OnKickedOut?.Invoke(reason);

            Debug.Log($"[ClientUserHandle] 被踢下线，原因={reason}。");
        }
    }
}