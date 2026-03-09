using System.Collections.Generic;
using StellarNet.Server.Network;
using StellarNet.Server.Room.Events;
using StellarNet.Server.Room.Services;
using StellarNet.Server.Room.Settings;
using StellarNet.Server.Sender;
using StellarNet.Server.Session;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Server.Room.BuiltIn
{
    public sealed class ServerRoomBaseSettingsHandle :
        ServerRoomAssembler.IInitializableRoomComponent,
        IRoomBaseSettingsService
    {
        public const string StableComponentId = "room.base_settings";
        public string ComponentId => StableComponentId;

        private readonly ServerGlobalMessageSender _globalSender;
        private readonly ServerRoomMessageSender _roomSender;
        private readonly SessionManager _sessionManager;
        private readonly GlobalRoomManager _globalRoomManager;

        private ServerRoomBaseSettingsModel _model;
        private RoomInstance _room;
        private bool _isInitialized;

        public ServerRoomBaseSettingsHandle(
            ServerGlobalMessageSender globalSender,
            ServerRoomMessageSender roomSender,
            SessionManager sessionManager,
            GlobalRoomManager globalRoomManager)
        {
            _globalSender = globalSender;
            _roomSender = roomSender;
            _sessionManager = sessionManager;
            _globalRoomManager = globalRoomManager;
        }

        public bool Init(RoomInstance roomInstance)
        {
            if (roomInstance == null) return false;

            _room = roomInstance;
            _model = new ServerRoomBaseSettingsModel();

            if (_room.Settings is GeneralRoomSettings settings)
            {
                _model.SetBaseInfo(settings.RoomName, string.Empty, settings.MaxMemberCount);
            }

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
            return new List<ServerRoomAssembler.RoomHandlerBinding>
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
                },
                new ServerRoomAssembler.RoomHandlerBinding
                {
                    MessageType = typeof(C2S_KickRoomMember),
                    Handler = OnC2S_KickRoomMember
                },
                // [修复] 注册重连就绪协议，完成重连握手闭环
                new ServerRoomAssembler.RoomHandlerBinding
                {
                    MessageType = typeof(C2S_ReconnectRoomReady),
                    Handler = OnC2S_ReconnectRoomReady
                }
            };
        }

        public void OnRoomCreate()
        {
        }

        public void OnRoomWaitStart() => RecalculateCanStartAndBroadcastIfChanged();

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

        public void OnRoomDestroy() => ClearState();

        public void NotifyMemberJoined(string sessionId)
        {
            if (!EnsureAvailable("NotifyMemberJoined")) return;

            _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: false);

            if (string.IsNullOrEmpty(_model.OwnerSessionId))
            {
                _model.SetOwner(sessionId);
            }
            else if (_model.OwnerSessionId == sessionId)
            {
                _model.SetOwner(sessionId);
            }

            RecalculateCanStartAndBroadcastIfChanged();

            SendBaseSnapshotToMember(sessionId);
            SendMemberSnapshotToMember(sessionId);

            var joinMsg = new S2C_MemberJoined { SessionId = sessionId };
            _roomSender.BroadcastToRoom(_room.RoomId, joinMsg);
            BroadcastMemberSnapshotToRoom();

            _room.EventBus.Publish(new RoomMemberJoinedEvent
            {
                RoomId = _room.RoomId,
                SessionId = sessionId
            });
        }

        public void NotifyMemberLeft(string sessionId, string reason)
        {
            if (!EnsureAvailable("NotifyMemberLeft")) return;

            _model.RemoveMember(sessionId);

            if (_model.OwnerSessionId == sessionId)
            {
                string nextOwner = _model.SelectNextOwnerSessionId();
                _model.SetOwner(nextOwner);
                if (!string.IsNullOrEmpty(nextOwner))
                {
                    BroadcastOwnerChanged(nextOwner);
                }
            }

            BroadcastMemberLeft(sessionId, reason);
            RecalculateCanStartAndBroadcastIfChanged();
            BroadcastMemberSnapshotToRoom();

            _room.EventBus.Publish(new RoomMemberLeftEvent
            {
                RoomId = _room.RoomId,
                SessionId = sessionId,
                Reason = reason ?? string.Empty
            });
        }

        public void NotifyReconnectRecovered(string sessionId)
        {
            if (!EnsureAvailable("NotifyReconnectRecovered")) return;

            var member = _model.GetMember(sessionId);
            if (member != null)
            {
                _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: member.IsReady);
            }
            else
            {
                _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: false);
                if (_model.OwnerSessionId == sessionId) _model.SetOwner(sessionId);
            }

            SendBaseSnapshotToMember(sessionId);
            SendMemberSnapshotToMember(sessionId);
            BroadcastMemberSnapshotToRoom();
        }

        public List<RoomMemberSnapshot> GetMemberSnapshots() => _model?.GetAllMembers() ?? new List<RoomMemberSnapshot>();
        public string GetOwnerSessionId() => _model?.OwnerSessionId ?? string.Empty;
        public bool GetCanStart() => _model?.CanStart ?? false;

        public S2C_RoomBaseSettingsSnapshot BuildBaseSnapshot()
        {
            if (!EnsureAvailable("BuildBaseSnapshot")) return null;

            return new S2C_RoomBaseSettingsSnapshot
            {
                RoomId = _room.RoomId,
                RoomName = _model.RoomName,
                MaxMemberCount = _model.MaxMemberCount,
                OwnerSessionId = _model.OwnerSessionId,
                CanStart = _model.CanStart
            };
        }

        public void ClearState() => _model?.Clear();

        private void OnC2S_SetReadyState(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_SetReadyState")) return;

            var message = rawMessage as C2S_SetReadyState;
            if (message == null) return;

            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId)) return;

            var member = _model.GetMember(sessionId);
            if (member == null) return;

            if (member.IsReady == message.IsReady) return;

            _model.AddOrUpdateMember(sessionId, member.IsOnline, message.IsReady);

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
            if (!EnsureAvailable("OnC2S_GetRoomMemberList")) return;

            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId)) return;

            SendMemberSnapshotToMember(sessionId);
        }

        private void OnC2S_KickRoomMember(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_KickRoomMember")) return;

            var message = rawMessage as C2S_KickRoomMember;
            if (message == null) return;

            string requesterSessionId = ResolveSessionIdByConnection(connectionId);
            if (requesterSessionId != _model.OwnerSessionId)
            {
                Debug.LogWarning($"[ServerRoomBaseSettings] 踢人失败：非房主操作。Requester={requesterSessionId}");
                return;
            }

            string targetSessionId = message.TargetSessionId;
            if (string.IsNullOrEmpty(targetSessionId)) return;
            if (targetSessionId == requesterSessionId) return;

            if (_model.GetMember(targetSessionId) == null)
            {
                Debug.LogWarning($"[ServerRoomBaseSettings] 踢人失败：目标 {targetSessionId} 不在房间中。");
                return;
            }

            Debug.Log($"[ServerRoomBaseSettings] 房主 {requesterSessionId} 踢出了 {targetSessionId}");

            var kickNotice = new S2C_KickedFromRoom
            {
                RoomId = _room.RoomId,
                ByOwnerSessionId = requesterSessionId
            };
            _globalSender.SendToSession(targetSessionId, kickNotice);

            _globalRoomManager.RemoveMember(targetSessionId, "被房主踢出");
        }

        // [修复] 处理客户端发来的重连就绪协议
        private void OnC2S_ReconnectRoomReady(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_ReconnectRoomReady")) return;

            var message = rawMessage as C2S_ReconnectRoomReady;
            if (message == null) return;

            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId)) return;

            // 1. 触发房间骨架恢复通知（下发快照）
            NotifyReconnectRecovered(sessionId);

            // 2. 下发重连恢复完成确认
            var completeMsg = new S2C_ReconnectRecoveryComplete
            {
                RoomId = _room.RoomId
            };
            _roomSender.SendToRoomMember(_room.RoomId, sessionId, completeMsg);

            Debug.Log($"[ServerRoomBaseSettings] 收到重连 Ready，已下发恢复快照并完成握手。SessionId={sessionId}");
        }

        private void SendBaseSnapshotToMember(string sessionId)
        {
            var snapshot = BuildBaseSnapshot();
            if (snapshot != null) _roomSender.SendToRoomMember(_room.RoomId, sessionId, snapshot);
        }

        private void SendMemberSnapshotToMember(string sessionId)
        {
            var snapshot = new S2C_RoomMemberListSnapshot
            {
                RoomId = _room.RoomId,
                Members = _model.GetAllMembers().ToArray()
            };
            _roomSender.SendToRoomMember(_room.RoomId, sessionId, snapshot);
        }

        private void BroadcastMemberSnapshotToRoom()
        {
            var snapshot = new S2C_RoomMemberListSnapshot
            {
                RoomId = _room.RoomId,
                Members = _model.GetAllMembers().ToArray()
            };
            _roomSender.BroadcastToRoom(_room.RoomId, snapshot);
        }

        private void BroadcastMemberLeft(string sessionId, string reason)
        {
            var message = new S2C_MemberLeft { SessionId = sessionId, Reason = reason ?? string.Empty };
            _roomSender.BroadcastToRoom(_room.RoomId, message);
        }

        private void BroadcastOwnerChanged(string newOwnerSessionId)
        {
            var message = new S2C_RoomOwnerChanged { RoomId = _room.RoomId, NewOwnerSessionId = newOwnerSessionId };
            _roomSender.BroadcastToRoom(_room.RoomId, message);
        }

        private void RecalculateCanStartAndBroadcastIfChanged()
        {
            bool oldCanStart = _model.CanStart;
            bool newCanStart = _model.CalculateCanStart();

            _model.SetCanStart(newCanStart);

            if (oldCanStart == newCanStart) return;

            var changed = new S2C_RoomCanStartStateChanged
            {
                RoomId = _room.RoomId,
                CanStart = newCanStart
            };

            _roomSender.BroadcastToRoom(_room.RoomId, changed);

            _room.EventBus.Publish(new RoomCanStartStateChangedEvent
            {
                RoomId = _room.RoomId,
                CanStart = newCanStart
            });
        }

        private string ResolveSessionIdByConnection(ConnectionId connectionId)
        {
            var session = _sessionManager.GetSessionByConnectionId(connectionId);
            return session?.SessionId ?? string.Empty;
        }

        private bool EnsureAvailable(string caller)
        {
            if (!_isInitialized || _room == null)
            {
                Debug.LogError($"[ServerRoomBaseSettings] {caller} 失败：组件未初始化或 Room 为空。");
                return false;
            }

            return true;
        }
    }
}