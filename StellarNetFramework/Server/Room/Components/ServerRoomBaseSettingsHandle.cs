using System.Collections.Generic;
using StellarNet.Server.Network;
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
    /// 房间基础设置业务 Handle。
    /// 直接作为房间业务单元的主入口，实现 IInitializableRoomComponent 接入装配管线。
    /// 负责生命周期接入、协议监听注册、业务逻辑组织、网络收发与事件发布。
    /// 严格遵循文档规范：消除 Component+Handle 双主概念，Handle 即为业务主入口。
    /// </summary>
    public sealed class ServerRoomBaseSettingsHandle :
        ServerRoomAssembler.IInitializableRoomComponent,
        IRoomBaseSettingsService
    {
        public const string StableComponentId = "room.base_settings";
        public string ComponentId => StableComponentId;

        private readonly ServerGlobalMessageSender _globalSender;
        private readonly ServerRoomMessageSender _roomSender;
        private readonly SessionManager _sessionManager;

        private ServerRoomBaseSettingsModel _model;
        private RoomInstance _room;
        private bool _isInitialized;

        public ServerRoomBaseSettingsHandle(
            ServerGlobalMessageSender globalSender,
            ServerRoomMessageSender roomSender,
            SessionManager sessionManager)
        {
            if (globalSender == null)
            {
                Debug.LogError("[ServerRoomBaseSettingsHandle] 构造失败：globalSender 为 null。");
                return;
            }

            if (roomSender == null)
            {
                Debug.LogError("[ServerRoomBaseSettingsHandle] 构造失败：roomSender 为 null。");
                return;
            }

            if (sessionManager == null)
            {
                Debug.LogError("[ServerRoomBaseSettingsHandle] 构造失败：sessionManager 为 null。");
                return;
            }

            _globalSender = globalSender;
            _roomSender = roomSender;
            _sessionManager = sessionManager;
        }

        // ─── 生命周期与装配接口 ──────────────────────────────────────────

        public bool Init(RoomInstance roomInstance)
        {
            if (roomInstance == null)
            {
                Debug.LogError("[ServerRoomBaseSettingsHandle] Init 失败：roomInstance 为 null。");
                return false;
            }

            if (roomInstance.RoomServiceLocator == null)
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsHandle] Init 失败：RoomId={roomInstance.RoomId} 的 RoomServiceLocator 为 null。");
                return false;
            }

            if (roomInstance.EventBus == null)
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsHandle] Init 失败：RoomId={roomInstance.RoomId} 的 EventBus 为 null。");
                return false;
            }

            _room = roomInstance;
            _model = new ServerRoomBaseSettingsModel();

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
                }
            };
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

        // ─── IRoomBaseSettingsService 跨域代理接口 ───────────────────────

        public void NotifyMemberJoined(string sessionId)
        {
            if (!EnsureAvailable("NotifyMemberJoined")) return;
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsHandle] NotifyMemberJoined 失败：sessionId 为空，RoomId={_room.RoomId}。");
                return;
            }

            _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: false);

            if (string.IsNullOrEmpty(_model.OwnerSessionId))
            {
                _model.SetOwner(sessionId);
            }
            else
            {
                _model.SetOwner(_model.OwnerSessionId); // 刷新房主标记
            }

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

        public void NotifyMemberLeft(string sessionId, string reason)
        {
            if (!EnsureAvailable("NotifyMemberLeft")) return;
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsHandle] NotifyMemberLeft 失败：sessionId 为空，RoomId={_room.RoomId}。");
                return;
            }

            bool existed = _model.RemoveMember(sessionId);
            if (!existed)
            {
                Debug.LogWarning(
                    $"[ServerRoomBaseSettingsHandle] NotifyMemberLeft 警告：RoomId={_room.RoomId} 的成员表中不存在 SessionId={sessionId}。");
            }

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
            _model.SetOwner(_model.OwnerSessionId); // 刷新房主标记
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
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsHandle] NotifyReconnectRecovered 失败：sessionId 为空，RoomId={_room.RoomId}。");
                return;
            }

            var member = _model.GetMember(sessionId);
            if (member != null)
            {
                _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: member.IsReady);
            }
            else
            {
                _model.AddOrUpdateMember(sessionId, isOnline: true, isReady: false);
                if (_model.OwnerSessionId == sessionId)
                {
                    _model.SetOwner(sessionId);
                }
            }

            SendBaseSnapshotToMember(sessionId);
            SendMemberSnapshotToMember(sessionId);
            BroadcastMemberSnapshotToRoom();
        }

        public List<RoomMemberSnapshot> GetMemberSnapshots() =>
            _model?.GetAllMembers() ?? new List<RoomMemberSnapshot>();

        public string GetOwnerSessionId() => _model?.OwnerSessionId ?? string.Empty;
        public bool GetCanStart() => _model?.CanStart ?? false;

        public S2C_RoomBaseSettingsSnapshot BuildBaseSnapshot()
        {
            if (!EnsureAvailable("BuildBaseSnapshot")) return null;
            return new S2C_RoomBaseSettingsSnapshot
            {
                RoomId = _room.RoomId,
                RoomName = string.Empty,
                Description = string.Empty,
                HasPassword = false,
                MaxMemberCount = 0,
                OwnerSessionId = _model.OwnerSessionId,
                CanStart = _model.CanStart
            };
        }

        public void ClearState()
        {
            _model?.Clear();
        }

        // ─── 协议处理逻辑 ──────────────────────────────────────────────────

        private void OnC2S_SetReadyState(ConnectionId connectionId, string roomId, object rawMessage)
        {
            if (!EnsureAvailable("OnC2S_SetReadyState")) return;

            var message = rawMessage as C2S_SetReadyState;
            if (message == null)
            {
                Debug.LogError($"[ServerRoomBaseSettingsHandle] OnC2S_SetReadyState 失败：消息类型转换失败，RoomId={roomId}。");
                return;
            }

            string sessionId = ResolveSessionIdByConnection(connectionId);
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsHandle] OnC2S_SetReadyState 失败：无法解析 SessionId，ConnectionId={connectionId}。");
                return;
            }

            var member = _model.GetMember(sessionId);
            if (member == null)
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsHandle] OnC2S_SetReadyState 失败：SessionId={sessionId} 不在房间成员表中。");
                return;
            }

            if (member.IsReady == message.IsReady)
            {
                return;
            }

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
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError(
                    $"[ServerRoomBaseSettingsHandle] OnC2S_GetRoomMemberList 失败：无法解析 SessionId，ConnectionId={connectionId}。");
                return;
            }

            SendMemberSnapshotToMember(sessionId);
        }

        // ─── 内部辅助方法 ──────────────────────────────────────────────────

        private void SendBaseSnapshotToMember(string sessionId)
        {
            var snapshot = BuildBaseSnapshot();
            if (snapshot != null)
            {
                _roomSender.SendToRoomMember(_room.RoomId, sessionId, snapshot);
            }
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

        private void BroadcastMemberJoined(string sessionId)
        {
            var message = new S2C_MemberJoined { SessionId = sessionId };
            _roomSender.BroadcastToRoom(_room.RoomId, message);
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

            if (oldCanStart == newCanStart)
            {
                return;
            }

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
            if (!_isInitialized)
            {
                Debug.LogError($"[ServerRoomBaseSettingsHandle] {caller} 失败：组件尚未完成初始化。");
                return false;
            }

            if (_room == null)
            {
                Debug.LogError($"[ServerRoomBaseSettingsHandle] {caller} 失败：内部 _room 为 null。");
                return false;
            }

            return true;
        }
    }
}