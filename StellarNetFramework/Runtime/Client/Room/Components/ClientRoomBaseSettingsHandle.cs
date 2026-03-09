using System.Collections.Generic;
using StellarNet.Client.Room;
using StellarNet.Shared.Protocol.BuiltIn;
using UnityEngine;

namespace StellarNet.Client.Room.Components
{
    /// <summary>
    /// 客户端房间基础设置组件 Handle。
    /// 负责处理房间成员列表同步、成员加入/离开通知、房主变更等基础骨架逻辑。
    /// 对应服务端的 ServerRoomBaseSettingsHandle。
    /// </summary>
    public sealed class ClientRoomBaseSettingsHandle : ClientRoomAssembler.IInitializableClientRoomComponent
    {
// 必须与服务端保持一致的稳定组件标识
        public const string StableComponentId = "room.base_settings";
        public string ComponentId => StableComponentId;
        private ClientRoomBaseSettingsModel _model;

        private ClientRoomInstance _room;

// 供 View 层订阅的事件
        public event System.Action<RoomMemberSnapshot> OnMemberJoined;
        public event System.Action<string, string> OnMemberLeft; // sessionId, reason
        public event System.Action OnMemberListUpdated;
        public event System.Action<string> OnOwnerChanged;
        public event System.Action<bool> OnCanStartChanged;

        public bool Init(ClientRoomInstance roomInstance)
        {
            if (roomInstance == null)
            {
                Debug.LogError("[ClientRoomBaseSettingsHandle] Init 失败：roomInstance 为 null。");
                return false;
            }

            _room = roomInstance;
            _model = new ClientRoomBaseSettingsModel();
// 可以在此处将 Model 或 Handle 注册到 RoomServiceLocator 供其他组件访问
            _room.RoomServiceLocator.Register(this);
            _room.RoomServiceLocator.Register(_model);
            return true;
        }

        public void Deinit()
        {
            if (_room != null)
            {
                _room.RoomServiceLocator.Unregister<ClientRoomBaseSettingsHandle>();
                _room.RoomServiceLocator.Unregister<ClientRoomBaseSettingsModel>();
            }

            _model?.Clear();
            _room = null;
        }

        public IReadOnlyList<ClientRoomAssembler.ClientRoomHandlerBinding> GetHandlerBindings()
        {
            return new List<ClientRoomAssembler.ClientRoomHandlerBinding>
            {
                new ClientRoomAssembler.ClientRoomHandlerBinding
                {
                    MessageType = typeof(S2C_RoomMemberListSnapshot),
                    Handler = OnS2C_RoomMemberListSnapshot
                },
                new ClientRoomAssembler.ClientRoomHandlerBinding
                {
                    MessageType = typeof(S2C_MemberJoined),
                    Handler = OnS2C_MemberJoined
                },
                new ClientRoomAssembler.ClientRoomHandlerBinding
                {
                    MessageType = typeof(S2C_MemberLeft),
                    Handler = OnS2C_MemberLeft
                },
                new ClientRoomAssembler.ClientRoomHandlerBinding
                {
                    MessageType = typeof(S2C_RoomOwnerChanged),
                    Handler = OnS2C_RoomOwnerChanged
                },
                new ClientRoomAssembler.ClientRoomHandlerBinding
                {
                    MessageType = typeof(S2C_RoomBaseSettingsSnapshot),
                    Handler = OnS2C_RoomBaseSettingsSnapshot
                },
// [修复] 注册准备状态变更协议
                new ClientRoomAssembler.ClientRoomHandlerBinding
                {
                    MessageType = typeof(S2C_MemberReadyStateChanged),
                    Handler = OnS2C_MemberReadyStateChanged
                },
// [修复] 注册可开始状态变更协议
                new ClientRoomAssembler.ClientRoomHandlerBinding
                {
                    MessageType = typeof(S2C_RoomCanStartStateChanged),
                    Handler = OnS2C_RoomCanStartStateChanged
                }
            };
        }

        public void OnTick(float deltaTime)
        {
// 客户端基础组件通常不需要每帧 Tick，除非有倒计时逻辑
        }

        public void OnRoomDestroy()
        {
// 房间销毁时的清理逻辑
            _model?.Clear();
        }

// ─── 协议处理 ────────────────────────────────────────────────────────
        private void OnS2C_RoomMemberListSnapshot(string roomId, object rawMessage)
        {
            var message = rawMessage as S2C_RoomMemberListSnapshot;
            if (message == null) return;
            _model.SetMembers(message.Members);
            OnMemberListUpdated?.Invoke();
            Debug.Log($"[ClientRoomBaseSettingsHandle] 收到成员列表快照，成员数={message.Members?.Length ?? 0}。");
        }

        private void OnS2C_MemberJoined(string roomId, object rawMessage)
        {
            var message = rawMessage as S2C_MemberJoined;
            if (message == null) return;
// 注意：S2C_MemberJoined 仅包含 SessionId，通常服务端会紧接着发列表快照
// 或者我们可以先添加一个占位，等待快照刷新详细信息
// 这里简单处理，仅触发事件，数据由快照保证最终一致性
            Debug.Log($"[ClientRoomBaseSettingsHandle] 成员加入通知：SessionId={message.SessionId}。");
// 构造一个临时快照通知上层，详细信息等待全量同步
            var tempSnapshot = new RoomMemberSnapshot
            {
                SessionId = message.SessionId,
                IsOnline = true,
                IsReady = false,
                IsRoomOwner = false
            };
            OnMemberJoined?.Invoke(tempSnapshot);
        }

        private void OnS2C_MemberLeft(string roomId, object rawMessage)
        {
            var message = rawMessage as S2C_MemberLeft;
            if (message == null) return;
            _model.RemoveMember(message.SessionId);
            OnMemberLeft?.Invoke(message.SessionId, message.Reason);
            OnMemberListUpdated?.Invoke();
            Debug.Log($"[ClientRoomBaseSettingsHandle] 成员离开通知：SessionId={message.SessionId}，原因={message.Reason}。");
        }

        private void OnS2C_RoomOwnerChanged(string roomId, object rawMessage)
        {
            var message = rawMessage as S2C_RoomOwnerChanged;
            if (message == null) return;
            _model.SetOwner(message.NewOwnerSessionId);
            OnOwnerChanged?.Invoke(message.NewOwnerSessionId);
            OnMemberListUpdated?.Invoke(); // 房主变更会影响成员列表中的 IsOwner 字段
            Debug.Log($"[ClientRoomBaseSettingsHandle] 房主变更：NewOwner={message.NewOwnerSessionId}。");
        }

        private void OnS2C_RoomBaseSettingsSnapshot(string roomId, object rawMessage)
        {
            var message = rawMessage as S2C_RoomBaseSettingsSnapshot;
            if (message == null) return;
            _model.SetBaseInfo(message.RoomName, message.OwnerSessionId, message.MaxMemberCount);
            OnOwnerChanged?.Invoke(message.OwnerSessionId);
            Debug.Log($"[ClientRoomBaseSettingsHandle] 收到房间基础信息快照，RoomName={message.RoomName}。");
        }

// [修复] 处理成员准备状态变更
        private void OnS2C_MemberReadyStateChanged(string roomId, object rawMessage)
        {
            var message = rawMessage as S2C_MemberReadyStateChanged;
            if (message == null) return;
            var member = _model.GetMember(message.SessionId);
            if (member != null)
            {
                member.IsReady = message.IsReady;
                OnMemberListUpdated?.Invoke();
            }

            Debug.Log($"[ClientRoomBaseSettingsHandle] 成员准备状态变更：SessionId={message.SessionId}, Ready={message.IsReady}。");
        }

// [修复] 处理房间可开始状态变更
        private void OnS2C_RoomCanStartStateChanged(string roomId, object rawMessage)
        {
            var message = rawMessage as S2C_RoomCanStartStateChanged;
            if (message == null) return;
            OnCanStartChanged?.Invoke(message.CanStart);
            Debug.Log($"[ClientRoomBaseSettingsHandle] 房间可开始状态变更：CanStart={message.CanStart}。");
        }
    }
}