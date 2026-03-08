using StellarNet.Client.Adapter;
using StellarNet.Client.Network;
using StellarNet.Client.Sender;
using StellarNet.Client.Session;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.Reconnect
{
    /// <summary>
    /// 客户端重连模块 Handle，驱动自动重连状态机。
    /// 断线事件由 MirrorClientAdapter.OnDisconnectedFromServer 触发，Handle 订阅此事件。
    /// 重连状态机阶段：Idle → WaitingInterval → Connecting → Succeeded/Failed。
    /// 达到最大重连次数后进入 Failed 状态，清空 SessionId，引导用户重新登录。
    /// Tick 由 ClientInfrastructure.Update 驱动，负责间隔计时与自动发起重连请求。
    /// 重连请求必须走统一 ClientGlobalMessageSender，禁止绕过发送链手写 MessageId 与序列化流程。
    /// </summary>
    public sealed class ClientReconnectHandle
    {
        private readonly ClientReconnectModel _model;
        private readonly ClientSessionContext _sessionContext;
        private readonly ClientGlobalMessageRegistrar _registrar;
        private readonly MirrorClientAdapter _adapter;
        private readonly ClientGlobalMessageSender _globalSender;
        private readonly string _serverAddress;
        private readonly int _serverPort;
        private readonly float _reconnectIntervalSeconds;

        public event System.Action<string> OnReconnectSucceeded;
        public event System.Action<string> OnReconnectFailed;
        public event System.Action<int, int> OnReconnectAttempt;

        public ClientReconnectHandle(
            ClientReconnectModel model,
            ClientSessionContext sessionContext,
            ClientGlobalMessageRegistrar registrar,
            MirrorClientAdapter adapter,
            ClientGlobalMessageSender globalSender,
            string serverAddress,
            int serverPort,
            float reconnectIntervalSeconds)
        {
            if (model == null)
            {
                Debug.LogError("[ClientReconnectHandle] 构造失败：model 为 null。");
                return;
            }

            if (sessionContext == null)
            {
                Debug.LogError("[ClientReconnectHandle] 构造失败：sessionContext 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[ClientReconnectHandle] 构造失败：registrar 为 null。");
                return;
            }

            if (adapter == null)
            {
                Debug.LogError("[ClientReconnectHandle] 构造失败：adapter 为 null。");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[ClientReconnectHandle] 构造失败：globalSender 为 null。");
                return;
            }

            if (string.IsNullOrEmpty(serverAddress))
            {
                Debug.LogError("[ClientReconnectHandle] 构造失败：serverAddress 为空。");
                return;
            }

            if (serverPort <= 0 || serverPort > 65535)
            {
                Debug.LogError($"[ClientReconnectHandle] 构造失败：serverPort={serverPort} 非法。");
                return;
            }

            _model = model;
            _sessionContext = sessionContext;
            _registrar = registrar;
            _adapter = adapter;
            _globalSender = globalSender;
            _serverAddress = serverAddress;
            _serverPort = serverPort;
            _reconnectIntervalSeconds = reconnectIntervalSeconds > 0f ? reconnectIntervalSeconds : 3f;
        }

        public void RegisterAll()
        {
            _registrar.Register<S2C_ReconnectResult>(OnS2C_ReconnectResult);
            _adapter.OnDisconnectedFromServer += OnDisconnectedFromServer;
            _adapter.OnConnectedToServer += OnConnectedToServer;
        }

        public void UnregisterAll()
        {
            _registrar.Unregister<S2C_ReconnectResult>();
            _adapter.OnDisconnectedFromServer -= OnDisconnectedFromServer;
            _adapter.OnConnectedToServer -= OnConnectedToServer;
        }

        /// <summary>
        /// 主循环 Tick，驱动重连间隔计时与自动发起下一次连接尝试。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_model.Phase != ClientReconnectModel.ReconnectPhase.WaitingInterval)
            {
                return;
            }

            _model.TickInterval(deltaTime);
            if (_model.IntervalRemaining > 0f)
            {
                return;
            }

            BeginConnectAttempt();
        }

        /// <summary>
        /// 仅在已登录状态下，断线才触发自动重连流程。
        /// 未登录断线属于正常情况，不应错误进入重连状态机。
        /// </summary>
        private void OnDisconnectedFromServer()
        {
            if (!_sessionContext.IsLoggedIn)
            {
                Debug.Log("[ClientReconnectHandle] 断线时未处于登录状态，不触发自动重连。");
                return;
            }

            if (_model.Phase != ClientReconnectModel.ReconnectPhase.Idle &&
                _model.Phase != ClientReconnectModel.ReconnectPhase.Succeeded)
            {
                Debug.LogWarning($"[ClientReconnectHandle] 断线时重连状态机不处于可进入状态，当前阶段={_model.Phase}。");
                return;
            }

            _model.Reset();
            Debug.Log($"[ClientReconnectHandle] 检测到断线，开始自动重连流程，SessionId={_sessionContext.SessionId}。");
            BeginNextAttempt();
        }

        /// <summary>
        /// 底层连接建立成功后，若当前处于重连 Connecting 阶段，立即走统一 Sender 发送 C2S_Reconnect。
        /// 这里不再手写 MessageId、序列化或 Envelope，防止破坏统一发送链。
        /// </summary>
        private void OnConnectedToServer()
        {
            if (_model.Phase != ClientReconnectModel.ReconnectPhase.Connecting)
            {
                return;
            }

            if (!_sessionContext.IsLoggedIn)
            {
                Debug.LogError("[ClientReconnectHandle] 重连时连接建立成功但 SessionId 已失效，重连流程终止。");
                MarkFailed("SessionId 已失效，请重新登录");
                return;
            }

            var reconnectMsg = new C2S_Reconnect
            {
                SessionId = _sessionContext.SessionId
            };

            _globalSender.Send(reconnectMsg);
            Debug.Log(
                $"[ClientReconnectHandle] 重连请求已发送，SessionId={_sessionContext.SessionId}，第 {_model.AttemptCount} 次尝试。");
        }

        private void OnS2C_ReconnectResult(S2C_ReconnectResult message)
        {
            if (message == null)
            {
                Debug.LogError("[ClientReconnectHandle] OnS2C_ReconnectResult 失败：message 为 null。");
                return;
            }

            if (!message.Success)
            {
                Debug.Log($"[ClientReconnectHandle] 重连失败（服务端拒绝），原因={message.FailReason}。");
                _sessionContext.ClearSession();
                MarkFailed(message.FailReason);
                return;
            }

            if (message.TargetState == "InRoom")
            {
                if (string.IsNullOrEmpty(message.TargetRoomId))
                {
                    Debug.LogError("[ClientReconnectHandle] 重连成功结果异常：TargetState=InRoom 但 TargetRoomId 为空。");
                    MarkFailed("服务端返回的目标房间无效");
                    return;
                }

                _sessionContext.SetCurrentRoomId(message.TargetRoomId);
            }
            else
            {
                _sessionContext.ClearCurrentRoomId();
            }

            _model.SetPhase(ClientReconnectModel.ReconnectPhase.Succeeded);
            OnReconnectSucceeded?.Invoke(message.TargetState);
            Debug.Log(
                $"[ClientReconnectHandle] 重连成功，TargetState={message.TargetState}，TargetRoomId={message.TargetRoomId}。");
        }

        private void BeginNextAttempt()
        {
            _model.IncrementAttempt();
            if (_model.IsMaxAttemptsReached)
            {
                MarkFailed($"已达最大重连次数（{_model.MaxAttempts}），停止自动重连");
                return;
            }

            OnReconnectAttempt?.Invoke(_model.AttemptCount, _model.MaxAttempts);
            _model.SetPhase(ClientReconnectModel.ReconnectPhase.WaitingInterval);
            _model.SetIntervalRemaining(_reconnectIntervalSeconds);

            Debug.Log(
                $"[ClientReconnectHandle] 开始第 {_model.AttemptCount}/{_model.MaxAttempts} 次重连尝试，间隔={_reconnectIntervalSeconds}s。");
        }

        /// <summary>
        /// 发起底层连接尝试，连接成功后在 OnConnectedToServer 中发送 C2S_Reconnect。
        /// </summary>
        private void BeginConnectAttempt()
        {
            _model.SetPhase(ClientReconnectModel.ReconnectPhase.Connecting);
            _adapter.Connect(_serverAddress, _serverPort);
        }

        private void MarkFailed(string reason)
        {
            _model.SetLastFailReason(reason);
            _model.SetPhase(ClientReconnectModel.ReconnectPhase.Failed);
            _sessionContext.ClearSession();
            OnReconnectFailed?.Invoke(reason);
            Debug.Log($"[ClientReconnectHandle] 重连流程结束（失败），原因={reason}。");
        }
    }
}