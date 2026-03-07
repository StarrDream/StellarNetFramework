// Assets/StellarNetFramework/Server/Modules/RoomDispatcherModule.cs

using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Identity;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Network.Router;
using StellarNet.Server.Network.Sender;
using StellarNet.Server.Session;
using StellarNet.Server.Room;
using StellarNet.Server.Room.Component;

namespace StellarNet.Server.Modules
{
    // 房间调度模块，负责建房、加房、离房与踢出的全局协调。
    // 是唯一允许调用 GlobalRoomManager.CreateRoom() 的模块。
    // 加房成功后必须同步更新 SessionManager 的会话房间归属与 RoomInstance 的成员集合。
    // 离房与踢出必须同步清理以上两处状态，保证会话与房间状态的一致性。
    // 不负责房间内业务逻辑，房间内业务由各 IServerRoomComponent 实现。
    public sealed class RoomDispatcherModule : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly GlobalRoomManager _roomManager;
        private readonly ServerGlobalMessageRouter _globalRouter;
        private readonly ServerGlobalMessageSender _globalSender;

        // 房间组件工厂委托：由业务层注入，根据建房请求构建组件列表。
        // 框架不内置任何房间业务组件，组件列表完全由业务层决定。
        private System.Func<
            ConnectionId,
            Shared.Protocol.Base.C2SGlobalMessage,
            IReadOnlyList<IServerRoomComponent>> _roomComponentFactory;

        public RoomDispatcherModule(
            SessionManager sessionManager,
            GlobalRoomManager roomManager,
            ServerGlobalMessageRouter globalRouter,
            ServerGlobalMessageSender globalSender)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[RoomDispatcherModule] 初始化失败：sessionManager 不得为 null");
                return;
            }

            if (roomManager == null)
            {
                Debug.LogError("[RoomDispatcherModule] 初始化失败：roomManager 不得为 null");
                return;
            }

            if (globalRouter == null)
            {
                Debug.LogError("[RoomDispatcherModule] 初始化失败：globalRouter 不得为 null");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[RoomDispatcherModule] 初始化失败：globalSender 不得为 null");
                return;
            }

            _sessionManager = sessionManager;
            _roomManager = roomManager;
            _globalRouter = globalRouter;
            _globalSender = globalSender;
        }

        // 注入房间组件工厂委托
        public void SetRoomComponentFactory(
            System.Func<ConnectionId,
                Shared.Protocol.Base.C2SGlobalMessage,
                IReadOnlyList<IServerRoomComponent>> factory)
        {
            if (factory == null)
            {
                Debug.LogError("[RoomDispatcherModule] SetRoomComponentFactory 失败：factory 不得为 null");
                return;
            }

            _roomComponentFactory = factory;
        }

        // 注册建房/加房/离房协议 Handler，由 GlobalInfrastructure 在装配阶段调用
        public void RegisterHandlers(
            System.Type createRoomMsgType,
            System.Type joinRoomMsgType,
            System.Type leaveRoomMsgType)
        {
            if (createRoomMsgType != null)
                _globalRouter.Register(createRoomMsgType, OnCreateRoomMessageReceived);

            if (joinRoomMsgType != null)
                _globalRouter.Register(joinRoomMsgType, OnJoinRoomMessageReceived);

            if (leaveRoomMsgType != null)
                _globalRouter.Register(leaveRoomMsgType, OnLeaveRoomMessageReceived);
        }

        // 注销协议 Handler
        public void UnregisterHandlers(
            System.Type createRoomMsgType,
            System.Type joinRoomMsgType,
            System.Type leaveRoomMsgType)
        {
            if (createRoomMsgType != null)
                _globalRouter.Unregister(createRoomMsgType);

            if (joinRoomMsgType != null)
                _globalRouter.Unregister(joinRoomMsgType);

            if (leaveRoomMsgType != null)
                _globalRouter.Unregister(leaveRoomMsgType);
        }

        // 建房处理入口
        private void OnCreateRoomMessageReceived(
            ConnectionId connectionId,
            Shared.Protocol.Base.C2SGlobalMessage message)
        {
            var session = _sessionManager.GetSessionByConnection(connectionId);
            if (session == null)
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] 建房失败：ConnectionId={connectionId} 未绑定有效会话，" +
                    $"请确认客户端已完成认证。");
                return;
            }

            if (session.IsInRoom)
            {
                Debug.LogWarning(
                    $"[RoomDispatcherModule] 建房失败：SessionId={session.SessionId} 已在房间 " +
                    $"{session.CurrentRoomId} 中，不允许重复建房。");
                return;
            }

            if (_roomComponentFactory == null)
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] 建房失败：房间组件工厂委托未注入，" +
                    $"SessionId={session.SessionId}");
                return;
            }

            var components = _roomComponentFactory.Invoke(connectionId, message);
            if (components == null || components.Count == 0)
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] 建房失败：组件工厂返回空组件列表，" +
                    $"SessionId={session.SessionId}");
                return;
            }

            var nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var room = _roomManager.CreateRoom(components, nowMs);
            if (room == null)
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] 建房失败：GlobalRoomManager.CreateRoom 返回 null，" +
                    $"SessionId={session.SessionId}");
                return;
            }

            // 同步更新会话房间归属与房间成员集合
            _sessionManager.SetSessionRoom(session.SessionId, room.RoomId);
            room.AddMember(session.SessionId.Value);
            room.SetMemberOnline(session.SessionId.Value, connectionId);

            // 触发建房成功回调，由业务层决定向客户端下发何种协议
            _onRoomCreated?.Invoke(connectionId, session.SessionId, room.RoomId);
        }

        // 加房处理入口
        private void OnJoinRoomMessageReceived(
            ConnectionId connectionId,
            Shared.Protocol.Base.C2SGlobalMessage message)
        {
            var session = _sessionManager.GetSessionByConnection(connectionId);
            if (session == null)
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] 加房失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (session.IsInRoom)
            {
                Debug.LogWarning(
                    $"[RoomDispatcherModule] 加房失败：SessionId={session.SessionId} 已在房间 " +
                    $"{session.CurrentRoomId} 中，不允许重复加房。");
                return;
            }

            // 目标 RoomId 由业务层从消息体中解析后通过回调提供
            if (_joinRoomIdResolver == null)
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] 加房失败：JoinRoomId 解析委托未注入，" +
                    $"SessionId={session.SessionId}");
                return;
            }

            var targetRoomId = _joinRoomIdResolver.Invoke(message);
            if (string.IsNullOrEmpty(targetRoomId))
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] 加房失败：解析到的目标 RoomId 为空，" +
                    $"SessionId={session.SessionId}");
                return;
            }

            var room = _roomManager.GetRoutableRoom(targetRoomId);
            if (room == null)
            {
                Debug.LogWarning(
                    $"[RoomDispatcherModule] 加房失败：目标 RoomId={targetRoomId} 不存在或不可路由，" +
                    $"SessionId={session.SessionId}");
                return;
            }

            // 同步更新会话房间归属与房间成员集合
            _sessionManager.SetSessionRoom(session.SessionId, room.RoomId);
            room.AddMember(session.SessionId.Value);
            room.SetMemberOnline(session.SessionId.Value, connectionId);

            _onRoomJoined?.Invoke(connectionId, session.SessionId, room.RoomId);
        }

        // 离房处理入口
        private void OnLeaveRoomMessageReceived(
            ConnectionId connectionId,
            Shared.Protocol.Base.C2SGlobalMessage message)
        {
            var session = _sessionManager.GetSessionByConnection(connectionId);
            if (session == null)
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] 离房失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (!session.IsInRoom)
            {
                Debug.LogWarning(
                    $"[RoomDispatcherModule] 离房失败：SessionId={session.SessionId} 当前不在任何房间中。");
                return;
            }

            var roomId = session.CurrentRoomId;
            var room = _roomManager.GetRoutableRoom(roomId);

            // 无论房间是否仍可路由，都必须清理会话房间归属
            _sessionManager.ClearSessionRoom(session.SessionId);

            if (room != null)
                room.RemoveMember(session.SessionId.Value);

            _onRoomLeft?.Invoke(connectionId, session.SessionId, roomId);
        }

        // 主动踢出指定会话，由业务逻辑（如房主踢人）触发
        public void KickMember(SessionId targetSessionId, string reason)
        {
            var session = _sessionManager.GetSessionById(targetSessionId);
            if (session == null)
            {
                Debug.LogError(
                    $"[RoomDispatcherModule] KickMember 失败：SessionId={targetSessionId} 不存在");
                return;
            }

            if (!session.IsInRoom)
            {
                Debug.LogWarning(
                    $"[RoomDispatcherModule] KickMember 警告：SessionId={targetSessionId} 不在任何房间中，" +
                    $"本次踢出已忽略。");
                return;
            }

            var roomId = session.CurrentRoomId;
            var room = _roomManager.GetRoutableRoom(roomId);

            _sessionManager.ClearSessionRoom(targetSessionId);

            if (room != null)
                room.RemoveMember(targetSessionId.Value);

            _onMemberKicked?.Invoke(targetSessionId, roomId, reason);
        }

        // 业务回调委托，由业务层注入
        private System.Action<ConnectionId, SessionId, string> _onRoomCreated;
        private System.Action<ConnectionId, SessionId, string> _onRoomJoined;
        private System.Action<ConnectionId, SessionId, string> _onRoomLeft;
        private System.Action<SessionId, string, string> _onMemberKicked;
        private System.Func<Shared.Protocol.Base.C2SGlobalMessage, string> _joinRoomIdResolver;

        public void SetCallbacks(
            System.Action<ConnectionId, SessionId, string> onRoomCreated,
            System.Action<ConnectionId, SessionId, string> onRoomJoined,
            System.Action<ConnectionId, SessionId, string> onRoomLeft,
            System.Action<SessionId, string, string> onMemberKicked,
            System.Func<Shared.Protocol.Base.C2SGlobalMessage, string> joinRoomIdResolver)
        {
            _onRoomCreated = onRoomCreated;
            _onRoomJoined = onRoomJoined;
            _onRoomLeft = onRoomLeft;
            _onMemberKicked = onMemberKicked;
            _joinRoomIdResolver = joinRoomIdResolver;
        }
    }
}
