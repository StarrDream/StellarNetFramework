using System.Collections.Generic;

namespace StellarNet.Server.GlobalModules.ReplayModule
{
    /// <summary>
    /// 回放模块 Model，保存回放文件元信息索引与下载会话状态。
    /// 不承载业务逻辑，不直接驱动任何网络发送。
    /// 回放文件实际存储由文件系统负责，此 Model 只维护内存中的元信息索引。
    /// ContentMd5 由 ReplayRecorder 在落盘时计算并写入文件头，注册元信息时一并写入此 Model。
    /// </summary>
    public sealed class ReplayModel
    {
        /// <summary>
        /// 回放文件元信息记录。
        /// </summary>
        public sealed class ReplayMetaRecord
        {
            public string ReplayId;
            public string FilePath;
            public long FileSizeBytes;
            public long RecordStartUnixMs;
            public int TotalTicks;
            public string FrameworkVersion;
            public string ProtocolVersion;

            /// <summary>
            /// 回放帧数据块的 MD5 哈希值，由 ReplayRecorder 落盘时计算并写入文件头。
            /// 在元信息响应阶段下发给客户端，客户端在所有分块拼装完成后做完整性校验。
            /// </summary>
            public string ContentMd5;
        }

        // ReplayId → 元信息记录
        private readonly Dictionary<string, ReplayMetaRecord> _metaIndex
            = new Dictionary<string, ReplayMetaRecord>();

        // 按录制时间排序的 ReplayId 列表，用于分页查询
        private readonly List<string> _orderedReplayIds = new List<string>();

        /// <summary>
        /// 注册回放文件元信息，在录制完成后由 ReplayHandle 调用。
        /// </summary>
        public void RegisterReplay(ReplayMetaRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.ReplayId))
            {
                return;
            }

            if (!_metaIndex.ContainsKey(record.ReplayId))
            {
                _orderedReplayIds.Add(record.ReplayId);
            }

            _metaIndex[record.ReplayId] = record;
        }

        /// <summary>
        /// 通过 ReplayId 获取元信息记录，查询失败返回 null。
        /// </summary>
        public ReplayMetaRecord GetMeta(string replayId)
        {
            if (string.IsNullOrEmpty(replayId))
            {
                return null;
            }

            _metaIndex.TryGetValue(replayId, out var record);
            return record;
        }

        /// <summary>
        /// 分页获取回放列表，按录制时间从新到旧排列。
        /// </summary>
        public List<ReplayMetaRecord> GetReplayList(int pageIndex, int pageSize)
        {
            var result = new List<ReplayMetaRecord>();
            int startIndex = _orderedReplayIds.Count - 1 - pageIndex * pageSize;
            for (int i = startIndex; i >= 0 && result.Count < pageSize; i--)
            {
                if (_metaIndex.TryGetValue(_orderedReplayIds[i], out var record))
                {
                    result.Add(record);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取当前回放总数量，用于分页计算。
        /// </summary>
        public int TotalCount => _orderedReplayIds.Count;
    }
}