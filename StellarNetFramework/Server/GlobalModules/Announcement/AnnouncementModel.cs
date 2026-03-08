using System.Collections.Generic;
using StellarNet.Shared.Protocol.BuiltIn;

namespace StellarNet.Server.GlobalModules.Announcement
{
    /// <summary>
    /// 公告模块 Model，维护公告列表与已读状态索引。
    /// 不承载业务逻辑，不直接驱动任何网络发送。
    /// 公告列表按发布时间从新到旧排列，分页查询基于此顺序。
    /// 已读状态以 SessionId + AnnouncementId 为键维护，Session 销毁后已读状态随之失效。
    /// </summary>
    public sealed class AnnouncementModel
    {
        // AnnouncementId → 公告信息
        private readonly Dictionary<string, AnnouncementInfo> _announcements
            = new Dictionary<string, AnnouncementInfo>();

        // 按发布时间从新到旧排序的 AnnouncementId 列表
        private readonly List<string> _orderedIds = new List<string>();

        // SessionId → 已读 AnnouncementId 集合
        private readonly Dictionary<string, HashSet<string>> _readStatus
            = new Dictionary<string, HashSet<string>>();

        /// <summary>
        /// 添加或更新公告，按发布时间插入有序列表。
        /// </summary>
        public void AddAnnouncement(AnnouncementInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.AnnouncementId))
            {
                return;
            }

            if (!_announcements.ContainsKey(info.AnnouncementId))
            {
                // 按 PublishUnixMs 从新到旧插入有序列表
                int insertIndex = 0;
                for (int i = 0; i < _orderedIds.Count; i++)
                {
                    if (_announcements.TryGetValue(_orderedIds[i], out var existing) &&
                        existing.PublishUnixMs >= info.PublishUnixMs)
                    {
                        insertIndex = i + 1;
                    }
                    else
                    {
                        break;
                    }
                }
                _orderedIds.Insert(insertIndex, info.AnnouncementId);
            }

            _announcements[info.AnnouncementId] = info;
        }

        /// <summary>
        /// 分页获取公告列表，按发布时间从新到旧排列。
        /// </summary>
        public List<AnnouncementInfo> GetAnnouncementList(int pageIndex, int pageSize)
        {
            var result = new List<AnnouncementInfo>();
            int start = pageIndex * pageSize;

            for (int i = start; i < _orderedIds.Count && result.Count < pageSize; i++)
            {
                if (_announcements.TryGetValue(_orderedIds[i], out var info))
                {
                    result.Add(info);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取公告总数量，用于分页计算。
        /// </summary>
        public int TotalCount => _orderedIds.Count;

        /// <summary>
        /// 标记指定公告为已读，以 SessionId + AnnouncementId 为键。
        /// </summary>
        public void MarkRead(string sessionId, string announcementId)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(announcementId))
            {
                return;
            }

            if (!_readStatus.TryGetValue(sessionId, out var readSet))
            {
                readSet = new HashSet<string>();
                _readStatus[sessionId] = readSet;
            }

            readSet.Add(announcementId);
        }

        /// <summary>
        /// 判断指定公告是否已被指定 Session 读取。
        /// </summary>
        public bool IsRead(string sessionId, string announcementId)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(announcementId))
            {
                return false;
            }

            return _readStatus.TryGetValue(sessionId, out var readSet) &&
                   readSet.Contains(announcementId);
        }

        /// <summary>
        /// 清除指定 Session 的已读状态，在 Session 销毁时调用。
        /// </summary>
        public void ClearReadStatus(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                _readStatus.Remove(sessionId);
            }
        }
    }
}
