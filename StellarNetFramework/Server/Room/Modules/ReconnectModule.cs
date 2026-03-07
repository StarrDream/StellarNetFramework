// Assets/StellarNetFramework/Server/Modules/ReconnectModule.cs

using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Network.Adapter;
using StellarNet.Server.Network.Router;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Session;
using StellarNet.Server.Room;

namespace StellarNet.Server.Modules
{
    // 重连模块，负责客户端断线重连的完整接管流程。
    // 重连认证成功后必须按顺序完成：
    //   1. SessionManager.TakeoverSession() 完成会话接管
    //   2. 若会话处于房间内，RoomInstance.SetMemberOnline() 更新在线连接映射
    //   3. 向新连接下发重连成功协议（由业务层回调决定协议内容）
    // 以上三步必须原子完成，任意一步失败则整体回滚并断开新连接。
    // 不负责重连后的状态补偿，状态补偿由各房间业务组件在收到重连事件后自行处理。
    public sealed class ReconnectModule : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly GlobalRoomManager _roomManager;
        private readonly ServerGlobalMessageRouter _globalRouter;
        private readonly ServerGlobalMessageSender _globalSender;
        private readonly INetworkAdapter _adapter;

        // 重连凭证解析委托：从重连请求消息中提取客户端回传的 SessionId
        private System.Func<Shared.Protocol.Base.C2SGlobalMessage, SessionId> _sessionIdResolver;

        // 当前时间戳提供委托
        private System.Func<long> _nowUnixMsProvider;

        public ReconnectModule(
            SessionManager sessionManager,
            GlobalRoomManager roomManager,
            ServerGlobalMessageRouter globalRouter,
            ServerGlobalMessageSender globalSender,
            INetworkAdapter adapter)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[ReconnectModule] 初始化失败：sessionManager 不得为 null");
                return;
            }

            if (roomManager == null)
            {
                Debug.LogError("[ReconnectModule] 初始化失败：roomManager 不得为 null");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[ReconnectModule] 初始化失败：globalRouter 不得为 null");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[ReconnectModule] 初始化失败：globalSender 不得为 null");
                return;
            }

            if (adapter == null)
            {
                Debug.LogError("[ReconnectModule] 初始化失败：adapter 不得为 null");
                return;
            }

            _sessionManager = sessionManager;
            _roomManager = roomManager;
            _globalRouter = globalRouter;
            _globalSender = globalSender;
            _adapter = adapter;
        }

        // 注册重连协议 Handler
        public void RegisterReconnectHandler(System.Type reconnectMsgType)
        {
            if (reconnectMsgType == null)
            {
                Debug.LogError("[ReconnectModule] RegisterReconnectHandler 失败：reconnectMsgType 不得为 null");
                return;
            }

            _globalRouter.Register(reconnectMsgType, OnReconnectMessageReceived);
        }

        // 注销重连协议 Handler
        public void UnregisterReconnectHandler(System.Type reconnectMsgType)
        {
            if (reconnectMsgType == null)
                return;

            _globalRouter.Unregister(reconnectMsgType);
        }

        // 注入重连凭证解析委托
        public void SetSessionIdResolver(
            System.Func<Shared.Protocol.Base.C2SGlobalMessage, SessionId> resolver)
        {
            if (resolver == null)
            {
                Debug.LogError("[ReconnectModule] SetSessionIdResolver 失败：resolver 不得为 null");
                return;
            }

            _sessionIdResolver = resolver;
        }

        // 注入时间戳提供委托
        public void SetNowUnixMsProvider(System.Func<long> provider)
        {
            if (provider == null)
            {
                Debug.LogError("[ReconnectModule] SetNowUnixMsProvider 失败：provider 不得为 null");
                return;
            }

            _nowUnixMsProvider = provider;
        }

        // 重连协议处理入口
        private void OnReconnectMessageReceived(
            ConnectionId connectionId,
            Shared.Protocol.Base.C2SGlobalMessage message)
        {
            if (!connectionId.IsValid)
            {
                Debug.LogError(
                    $"[ReconnectModule] 重连失败：ConnectionId 无效，ConnectionId={connectionId}");
                return;
            }

            if (_sessionIdResolver == null)
            {
                Debug.LogError(
                    $"[ReconnectModule] 重连失败：SessionId 解析委托未注入，ConnectionId={connectionId}");
                return;
            }

            var sessionId = _sessionIdResolver.Invoke(message);
            if (!sessionId.IsValid)
            {
                Debug.LogError(
                    $"[ReconnectModule] 重连失败：解析到的 SessionId 无效，ConnectionId={connectionId}");
                _adapter.Disconnect(connectionId);
                return;
            }

            var nowMs = _nowUnixMsProvider != null
                ? _nowUnixMsProvider.Invoke()
                : System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 步骤一：会话接管
            var session = _sessionManager.TakeoverSession(sessionId, connectionId, nowMs);
            if (session == null)
            {
                Debug.LogError(
                    $"[ReconnectModule] 重连失败：会话接管失败，SessionId={sessionId}，" +
                    $"ConnectionId={connectionId}，断开新连接。");
                _adapter.Disconnect(connectionId);
                return;
            }

            // 步骤二：若会话处于房间内，更新房间在线连接映射
            if (session.IsInRoom)
            {
                var room = _roomManager.GetRoutableRoom(session.CurrentRoomId);
                if (room == null)
                {
                    Debug.LogWarning(
                        $"[ReconnectModule] 重连警告：SessionId={sessionId} 的会话记录房间 " +
                        $"{session.CurrentRoomId} 已不可路由，清理会话房间归属，继续完成重连。");
                    _sessionManager.ClearSessionRoom(sessionId);
                }
                else
                {
                    room.SetMemberOnline(sessionId.Value, connectionId);
                }
            }

            // 步骤三：触发重连成功回调，由业务层决定向客户端下发何种协议
            _onReconnectSuccess?.Invoke(connectionId, sessionId, session.CurrentRoomId);
        }

        // 重连成功回调，由业务层注入
        private System.Action<ConnectionId, SessionId, string> _onReconnectSuccess;

        public void SetOnReconnectSuccessCallback(
            System.Action<ConnectionId, SessionId, string> callback)
        {
            if (callback == null)
            {
                Debug.LogError("[ReconnectModule] SetOnReconnectSuccessCallback 失败：callback 不得为 null");
                return;
            }

            _onReconnectSuccess = callback;
        }
    }
}
