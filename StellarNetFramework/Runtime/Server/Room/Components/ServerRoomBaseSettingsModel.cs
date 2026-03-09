// ════════════════════════════════════════════════════════════════
// 文件：ServerRoomBaseSettingsModel.cs
// 路径：Assets/StellarNetFramework/Runtime/Server/Room/Components/ServerRoomBaseSettingsModel.cs
// 职责：房间基础设置组件 Model，纯状态层。
//       负责维护房间成员列表、房主标识与可开始状态。
//       新增：维护房间名与最大人数（从配置蓝图初始化）。
// ════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using StellarNet.Shared.Protocol.BuiltIn;

namespace StellarNet.Server.Room.BuiltIn
{
    /// <summary>
    /// 房间基础设置组件 Model，纯状态层。
    /// 负责维护房间成员列表、房主标识与可开始状态。
    /// 严格遵循 MSV 架构，不承载网络发送与事件发布职责，仅供 Handle 读取与修改。
    /// </summary>
    public sealed class ServerRoomBaseSettingsModel
    {
        private readonly Dictionary<string, RoomMemberSnapshot> _memberMap =
            new Dictionary<string, RoomMemberSnapshot>();

        // 运行时动态状态
        public string OwnerSessionId { get; private set; } = string.Empty;
        public bool CanStart { get; private set; } = false;

        // [新增] 静态配置状态（由 Init 时读取配置蓝图写入）
        public string RoomName { get; private set; } = string.Empty;
        public int MaxMemberCount { get; private set; } = 0;

        /// <summary>
        /// 初始化基础信息，由 Handle 在 Init 阶段调用。
        /// </summary>
        public void SetBaseInfo(string roomName, string ownerSessionId, int maxMemberCount)
        {
            RoomName = roomName ?? string.Empty;
            OwnerSessionId = ownerSessionId ?? string.Empty;
            MaxMemberCount = maxMemberCount;
        }

        public void AddOrUpdateMember(string sessionId, bool isOnline, bool isReady)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            if (_memberMap.TryGetValue(sessionId, out var member))
            {
                member.IsOnline = isOnline;
                member.IsReady = isReady;
            }
            else
            {
                _memberMap[sessionId] = new RoomMemberSnapshot
                {
                    SessionId = sessionId,
                    IsOnline = isOnline,
                    IsRoomOwner = false,
                    IsReady = isReady
                };
            }
        }

        public bool RemoveMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }

            return _memberMap.Remove(sessionId);
        }

        public RoomMemberSnapshot GetMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }

            _memberMap.TryGetValue(sessionId, out var member);
            return member;
        }

        public List<RoomMemberSnapshot> GetAllMembers()
        {
            var result = new List<RoomMemberSnapshot>(_memberMap.Count);
            foreach (var pair in _memberMap)
            {
                result.Add(CloneSnapshot(pair.Value));
            }

            return result;
        }

        public void SetOwner(string sessionId)
        {
            OwnerSessionId = sessionId ?? string.Empty;
            foreach (var pair in _memberMap)
            {
                pair.Value.IsRoomOwner = (pair.Key == OwnerSessionId);
            }
        }

        public void SetCanStart(bool canStart)
        {
            CanStart = canStart;
        }

        public bool CalculateCanStart()
        {
            if (_memberMap.Count <= 0)
            {
                return false;
            }

            foreach (var pair in _memberMap)
            {
                if (!pair.Value.IsOnline || !pair.Value.IsReady)
                {
                    return false;
                }
            }

            return true;
        }

        public string SelectNextOwnerSessionId()
        {
            foreach (var pair in _memberMap)
            {
                return pair.Key;
            }

            return string.Empty;
        }

        public void Clear()
        {
            _memberMap.Clear();
            OwnerSessionId = string.Empty;
            CanStart = false;
            // RoomName 和 MaxMemberCount 通常不需要在 Clear 中重置，
            // 因为 Deinit 会销毁整个 Model 实例，下次 Init 会创建新的。
        }

        private static RoomMemberSnapshot CloneSnapshot(RoomMemberSnapshot source)
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
    }
}