using System.Collections.Generic;
using StellarNet.Shared.Protocol.BuiltIn;

namespace StellarNet.Client.Room.Components
{
    /// <summary>
    /// 客户端房间基础设置组件 Model。
    /// 维护房间成员列表、房主信息等本地状态。
    /// </summary>
    public sealed class ClientRoomBaseSettingsModel
    {
        private readonly List<RoomMemberSnapshot> _members = new List<RoomMemberSnapshot>();

        public string RoomName { get; private set; }
        public string OwnerSessionId { get; private set; }
        public int MaxMemberCount { get; private set; }

        public void SetMembers(RoomMemberSnapshot[] members)
        {
            _members.Clear();
            if (members != null)
            {
                _members.AddRange(members);
            }
        }

        public void RemoveMember(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            _members.RemoveAll(m => m.SessionId == sessionId);
        }

        public void SetOwner(string ownerSessionId)
        {
            OwnerSessionId = ownerSessionId;
            // 更新本地成员列表中的房主标记
            foreach (var member in _members)
            {
                member.IsRoomOwner = (member.SessionId == ownerSessionId);
            }
        }

        public void SetBaseInfo(string roomName, string ownerSessionId, int maxMemberCount)
        {
            RoomName = roomName;
            OwnerSessionId = ownerSessionId;
            MaxMemberCount = maxMemberCount;
        }

        public List<RoomMemberSnapshot> GetMembers()
        {
            return new List<RoomMemberSnapshot>(_members);
        }

        public RoomMemberSnapshot GetMember(string sessionId)
        {
            return _members.Find(m => m.SessionId == sessionId);
        }

        public void Clear()
        {
            _members.Clear();
            RoomName = string.Empty;
            OwnerSessionId = string.Empty;
            MaxMemberCount = 0;
        }
    }
}