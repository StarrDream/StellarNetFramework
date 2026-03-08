using System.Collections.Generic;
using StellarNet.Server.Room.Events;
using StellarNet.Server.Room.Services;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Server.Room.BuiltIn
{
    /// <summary>
    /// 服务端出厂房间基础设置组件。
    /// 这是每个房间实例必须强制挂载的骨架级业务组件，负责维护房间成员、房主、准备状态、
    /// 可开始状态、基础快照与成员快照同步，并对房间内其他组件与全局模块暴露稳定服务契约。
    /// 同房间业务组件之间通过 RoomEventBus 解耦通信；
    /// 全局模块通过 GlobalRoomManager 间接访问本组件，不允许直接穿透组件实例。
    /// </summary>
    public sealed class ServerRoomBaseSettingsComponent :
        ServerRoomAssembler.IInitializableRoomComponent,
        IRoomBaseSettingsService
    {
        public const string StableComponentId = "room.base_settings";

        private readonly ServerGlobalMessageSender _globalSender;
        private readonly ServerRoomMessageSender _roomSender;
        private readonly SessionManager _sessionManager;

        private RoomInstance _room;

        private readonly Dictionary<string, RoomMemberSnapshot> _memberMap =
            new Dictionary<string, RoomMemberSnapshot>();

        private string _ownerSessionId;
        private bool _canStart;
        private bool _isInitialized;

        public string ComponentId => StableComponentId;

        public ServerRoomBaseSettingsComponent(
            ServerGlobalMessageSender globalSender,
            ServerRoomMessageSender roomSender,
            SessionManager sessionManager)
        {
            if (globalSender == null)
            {
                Debug.LogError("[ServerRoomBaseSettingsComponent] 构造失败：globalSender 为 null。");
                return;
            }

            if (roomSender == null)
            {
                Debug.LogError("[ServerRoomBaseSettingsComponent] 构造失败：roomSender 为 null。");
                return;
            }

            if (sessionManager == null)
            {
                Debug.LogError("[ServerRoomBaseSettingsComponent] 构造失败：sessionManager 为 null。");
                return;
            }

            _globalSender = globalSender;
            _roomSender = roomSender;
            _sessionManager = sessionManager;
        }

        public bool Init(RoomInstance roomInstance)
        {
            if (roomInstance == null)
            {
                Debug.LogError("[ServerRoomBaseSettingsComponent] Init 失败：roomInstance 为 null。");
                return false;
            }

            if (_globalSender == null || _roomSender == null || _sessionManager == null)
            {
                Debug.LogError($"[ServerRoomBaseSettingsComponent] Init 失败：RoomId={roomInstance.RoomId}，关键依赖未初始化。");
                return false;
            }

            if (roomInstance.RoomServiceLocator == null)
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] Init 失败：RoomId={roomInstance.RoomId} 的 RoomServiceLocator 为 null。");
                return false;
            }

            if (roomInstance.EventBus == null)
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] Init 失败：RoomId={roomInstance.RoomId} 的 EventBus 为 null。");
                return false;
            }

            _room = roomInstance;
            _ownerSessionId = string.Empty;
            _canStart = false;
            _memberMap.Clear();

            _room.RoomServiceLocator.Register<IRoomBaseSettingsService>(this);
            _isInitialized = true;

            return true;
        }

        public void Deinit()
        {
            if (_room != null && _room.RoomServiceLocator != null)
            {
                _room.RoomServiceLocator.Unregister<IRoomBaseSettingsService>();
            }

            ClearState();
            _room = null;
            _isInitialized = false;
        }

        public IReadOnlyList<ServerRoomAssembler.RoomHandlerBinding> GetHandlerBindings()
        {
            var bindings = new List<ServerRoomAssembler.RoomHandlerBinding>
            {
                new ServerRoomAssembler.RoomHandlerBinding
                {
                    MessageType = typeof(C2S_SetReadyState),
                    Handler = OnC2S_SetReadyState
                },
                new ServerRoomAssembler.RoomHandlerBinding
                {
                    MessageType = typeof(C2S_GetRoomMemberList),
                    Handler = OnC2S_GetRoomMemberList
                }
            };

            return bindings;
        }

        public void OnRoomCreate()
        {
        }

        public void OnRoomWaitStart()
        {
            RecalculateCanStartAndBroadcastIfChanged();
        }

        public void OnRoomStartGame()
        {
        }

        public void OnRoomGameEnding()
        {
        }

        public void OnRoomSettling()
        {
        }

        public void OnTick(float deltaTime)
        {
        }

        public void OnRoomDestroy()
        {
            ClearState();
        }

        /// <summary>
        /// 通知有成员加入房间。
        /// 这里完成骨架级状态维护、快照同步、房间事件发布。
        /// </summary>
        public void NotifyMemberJoined(string sessionId)
        {
            if (!EnsureAvailable("NotifyMemberJoined"))
            {
                return;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] NotifyMemberJoined 失败：RoomId={_room.RoomId}，sessionId 为空。");
                return;
            }

            if (_memberMap.TryGetValue(sessionId, out var existingMember))
            {
                existingMember.IsOnline = true;
                existingMember.IsReady = false;
            }
            else
            {
                _memberMap[sessionId] = new RoomMemberSnapshot
                {
                    SessionId = sessionId,
                    IsOnline = true,
                    IsRoomOwner = false,
                    IsReady = false
                };
            }

            if (string.IsNullOrEmpty(_ownerSessionId))
            {
                _ownerSessionId = sessionId;
            }

            RebuildOwnerFlags();
            RecalculateCanStartAndBroadcastIfChanged();

            SendBaseSnapshotToMember(sessionId);
            SendMemberSnapshotToMember(sessionId);
            BroadcastMemberJoined(sessionId);
            BroadcastMemberSnapshotToRoom();

            _room.EventBus.Publish(new RoomMemberJoinedEvent
            {
                RoomId = _room.RoomId,
                SessionId = sessionId
            });
        }

        /// <summary>
        /// 通知有成员离开房间。
        /// 这里完成成员移除、房主重选、快照广播、房间事件发布。
        /// </summary>
        public void NotifyMemberLeft(string sessionId, string reason)
        {
            if (!EnsureAvailable("NotifyMemberLeft"))
            {
                return;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] NotifyMemberLeft 失败：RoomId={_room.RoomId}，sessionId 为空，Reason={reason}。");
                return;
            }

            bool existed = _memberMap.Remove(sessionId);
            if (!existed)
            {
                Debug.LogWarning(
                    $"[ServerRoomBaseSettingsComponent] NotifyMemberLeft 警告：RoomId={_room.RoomId} 的成员表中不存在 SessionId={sessionId}，仍继续执行后续同步。");
            }

            if (_ownerSessionId == sessionId)
            {
                _ownerSessionId = SelectNextOwnerSessionId();
                if (!string.IsNullOrEmpty(_ownerSessionId))
                {
                    BroadcastOwnerChanged(_ownerSessionId);
                }
            }

            BroadcastMemberLeft(sessionId, reason);
            RebuildOwnerFlags();
            RecalculateCanStartAndBroadcastIfChanged();
            BroadcastMemberSnapshotToRoom();

            _room.EventBus.Publish(new RoomMemberLeftEvent
            {
                RoomId = _room.RoomId,
                SessionId = sessionId,
                Reason = reason ?? string.Empty
            });
        }

        /// <summary>
        /// 通知某成员完成重连接管。
        /// 这里不改变房间成员关系，只负责恢复在线态并向该成员补发基础快照与成员快照。
        /// </summary>
        public void NotifyReconnectRecovered(string sessionId)
        {
            if (!EnsureAvailable("NotifyReconnectRecovered"))
            {
                return;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] NotifyReconnectRecovered 失败：RoomId={_room.RoomId}，sessionId 为空。");
                return;
            }

            if (_memberMap.TryGetValue(sessionId, out var member))
            {
                member.IsOnline = true;
            }
            else
            {
                _memberMap[sessionId] = new RoomMemberSnapshot
                {
                    SessionId = sessionId,
                    IsOnline = true,
                    IsRoomOwner = sessionId == _ownerSessionId,
                    IsReady = false
                };
            }

            SendBaseSnapshotToMember(sessionId);
            SendMemberSnapshotToMember(sessionId);
            BroadcastMemberSnapshotToRoom();
        }

        public List<RoomMemberSnapshot> GetMemberSnapshots()
        {
            var result = new List<RoomMemberSnapshot>(_memberMap.Count);

            foreach (var pair in _memberMap)
            {
                var member = pair.Value;
                result.Add(CloneMemberSnapshot(member));
            }

            return result;
        }

        public string GetOwnerSessionId()
        {
            return _ownerSessionId ?? string.Empty;
        }

        public bool GetCanStart()
        {
            return _canStart;
        }

        public S2C_RoomBaseSettingsSnapshot BuildBaseSnapshot()
        {
            if (!EnsureAvailable("BuildBaseSnapshot"))
            {
                return null;
            }

            return new S2C_RoomBaseSettingsSnapshot
            {
                RoomId = _room.RoomId,
                RoomName = string.Empty,
                Description = string.Empty,
                HasPassword = false,
                MaxMemberCount = 0,
                OwnerSessionId = _ownerSessionId ?? string.Empty,
                CanStart = _canStart
            };
        }

        public void ClearState()
        {
            _memberMap.Clear();
            _ownerSessionId = string.Empty;
            _canStart = false;
        }

        private void OnC2S_SetReadyState(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_SetReadyState"))
            {
                return;
            }

            var message = rawMessage as C2S_SetReadyState;
            if (message == null)
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] OnC2S_SetReadyState 失败：消息类型转换失败，RoomId={roomId}，ConnectionId={connectionId}。");
                return;
            }

            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] OnC2S_SetReadyState 失败：无法通过 ConnectionId={connectionId} 解析 SessionId，RoomId={roomId}。");
                return;
            }

            if (!_memberMap.TryGetValue(sessionId, out var member))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] OnC2S_SetReadyState 失败：SessionId={sessionId} 不在 RoomId={roomId} 的成员表中。");
                return;
            }

            if (member.IsReady == message.IsReady)
            {
                return;
            }

            member.IsReady = message.IsReady;

            var changed = new S2C_MemberReadyStateChanged
            {
                RoomId = _room.RoomId,
                SessionId = sessionId,
                IsReady = message.IsReady
            };
            _roomSender.BroadcastToRoom(_room.RoomId, changed);

            _room.EventBus.Publish(new RoomReadyStateChangedEvent
            {
                RoomId = _room.RoomId,
                SessionId = sessionId,
                IsReady = message.IsReady
            });

            RecalculateCanStartAndBroadcastIfChanged();
        }

        private void OnC2S_GetRoomMemberList(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_GetRoomMemberList"))
            {
                return;
            }

            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] OnC2S_GetRoomMemberList 失败：无法通过 ConnectionId={connectionId} 解析 SessionId，RoomId={roomId}。");
                return;
            }

            SendMemberSnapshotToMember(sessionId);
        }

        private void SendBaseSnapshotToMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] SendBaseSnapshotToMember 失败：RoomId={_room?.RoomId}，sessionId 为空。");
                return;
            }

            var snapshot = BuildBaseSnapshot();
            if (snapshot == null)
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] SendBaseSnapshotToMember 失败：BuildBaseSnapshot 返回 null，RoomId={_room?.RoomId}，SessionId={sessionId}。");
                return;
            }

            _roomSender.SendToSessionInRoom(_room.RoomId, sessionId, snapshot);
        }

        private void SendMemberSnapshotToMember(string sessionId)
        {
            if (!EnsureAvailable("SendMemberSnapshotToMember"))
            {
                return;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] SendMemberSnapshotToMember 失败：RoomId={_room.RoomId}，sessionId 为空。");
                return;
            }

            var snapshot = new S2C_RoomMemberListSnapshot
            {
                RoomId = _room.RoomId,
                Members = BuildMemberSnapshotArray()
            };

            _roomSender.SendToSessionInRoom(_room.RoomId, sessionId, snapshot);
        }

        private void BroadcastMemberSnapshotToRoom()
        {
            if (!EnsureAvailable("BroadcastMemberSnapshotToRoom"))
            {
                return;
            }

            var snapshot = new S2C_RoomMemberListSnapshot
            {
                RoomId = _room.RoomId,
                Members = BuildMemberSnapshotArray()
            };

            _roomSender.BroadcastToRoom(_room.RoomId, snapshot);
        }

        private void BroadcastMemberJoined(string joinedSessionId)
        {
            if (string.IsNullOrEmpty(joinedSessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] BroadcastMemberJoined 失败：RoomId={_room?.RoomId}，joinedSessionId 为空。");
                return;
            }

            var message = new S2C_MemberJoined
            {
                SessionId = joinedSessionId
            };

            _roomSender.BroadcastToRoom(_room.RoomId, message);
        }

        private void BroadcastMemberLeft(string sessionId, string reason)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] BroadcastMemberLeft 失败：RoomId={_room?.RoomId}，sessionId 为空，Reason={reason}。");
                return;
            }

            var message = new S2C_MemberLeft
            {
                SessionId = sessionId,
                Reason = reason ?? string.Empty
            };

            _roomSender.BroadcastToRoom(_room.RoomId, message);
        }

        private void BroadcastOwnerChanged(string newOwnerSessionId)
        {
            if (string.IsNullOrEmpty(newOwnerSessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsComponent] BroadcastOwnerChanged 失败：RoomId={_room?.RoomId}，newOwnerSessionId 为空。");
                return;
            }

            var message = new S2C_RoomOwnerChanged
            {
                RoomId = _room.RoomId,
                NewOwnerSessionId = newOwnerSessionId
            };

            _roomSender.BroadcastToRoom(_room.RoomId, message);
        }

        private void RebuildOwnerFlags()
        {
            foreach (var pair in _memberMap)
            {
                pair.Value.IsRoomOwner = pair.Key == _ownerSessionId;
            }
        }

        private void RecalculateCanStartAndBroadcastIfChanged()
        {
            bool oldCanStart = _canStart;
            _canStart = CalculateCanStart();

            if (oldCanStart == _canStart)
            {
                return;
            }

            var changed = new S2C_RoomCanStartStateChanged
            {
                RoomId = _room.RoomId,
                CanStart = _canStart
            };
            _roomSender.BroadcastToRoom(_room.RoomId, changed);

            _room.EventBus.Publish(new RoomCanStartStateChangedEvent
            {
                RoomId = _room.RoomId,
                CanStart = _canStart
            });
        }

        private bool CalculateCanStart()
        {
            if (_memberMap.Count <= 0)
            {
                return false;
            }

            foreach (var pair in _memberMap)
            {
                var member = pair.Value;
                if (!member.IsOnline)
                {
                    return false;
                }

                if (!member.IsReady)
                {
                    return false;
                }
            }

            return true;
        }

        private string SelectNextOwnerSessionId()
        {
            foreach (var pair in _memberMap)
            {
                return pair.Key;
            }

            return string.Empty;
        }

        private RoomMemberSnapshot[] BuildMemberSnapshotArray()
        {
            var result = new RoomMemberSnapshot[_memberMap.Count];
            int index = 0;

            foreach (var pair in _memberMap)
            {
                result[index] = CloneMemberSnapshot(pair.Value);
                index++;
            }

            return result;
        }

        private string ResolveSessionIdByConnection(ConnectionId connectionId)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            if (session == null)
            {
                return string.Empty;
            }

            return session.SessionId ?? string.Empty;
        }

        private static RoomMemberSnapshot CloneMemberSnapshot(RoomMemberSnapshot source)
        {
            if (source == null)
            {
                return null;
            }

            return new RoomMemberSnapshot
            {
                SessionId = source.SessionId,
                IsOnline = source.IsOnline,
                IsRoomOwner = source.IsRoomOwner,
                IsReady = source.IsReady
            };
        }

        private bool EnsureAvailable(string caller)
        {
            if (!_isInitialized)
            {
                Debug.LogError($"[ServerRoomBaseSettingsComponent] {caller} 失败：组件尚未完成初始化。");
                return false;
            }

            if (_room == null)
            {
                Debug.LogError($"[ServerRoomBaseSettingsComponent] {caller} 失败：内部 _room 为 null。");
                return false;
            }

            return true;
        }
    }
}