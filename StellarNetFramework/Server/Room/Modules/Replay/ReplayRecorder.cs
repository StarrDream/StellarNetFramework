// Assets/StellarNetFramework/Server/Modules/Replay/ReplayRecorder.cs

using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Protocol.Envelope;
using StellarNet.Server.Network.Sender;

namespace StellarNet.Server.Modules.Replay
{
    // 回放录制器，实现 IReplayRecorderWriter，挂载到需要录制的 RoomInstance 上。
    // 只录制满足以下全部条件的消息：
    //   1. 消息属于房间域（S2CRoomMessage）
    //   2. 投递范围为全体房间成员公共广播（isPublicBroadcast = true）
    // 以上条件由 ServerSendCoordinator 在调用 WriteFrame() 前保证，
    // ReplayRecorder 本身不再重复校验，只负责写入。
    // 录制数据以帧为单位缓冲，由 Flush() 定期写入持久化存储。
    // Flush 策略（间隔、触发条件）由外部驱动，ReplayRecorder 不自驱动。
    public sealed class ReplayRecorder : IReplayRecorderWriter
    {
        // 所属房间 ID，用于日志定位与文件命名
        private readonly string _roomId;

        // 帧数据缓冲队列，WriteFrame() 写入，Flush() 消费
        private readonly Queue<ReplayFrame> _frameBuffer = new Queue<ReplayFrame>();

        // 当前录制帧序号，单调递增，用于回放时的帧顺序还原
        private long _frameSequence = 0;

        // 录制开始时间戳（Unix 毫秒），用于回放文件头写入
        private readonly long _recordStartUnixMs;

        // 是否已停止录制（房间销毁后不再接受新帧写入）
        private bool _isStopped = false;

        // 持久化写入委托：由业务层注入，框架不内置文件 I/O 实现
        // 参数1：房间 ID
        // 参数2：待写入的帧数据列表快照
        private System.Action<string, IReadOnlyList<ReplayFrame>> _flushWriter;

        public ReplayRecorder(string roomId, long recordStartUnixMs)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[ReplayRecorder] 构造失败：roomId 不得为空");
                return;
            }

            _roomId = roomId;
            _recordStartUnixMs = recordStartUnixMs;
        }

        // 注入持久化写入委托
        public void SetFlushWriter(System.Action<string, IReadOnlyList<ReplayFrame>> writer)
        {
            if (writer == null)
            {
                Debug.LogError($"[ReplayRecorder] RoomId={_roomId} SetFlushWriter 失败：writer 不得为 null");
                return;
            }

            _flushWriter = writer;
        }

        // 写入一帧公共广播数据到缓冲队列
        // 由 ServerSendCoordinator 在满足录制条件时主动调用
        public void WriteFrame(NetworkEnvelope envelope)
        {
            if (_isStopped)
                return;

            if (envelope == null)
            {
                Debug.LogError($"[ReplayRecorder] RoomId={_roomId} WriteFrame 失败：envelope 不得为 null");
                return;
            }

            var frame = new ReplayFrame(
                _frameSequence++,
                System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                envelope.MessageId,
                envelope.Payload);

            _frameBuffer.Enqueue(frame);
        }

        // 将缓冲队列中的帧数据刷新到持久化存储
        // 由外部驱动（RoomInstance.Tick 或定时器），不自驱动
        public void Flush()
        {
            if (_frameBuffer.Count == 0)
                return;

            if (_flushWriter == null)
            {
                Debug.LogWarning(
                    $"[ReplayRecorder] RoomId={_roomId} Flush 警告：持久化写入委托未注入，" +
                    $"当前缓冲帧数={_frameBuffer.Count}，本次 Flush 已跳过。");
                return;
            }

            // 消费缓冲队列，构建快照后交给持久化写入委托
            var snapshot = new List<ReplayFrame>(_frameBuffer.Count);
            while (_frameBuffer.Count > 0)
            {
                snapshot.Add(_frameBuffer.Dequeue());
            }

            _flushWriter.Invoke(_roomId, snapshot);
        }

        // 停止录制，执行最后一次 Flush 后不再接受新帧写入
        // 由 RoomInstance 销毁流程中的业务组件 OnDestroy() 调用
        public void Stop()
        {
            if (_isStopped)
                return;

            _isStopped = true;

            // 停止时执行最后一次 Flush，确保缓冲数据不丢失
            Flush();
        }

        // 当前缓冲帧数，用于诊断
        public int BufferedFrameCount => _frameBuffer.Count;

        // 当前已录制总帧数
        public long TotalFrameCount => _frameSequence;
    }

    // 单帧回放数据结构
    public sealed class ReplayFrame
    {
        // 帧序号，单调递增
        public long Sequence { get; }

        // 帧写入时间戳（Unix 毫秒）
        public long TimestampUnixMs { get; }

        // 协议 MessageId
        public int MessageId { get; }

        // 协议体序列化字节数组
        public byte[] Payload { get; }

        public ReplayFrame(long sequence, long timestampUnixMs, int messageId, byte[] payload)
        {
            Sequence = sequence;
            TimestampUnixMs = timestampUnixMs;
            MessageId = messageId;
            Payload = payload;
        }
    }
}
