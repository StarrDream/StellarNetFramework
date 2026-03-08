using System.Collections.Generic;
using StellarNet.Shared.Protocol.BuiltIn;

namespace StellarNet.Client.GlobalModules.Replay
{
    /// <summary>
    /// 客户端回放模块 Model，保存回放列表缓存与分块下载状态机运行时状态。
    /// 不承载业务逻辑，不直接驱动任何网络发送。
    /// 分块下载状态机：Idle → Downloading → Completed/Failed。
    /// ContentMd5 在元信息阶段写入，所有分块到齐后由 ClientReplayHandle 读取做完整性校验。
    /// </summary>
    public sealed class ClientReplayModel
    {
        /// <summary>
        /// 分块下载状态机阶段。
        /// </summary>
        public enum DownloadPhase
        {
            Idle,
            Downloading,
            Completed,
            Failed
        }

        // 回放列表缓存
        private readonly List<ReplayBriefInfo> _replayList = new List<ReplayBriefInfo>();

        /// <summary>
        /// 当前分块下载的回放 ID。
        /// </summary>
        public string DownloadingReplayId { get; private set; }

        /// <summary>
        /// 当前分块下载阶段。
        /// </summary>
        public DownloadPhase Phase { get; private set; }

        /// <summary>
        /// 总分块数量。
        /// </summary>
        public int TotalChunks { get; private set; }

        /// <summary>
        /// 已接收的分块数量。
        /// </summary>
        public int ReceivedChunks { get; private set; }

        /// <summary>
        /// 分块数据缓冲区，ChunkIndex → 分块字节数据。
        /// </summary>
        private readonly Dictionary<int, byte[]> _chunkBuffer = new Dictionary<int, byte[]>();

        /// <summary>
        /// 从元信息阶段缓存的 ContentMd5，在所有分块拼装完成后用于完整性校验。
        /// 与 ReplayRecorder 中的计算范围一致：只对帧数据块计算，不包含文件头。
        /// </summary>
        public string CachedContentMd5 { get; private set; }

        /// <summary>
        /// 最近一次下载失败原因。
        /// </summary>
        public string LastDownloadFailReason { get; private set; }

        /// <summary>
        /// 当前下载进度（0.0 ~ 1.0）。
        /// </summary>
        public float DownloadProgress =>
            TotalChunks > 0 ? (float)ReceivedChunks / TotalChunks : 0f;

        /// <summary>
        /// 回放列表总数量。
        /// </summary>
        public int ReplayListCount => _replayList.Count;

        public ClientReplayModel()
        {
            Phase = DownloadPhase.Idle;
            DownloadingReplayId = string.Empty;
            LastDownloadFailReason = string.Empty;
            CachedContentMd5 = string.Empty;
        }

        /// <summary>
        /// 更新回放列表缓存。
        /// </summary>
        public void SetReplayList(ReplayBriefInfo[] replays)
        {
            _replayList.Clear();
            if (replays != null)
            {
                _replayList.AddRange(replays);
            }
        }

        /// <summary>
        /// 获取回放列表快照。
        /// </summary>
        public List<ReplayBriefInfo> GetReplayList() => new List<ReplayBriefInfo>(_replayList);

        /// <summary>
        /// 初始化分块下载状态机，开始下载指定回放。
        /// contentMd5 在元信息阶段由服务端下发，缓存后在拼装完成时做完整性校验。
        /// </summary>
        public void BeginDownload(string replayId, int totalChunks, string contentMd5)
        {
            DownloadingReplayId = replayId ?? string.Empty;
            TotalChunks = totalChunks;
            ReceivedChunks = 0;
            Phase = DownloadPhase.Downloading;
            LastDownloadFailReason = string.Empty;
            CachedContentMd5 = contentMd5 ?? string.Empty;
            _chunkBuffer.Clear();
        }

        /// <summary>
        /// 写入一个分块数据。
        /// </summary>
        public void WriteChunk(int chunkIndex, byte[] data)
        {
            if (data == null || chunkIndex < 0 || chunkIndex >= TotalChunks)
            {
                return;
            }

            if (!_chunkBuffer.ContainsKey(chunkIndex))
            {
                _chunkBuffer[chunkIndex] = data;
                ReceivedChunks++;
            }
        }

        /// <summary>
        /// 判断所有分块是否已全部接收。
        /// </summary>
        public bool IsAllChunksReceived => ReceivedChunks >= TotalChunks && TotalChunks > 0;

        /// <summary>
        /// 按顺序拼装所有分块字节数据，返回完整文件字节数组。
        /// 拼装前必须确认 IsAllChunksReceived 为 true。
        /// </summary>
        public byte[] AssembleChunks()
        {
            if (!IsAllChunksReceived)
            {
                return null;
            }

            var parts = new List<byte[]>(TotalChunks);
            int totalLength = 0;
            for (int i = 0; i < TotalChunks; i++)
            {
                if (!_chunkBuffer.TryGetValue(i, out var chunk) || chunk == null)
                {
                    return null;
                }

                parts.Add(chunk);
                totalLength += chunk.Length;
            }

            byte[] result = new byte[totalLength];
            int offset = 0;
            foreach (var part in parts)
            {
                System.Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }

        public void SetPhase(DownloadPhase phase) => Phase = phase;

        public void SetDownloadFailed(string reason)
        {
            Phase = DownloadPhase.Failed;
            LastDownloadFailReason = reason ?? string.Empty;
        }

        public void ResetDownload()
        {
            Phase = DownloadPhase.Idle;
            DownloadingReplayId = string.Empty;
            TotalChunks = 0;
            ReceivedChunks = 0;
            LastDownloadFailReason = string.Empty;
            CachedContentMd5 = string.Empty;
            _chunkBuffer.Clear();
        }
    }
}