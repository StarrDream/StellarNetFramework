using StellarNet.Server.Idempotent;
using StellarNet.Server.Network;
using StellarNet.Server.Room;
using StellarNet.Server.Room.BuiltIn;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.ServiceLocator;
using UnityEngine;

namespace StellarNet.Server.GlobalModules.RoomDispatcher
{
    /// <summary>
    /// 房间调度模块 Handle，处理建房、加房、离房、获取房间列表等调度请求。
    /// 全局模块不直接穿透房间内部组件，而是通过 GlobalRoomManager 代理访问房间骨架组件。
    /// 房间基础设置组件是出厂强制挂载组件，默认组件清单必须包含它。
    /// </summary>
    public class RoomDispatcherHandle : IGlobalService
    {
        private readonly SessionManager _sessionManager;
        private readonly GlobalRoomManager _roomManager;
        private readonly RoomDispatcherModel _model;
        private readonly IdempotentCache _idempotentCache;
        private readonly ServerGlobalMessageSender _globalSender;
        private readonly ServerRoomMessageSender _roomSender;
        private readonly GlobalMessageRegistrar _registrar;

        public RoomDispatcherHandle(
            SessionManager sessionManager,
            GlobalRoomManager roomManager,
            RoomDispatcherModel model,
            IdempotentCache idempotentCache,
            ServerGlobalMessageSender globalSender,
            ServerRoomMessageSender roomSender,
            GlobalMessageRegistrar registrar)
        {
            if (sessionManager == null)
            {
                Debug.LogError("[RoomDispatcherHandle] 构造失败：sessionManager 为 null。");
                return;
            }

            if (roomManager == null)
            {
                Debug.LogError("[RoomDispatcherHandle] 构造失败：roomManager 为 null。");
                return;
            }

            if (model == null)
            {
                Debug.LogError("[RoomDispatcherHandle] 构造失败：model 为 null。");
                return;
            }

            if (idempotentCache == null)
            {
                Debug.LogError("[RoomDispatcherHandle] 构造失败：idempotentCache 为 null。");
                return;
            }

            if (globalSender == null)
            {
                Debug.LogError("[RoomDispatcherHandle] 构造失败：globalSender 为 null。");
                return;
            }

            if (roomSender == null)
            {
                Debug.LogError("[RoomDispatcherHandle] 构造失败：roomSender 为 null。");
                return;
            }

            if (registrar == null)
            {
                Debug.LogError("[RoomDispatcherHandle] 构造失败：registrar 为 null。");
                return;
            }

            _sessionManager = sessionManager;
            _roomManager = roomManager;
            _model = model;
            _idempotentCache = idempotentCache;
            _globalSender = globalSender;
            _roomSender = roomSender;
            _registrar = registrar;
        }

        public void RegisterAll()
        {
            _registrar
                .Register<C2S_CreateRoom>(OnC2S_CreateRoom)
                .Register<C2S_JoinRoom>(OnC2S_JoinRoom)
                .Register<C2S_LeaveRoom>(OnC2S_LeaveRoom)
                .Register<C2S_GetRoomList>(OnC2S_GetRoomList)
                .Register<C2S_GetRoomInfo>(OnC2S_GetRoomInfo);
        }

        public void UnregisterAll()
        {
            _registrar
                .Unregister<C2S_CreateRoom>()
                .Unregister<C2S_JoinRoom>()
                .Unregister<C2S_LeaveRoom>()
                .Unregister<C2S_GetRoomList>()
                .Unregister<C2S_GetRoomInfo>();
        }

        private void OnC2S_CreateRoom(ConnectionId connectionId, C2S_CreateRoom message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[RoomDispatcherHandle] 建房失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (string.IsNullOrEmpty(message.IdempotentToken))
            {
                Debug.LogError(
                    $"[RoomDispatcherHandle] 建房失败：IdempotentToken 为空，SessionId={session.SessionId}，ConnectionId={connectionId}。");
                SendCreateRoomFail(session.SessionId, "IdempotentToken 不能为空");
                return;
            }

            if (_idempotentCache.TryGetResult(message.IdempotentToken, out var cachedResult))
            {
                var cachedResponse = cachedResult as S2C_CreateRoomResult;
                if (cachedResponse != null)
                {
                    _globalSender.SendToSession(session.SessionId, cachedResponse);
                    Debug.Log(
                        $"[RoomDispatcherHandle] 建房幂等命中，复用缓存结果，Token={message.IdempotentToken}，SessionId={session.SessionId}。");
                }

                return;
            }

            if (!_idempotentCache.TryOccupy(message.IdempotentToken))
            {
                Debug.LogWarning(
                    $"[RoomDispatcherHandle] 建房请求正在处理中，Token={message.IdempotentToken}，SessionId={session.SessionId}，已忽略。");
                return;
            }

            if (session.IsInRoom)
            {
                Debug.LogError(
                    $"[RoomDispatcherHandle] 建房失败：SessionId={session.SessionId} 当前已在房间 {session.CurrentRoomId} 中，不允许重复建房。");
                var failResult = new S2C_CreateRoomResult { Success = false, FailReason = "当前已在房间中" };
                _idempotentCache.SetResult(message.IdempotentToken, failResult);
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            if (!_model.TryMarkCreating(session.SessionId))
            {
                Debug.LogWarning($"[RoomDispatcherHandle] 建房并发重入，SessionId={session.SessionId}，已忽略。");
                return;
            }

            var roomId = StellarNet.Shared.Identity.RoomId.Generate(message.RoomName ?? "Room");
            var settings = BuildRoomSettings(message, roomId.Value);
            if (settings == null)
            {
                _model.ClearCreating(session.SessionId);
                Debug.LogError(
                    $"[RoomDispatcherHandle] 建房失败：IRoomSettings 构建失败，SessionId={session.SessionId}，RoomId={roomId.Value}。");
                var failResult = new S2C_CreateRoomResult { Success = false, FailReason = "房间配置构建失败" };
                _idempotentCache.SetResult(message.IdempotentToken, failResult);
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            string[] componentIds = GetDefaultRoomComponentIds();
            var room = _roomManager.CreateRoom(roomId.Value, settings, componentIds);
            if (room == null)
            {
                _model.ClearCreating(session.SessionId);
                Debug.LogError(
                    $"[RoomDispatcherHandle] 建房失败：GlobalRoomManager.CreateRoom 返回 null，SessionId={session.SessionId}，RoomId={roomId.Value}。");
                var failResult = new S2C_CreateRoomResult { Success = false, FailReason = "房间创建失败" };
                _idempotentCache.SetResult(message.IdempotentToken, failResult);
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            room.AddMember(session.SessionId, connectionId);
            session.BindRoom(roomId.Value);

            bool notifyJoinedSuccess = _roomManager.TryNotifyRoomMemberJoined(roomId.Value, session.SessionId);
            if (!notifyJoinedSuccess)
            {
                _model.ClearCreating(session.SessionId);
                Debug.LogError(
                    $"[RoomDispatcherHandle] 建房失败：房间骨架成员加入通知失败，SessionId={session.SessionId}，RoomId={roomId.Value}。");
                _roomManager.DestroyRoom(roomId.Value, "建房后骨架同步失败");
                var failResult = new S2C_CreateRoomResult { Success = false, FailReason = "房间骨架初始化失败" };
                _idempotentCache.SetResult(message.IdempotentToken, failResult);
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            _model.ClearCreating(session.SessionId);

            var successResult = new S2C_CreateRoomResult
            {
                Success = true,
                RoomId = roomId.Value,
                FailReason = string.Empty
            };

            _idempotentCache.SetResult(message.IdempotentToken, successResult);
            _globalSender.SendToSession(session.SessionId, successResult);

            Debug.Log($"[RoomDispatcherHandle] 建房成功，RoomId={roomId.Value}，SessionId={session.SessionId}。");
        }

        private void OnC2S_JoinRoom(ConnectionId connectionId, C2S_JoinRoom message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[RoomDispatcherHandle] 加房失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (string.IsNullOrEmpty(message.RoomId))
            {
                Debug.LogError($"[RoomDispatcherHandle] 加房失败：RoomId 为空，SessionId={session.SessionId}。");
                SendJoinRoomFail(session.SessionId, "RoomId 不能为空");
                return;
            }

            if (session.IsInRoom)
            {
                Debug.LogError(
                    $"[RoomDispatcherHandle] 加房失败：SessionId={session.SessionId} 当前已在房间 {session.CurrentRoomId} 中。");
                SendJoinRoomFail(session.SessionId, "当前已在房间中");
                return;
            }

            if (!_model.TryMarkJoining(session.SessionId))
            {
                Debug.LogWarning($"[RoomDispatcherHandle] 加房并发重入，SessionId={session.SessionId}，已忽略。");
                return;
            }

            var room = _roomManager.GetRoom(message.RoomId);
            if (room == null)
            {
                _model.ClearJoining(session.SessionId);
                Debug.LogError(
                    $"[RoomDispatcherHandle] 加房失败：RoomId={message.RoomId} 不存在，SessionId={session.SessionId}。");
                SendJoinRoomFail(session.SessionId, "房间不存在");
                return;
            }

            if (!ValidateRoomPassword(room, message.Password))
            {
                _model.ClearJoining(session.SessionId);
                Debug.LogError(
                    $"[RoomDispatcherHandle] 加房失败：密码错误，RoomId={message.RoomId}，SessionId={session.SessionId}。");
                SendJoinRoomFail(session.SessionId, "房间密码错误");
                return;
            }

            room.AddMember(session.SessionId, connectionId);
            session.BindRoom(message.RoomId);

            bool notifyJoinedSuccess = _roomManager.TryNotifyRoomMemberJoined(message.RoomId, session.SessionId);
            if (!notifyJoinedSuccess)
            {
                room.RemoveMember(session.SessionId);
                session.UnbindRoom();
                _model.ClearJoining(session.SessionId);
                Debug.LogError(
                    $"[RoomDispatcherHandle] 加房失败：房间骨架成员加入通知失败，RoomId={message.RoomId}，SessionId={session.SessionId}。");
                SendJoinRoomFail(session.SessionId, "房间骨架同步失败");
                return;
            }

            _model.ClearJoining(session.SessionId);

            string[] componentIds = room.GetComponentIds();
            var joinResult = new S2C_JoinRoomResult
            {
                Success = true,
                RoomId = message.RoomId,
                RoomComponentIds = componentIds ?? new string[0],
                FailReason = string.Empty
            };

            _globalSender.SendToSession(session.SessionId, joinResult);

            Debug.Log($"[RoomDispatcherHandle] 加房成功，RoomId={message.RoomId}，SessionId={session.SessionId}。");
        }

        private void OnC2S_LeaveRoom(ConnectionId connectionId, C2S_LeaveRoom message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[RoomDispatcherHandle] 离房失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (!session.IsInRoom)
            {
                Debug.LogWarning($"[RoomDispatcherHandle] 离房请求：SessionId={session.SessionId} 当前不在任何房间中，已忽略。");
                return;
            }

            string roomId = session.CurrentRoomId;
            var room = _roomManager.GetRoom(roomId);
            if (room == null)
            {
                session.UnbindRoom();
                Debug.LogWarning(
                    $"[RoomDispatcherHandle] 离房时目标房间已不存在，SessionId={session.SessionId}，RoomId={roomId}，已清空房间绑定。");
                return;
            }

            var memberLeft = new S2C_MemberLeft
            {
                SessionId = session.SessionId,
                Reason = "主动离开"
            };
            _roomSender.BroadcastToRoom(roomId, memberLeft);

            room.RemoveMember(session.SessionId);
            session.UnbindRoom();

            bool notifyLeftSuccess = _roomManager.TryNotifyRoomMemberLeft(roomId, session.SessionId, "主动离开");
            if (!notifyLeftSuccess)
            {
                Debug.LogError(
                    $"[RoomDispatcherHandle] 离房后房间骨架成员离开通知失败，RoomId={roomId}，SessionId={session.SessionId}。");
            }

            if (room.MemberCount == 0)
            {
                _roomManager.DestroyRoom(roomId, "所有成员已离开");
            }

            Debug.Log($"[RoomDispatcherHandle] 离房成功，RoomId={roomId}，SessionId={session.SessionId}。");
        }

        private void OnC2S_GetRoomList(ConnectionId connectionId, C2S_GetRoomList message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[RoomDispatcherHandle] 获取房间列表失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            var rooms = GetRoomList(message.PageIndex, message.PageSize, out int totalCount);
            var result = new S2C_RoomListResult
            {
                Rooms = rooms,
                TotalCount = totalCount
            };
            _globalSender.SendToSession(session.SessionId, result);
        }

        private void OnC2S_GetRoomInfo(ConnectionId connectionId, C2S_GetRoomInfo message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                Debug.LogError($"[RoomDispatcherHandle] 获取房间信息失败：ConnectionId={connectionId} 未绑定有效会话。");
                return;
            }

            if (string.IsNullOrEmpty(message.RoomId))
            {
                Debug.LogError($"[RoomDispatcherHandle] 获取房间信息失败：RoomId 为空，SessionId={session.SessionId}。");
                return;
            }

            var room = _roomManager.GetRoom(message.RoomId);
            if (room == null)
            {
                var failResult = new S2C_RoomInfoResult { Success = false, FailReason = "房间不存在" };
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            var result = BuildRoomInfoResult(room);
            _globalSender.SendToSession(session.SessionId, result);
        }

        private void SendCreateRoomFail(string sessionId, string reason)
        {
            var result = new S2C_CreateRoomResult { Success = false, FailReason = reason };
            _globalSender.SendToSession(sessionId, result);
        }

        private void SendJoinRoomFail(string sessionId, string reason)
        {
            var result = new S2C_JoinRoomResult
            {
                Success = false,
                RoomId = string.Empty,
                RoomComponentIds = new string[0],
                FailReason = reason
            };
            _globalSender.SendToSession(sessionId, result);
        }

        protected virtual StellarNet.Shared.RoomSettings.IRoomSettings BuildRoomSettings(
            C2S_CreateRoom message, string roomId)
        {
            Debug.LogWarning(
                $"[RoomDispatcherHandle] BuildRoomSettings 未重写，返回 null，RoomId={roomId}。开发者必须重写此方法提供具体 IRoomSettings 实现。");
            return null;
        }

        /// <summary>
        /// 返回房间出厂默认组件清单。
        /// 房间基础设置组件属于骨架级强制挂载组件，默认房间不允许缺失此组件。
        /// </summary>
        protected virtual string[] GetDefaultRoomComponentIds()
        {
            return new[]
            {
                // 由于 Component 已被删除，这里改为引用 Handle 的 StableComponentId
                ServerRoomBaseSettingsHandle.StableComponentId
            };
        }

        protected virtual RoomBriefInfo[] GetRoomList(int pageIndex, int pageSize, out int totalCount)
        {
            totalCount = 0;
            return new RoomBriefInfo[0];
        }

        protected virtual bool ValidateRoomPassword(RoomInstance room, string inputPassword)
        {
            return true;
        }

        protected virtual S2C_RoomInfoResult BuildRoomInfoResult(RoomInstance room)
        {
            return new S2C_RoomInfoResult
            {
                Success = true,
                RoomId = room.RoomId,
                RoomName = string.Empty,
                CurrentMemberCount = room.MemberCount,
                MaxMemberCount = 0,
                HasPassword = false,
                FailReason = string.Empty
            };
        }
    }
}