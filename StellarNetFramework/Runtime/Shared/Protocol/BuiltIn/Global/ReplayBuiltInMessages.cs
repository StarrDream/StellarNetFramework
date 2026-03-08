using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.BuiltIn
{
    // ────────────────────────────────────────────────────────────────
    // 回放模块内置协议聚合脚本
    // 框架保留号段：3000 - 3999
    // 覆盖：回放列表请求、回放元信息请求、回放文件定位、回放分块传输、进入回放前校验
    // 回放传输属于全局域，不依赖当前房间归属上下文
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 客户端请求获取可观看的回放列表。
    /// 属于全局域上行协议。
    /// </summary>
    [MessageId(3000)]
    public sealed class C2S_GetReplayList : C2SGlobalMessage
    {
        public int PageIndex;
        public int PageSize;
    }

    /// <summary>
    /// 服务端返回回放列表。
    /// 属于全局域下行协议。
    /// </summary>
    [MessageId(3001)]
    public sealed class S2C_ReplayListResult : S2CGlobalMessage
    {
        public ReplayBriefInfo[] Replays;
        public int TotalCount;
    }

    /// <summary>
    /// 回放列表中单条回放的基础信息，用于大厅展示。
    /// </summary>
    public sealed class ReplayBriefInfo
    {
        /// <summary>
        /// 回放唯一标识，与录制时的 RoomId 关联。
        /// </summary>
        public string ReplayId;

        /// <summary>
        /// 录制开始时间（Unix 毫秒时间戳）。
        /// </summary>
        public long RecordStartUnixMs;

        /// <summary>
        /// 回放总 Tick 数，用于展示时长。
        /// </summary>
        public int TotalTicks;

        /// <summary>
        /// 回放文件总字节大小，用于展示文件大小与下载进度。
        /// </summary>
        public long FileSizeBytes;
    }

    /// <summary>
    /// 客户端请求获取指定回放的详细元信息。
    /// 属于全局域上行协议。
    /// </summary>
    [MessageId(3002)]
    public sealed class C2S_GetReplayMeta : C2SGlobalMessage
    {
        public string ReplayId;
    }

    /// <summary>
    /// 服务端返回指定回放的详细元信息。
    /// 属于全局域下行协议。
    /// 客户端在进入回放前必须先获取此信息做版本校验。
    /// ContentMd5 在此处下发，客户端缓存后在所有分块到齐时做完整性校验。
    /// </summary>
    [MessageId(3003)]
    public sealed class S2C_ReplayMetaResult : S2CGlobalMessage
    {
        public bool Success;
        public string ReplayId;

        /// <summary>
        /// 框架版本，客户端用于进入回放前的版本兼容性校验。
        /// </summary>
        public string FrameworkVersion;

        /// <summary>
        /// 协议版本，客户端用于进入回放前的协议兼容性校验。
        /// </summary>
        public string ProtocolVersion;

        /// <summary>
        /// 回放文件总字节大小。
        /// </summary>
        public long FileSizeBytes;

        /// <summary>
        /// 回放文件总分块数量，用于客户端分块下载管理。
        /// </summary>
        public int TotalChunks;

        /// <summary>
        /// 回放帧数据块的 MD5 哈希值，客户端在所有分块拼装完成后做完整性校验。
        /// 与 ReplayRecorder 中 ContentMd5 的计算范围保持一致：只对帧数据块计算，不包含文件头。
        /// </summary>
        public string ContentMd5;

        public string FailReason;
    }

    /// <summary>
    /// 客户端请求下载指定回放文件的某一分块。
    /// 属于全局域上行协议。
    /// </summary>
    [MessageId(3004)]
    public sealed class C2S_RequestReplayChunk : C2SGlobalMessage
    {
        public string ReplayId;

        /// <summary>
        /// 请求的分块索引，从 0 开始。
        /// </summary>
        public int ChunkIndex;
    }

    /// <summary>
    /// 服务端下发回放文件分块数据。
    /// 属于全局域下行协议。
    /// MD5 校验信息已在 S2C_ReplayMetaResult 中下发，此协议不重复携带，减少传输体积。
    /// </summary>
    [MessageId(3005)]
    public sealed class S2C_ReplayChunk : S2CGlobalMessage
    {
        public string ReplayId;

        /// <summary>
        /// 当前分块索引，从 0 开始。
        /// </summary>
        public int ChunkIndex;

        /// <summary>
        /// 总分块数量，客户端用于判断是否收齐。
        /// </summary>
        public int TotalChunks;

        /// <summary>
        /// 当前分块的字节长度。
        /// </summary>
        public int PayloadLength;

        /// <summary>
        /// 回放文件总字节长度，用于拼装完成后的大小校验。
        /// </summary>
        public long TotalLength;

        /// <summary>
        /// 当前分块的字节数据。
        /// </summary>
        public byte[] ChunkData;
    }

    /// <summary>
    /// 客户端请求进入回放前的基础校验结果。
    /// 属于全局域上行协议，客户端在本地文件校验通过后发送，请求服务端确认该回放仍可观看。
    /// </summary>
    [MessageId(3006)]
    public sealed class C2S_RequestEnterReplay : C2SGlobalMessage
    {
        public string ReplayId;
    }

    /// <summary>
    /// 服务端返回进入回放前的基础校验结果。
    /// 属于全局域下行协议。
    /// </summary>
    [MessageId(3007)]
    public sealed class S2C_EnterReplayResult : S2CGlobalMessage
    {
        public bool Success;
        public string ReplayId;
        public string FailReason;
    }
}