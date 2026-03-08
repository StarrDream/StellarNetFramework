using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.BuiltIn
{
    // ────────────────────────────────────────────────────────────────
    // 公告模块内置协议聚合脚本
    // 框架保留号段：4000 - 4999
    // 覆盖：公告列表拉取、公告推送、公告已读状态同步
    // 公告属于全局域，不依赖当前房间归属上下文
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 客户端请求拉取公告列表。
    /// 属于全局域上行协议。
    /// </summary>
    [MessageId(4000)]
    public sealed class C2S_GetAnnouncementList : C2SGlobalMessage
    {
        public int PageIndex;
        public int PageSize;
    }

    /// <summary>
    /// 服务端返回公告列表。
    /// 属于全局域下行协议。
    /// </summary>
    [MessageId(4001)]
    public sealed class S2C_AnnouncementListResult : S2CGlobalMessage
    {
        public AnnouncementInfo[] Announcements;
        public int TotalCount;
    }

    /// <summary>
    /// 单条公告信息结构。
    /// </summary>
    public sealed class AnnouncementInfo
    {
        /// <summary>
        /// 公告唯一标识。
        /// </summary>
        public string AnnouncementId;

        /// <summary>
        /// 公告标题。
        /// </summary>
        public string Title;

        /// <summary>
        /// 公告正文内容。
        /// </summary>
        public string Content;

        /// <summary>
        /// 公告发布时间（Unix 毫秒时间戳）。
        /// </summary>
        public long PublishUnixMs;

        /// <summary>
        /// 公告类型，由开发者业务层定义具体枚举值（系统通知、版本更新、活动公告等）。
        /// </summary>
        public int AnnouncementType;
    }

    /// <summary>
    /// 服务端主动向在线客户端推送新公告。
    /// 属于全局域下行协议，可广播给所有在线客户端。
    /// </summary>
    [MessageId(4002)]
    public sealed class S2C_AnnouncementPush : S2CGlobalMessage
    {
        public AnnouncementInfo Announcement;
    }

    /// <summary>
    /// 客户端上报公告已读状态。
    /// 属于全局域上行协议。
    /// </summary>
    [MessageId(4003)]
    public sealed class C2S_MarkAnnouncementRead : C2SGlobalMessage
    {
        public string AnnouncementId;
    }

    /// <summary>
    /// 服务端确认公告已读状态同步结果。
    /// 属于全局域下行协议。
    /// </summary>
    [MessageId(4004)]
    public sealed class S2C_MarkAnnouncementReadResult : S2CGlobalMessage
    {
        public bool Success;
        public string AnnouncementId;
    }
}
