using System;
using System.Linq;
using StellarNet.Server.Idempotent;
using StellarNet.Server.Network;
using StellarNet.Server.Room;
using StellarNet.Server.Room.BuiltIn;
using StellarNet.Server.Room.Settings;
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
                .Register<C2S_LeaveRoom>(OnC2S_LeaveRoom) // [新增] 处理全局离房请求
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
            if (session == null) return;

            if (string.IsNullOrEmpty(message.IdempotentToken))
            {
                SendCreateRoomFail(session.SessionId, "IdempotentToken 不能为空");
                return;
            }

            if (_idempotentCache.TryGetResult(message.IdempotentToken, out var cachedResult))
            {
                if (cachedResult is S2C_CreateRoomResult cachedResponse)
                {
                    _globalSender.SendToSession(session.SessionId, cachedResponse);
                }
                return;
            }

            if (!_idempotentCache.TryOccupy(message.IdempotentToken)) return;

            if (session.IsInRoom)
            {
                var failResult = new S2C_CreateRoomResult { Success = false, FailReason = "当前已在房间中" };
                _idempotentCache.SetResult(message.IdempotentToken, failResult);
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            if (!_model.TryMarkCreating(session.SessionId)) return;

            var roomId = StellarNet.Shared.Identity.RoomId.Generate(message.RoomName ?? "Room");
            var settings = BuildRoomSettings(message, roomId.Value);
            if (settings == null)
            {
                _model.ClearCreating(session.SessionId);
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
                var failResult = new S2C_CreateRoomResult { Success = false, FailReason = "房间创建失败" };
                _idempotentCache.SetResult(message.IdempotentToken, failResult);
                _globalSender.SendToSession(session.SessionId, failResult);
                return;
            }

            // 1. 发送回执 (Global)
            string[] actualComponentIds = room.GetComponentIds();
            var successResult = new S2C_CreateRoomResult
            {
                Success = true,
                RoomId = roomId.Value,
                RoomComponentIds = actualComponentIds ?? new string[0],
                FailReason = string.Empty
            };
            _idempotentCache.SetResult(message.IdempotentToken, successResult);
            _globalSender.SendToSession(session.SessionId, successResult);

            // 2. 加入房间 (Room)
            room.AddMember(session.SessionId, connectionId);
            session.BindRoom(roomId.Value);
            bool notifyJoinedSuccess = _roomManager.TryNotifyRoomMemberJoined(roomId.Value, session.SessionId);
            _model.ClearCreating(session.SessionId);

            if (!notifyJoinedSuccess)
            {
                _roomManager.DestroyRoom(roomId.Value, "建房后骨架同步失败");
            }
        }

        private void OnC2S_JoinRoom(ConnectionId connectionId, C2S_JoinRoom message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null) return;

            if (string.IsNullOrEmpty(message.RoomId))
            {
                SendJoinRoomFail(session.SessionId, "RoomId 不能为空");
                return;
            }

            if (session.IsInRoom)
            {
                SendJoinRoomFail(session.SessionId, "当前已在房间中");
                return;
            }

            if (!_model.TryMarkJoining(session.SessionId)) return;

            var room = _roomManager.GetRoom(message.RoomId);
            if (room == null)
            {
                _model.ClearJoining(session.SessionId);
                SendJoinRoomFail(session.SessionId, "房间不存在");
                return;
            }

            if (!ValidateRoomPassword(room, message.Password))
            {
                _model.ClearJoining(session.SessionId);
                SendJoinRoomFail(session.SessionId, "房间密码错误");
                return;
            }

            if (room.MemberCount >= room.MaxMemberCount)
            {
                _model.ClearJoining(session.SessionId);
                SendJoinRoomFail(session.SessionId, "房间已满");
                return;
            }

            // 1. 发送回执 (Global)
            string[] componentIds = room.GetComponentIds();
            var joinResult = new S2C_JoinRoomResult
            {
                Success = true,
                RoomId = message.RoomId,
                RoomComponentIds = componentIds ?? new string[0],
                FailReason = string.Empty
            };
            _globalSender.SendToSession(session.SessionId, joinResult);

            // 2. 加入房间 (Room)
            room.AddMember(session.SessionId, connectionId);
            session.BindRoom(message.RoomId);
            bool notifyJoinedSuccess = _roomManager.TryNotifyRoomMemberJoined(message.RoomId, session.SessionId);
            _model.ClearJoining(session.SessionId);

            if (!notifyJoinedSuccess)
            {
                room.RemoveMember(session.SessionId);
                session.UnbindRoom();
                var kickMsg = new S2C_KickedFromRoom { RoomId = message.RoomId, ByOwnerSessionId = "System" };
                _globalSender.SendToSessionWithTargetRoom(session.SessionId, kickMsg, message.RoomId);
            }
        }

        // [新增] 处理全局离房请求
        private void OnC2S_LeaveRoom(ConnectionId connectionId, C2S_LeaveRoom message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null) return;

            // 1. 校验状态
            if (!session.IsInRoom)
            {
                // 容错：如果服务端认为不在房间，直接返回成功让客户端切回大厅
                _globalSender.SendToSession(session.SessionId, new S2C_LeaveRoomResult { Success = true });
                return;
            }

            string roomId = session.CurrentRoomId;
            
            // 2. 执行移除
            // GlobalRoomManager.RemoveMember 会触发 RoomBaseSettings.NotifyMemberLeft
            // 进而触发房间内广播 S2C_MemberLeft
            _roomManager.RemoveMember(session.SessionId, "主动离开");

            // 3. 发送全局回执
            // 通知请求者本人切换状态机到 InLobby
            _globalSender.SendToSession(session.SessionId, new S2C_LeaveRoomResult { Success = true });

            Debug.Log($"[RoomDispatcherHandle] 离房处理完成，RoomId={roomId}，SessionId={session.SessionId}。");
        }

        private void OnC2S_GetRoomList(ConnectionId connectionId, C2S_GetRoomList message)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null) return;

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
            if (session == null) return;

            if (string.IsNullOrEmpty(message.RoomId)) return;

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
                RoomComponentIds = Array.Empty<string>(),
                FailReason = reason
            };
            _globalSender.SendToSession(sessionId, result);
        }

        protected virtual StellarNet.Shared.RoomSettings.IRoomSettings BuildRoomSettings(
            C2S_CreateRoom message, string roomId)
        {
            return new GeneralRoomSettings
            {
                RoomName = message.RoomName,
                MaxMemberCount = message.MaxMemberCount > 0 ? message.MaxMemberCount : 10,
                Password = message.Password
            };
        }

        protected virtual string[] GetDefaultRoomComponentIds()
        {
            return new[]
            {
                ServerRoomBaseSettingsHandle.StableComponentId
            };
        }

        protected virtual RoomBriefInfo[] GetRoomList(int pageIndex, int pageSize, out int totalCount)
        {
            var rooms = _roomManager.GetPagedRoomList(pageIndex, pageSize, out totalCount);
            var result = new RoomBriefInfo[rooms.Count];
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                string roomName = "Unknown Room";
                int maxMembers = 0;
                bool hasPassword = false;

                if (r.Settings is GeneralRoomSettings generalSettings)
                {
                    roomName = generalSettings.RoomName;
                    maxMembers = generalSettings.MaxMemberCount;
                    hasPassword = !string.IsNullOrEmpty(generalSettings.Password);
                }

                result[i] = new RoomBriefInfo
                {
                    RoomId = r.RoomId,
                    RoomName = roomName,
                    CurrentMemberCount = r.MemberCount,
                    MaxMemberCount = maxMembers,
                    HasPassword = hasPassword
                };
            }
            return result;
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
