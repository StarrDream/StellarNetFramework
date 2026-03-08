using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using StellarNet.Shared.RoomSettings;
using UnityEngine;

namespace StellarNet.Server.Replay
{
    /// <summary>
    /// 房间回放录制器，负责将服务端房间公共广播旁路写入录制文件。
    /// 采用主线程生产、异步队列消费的模型：主线程只写入内存缓冲队列，落盘由独立线程异步消费。
    /// 任意时刻不得因录像写入阻塞主线程。
    /// 缓冲背压策略：溢出时丢弃最旧帧并输出 Warning，绝不阻塞主线程。
    /// 录制只在房间业务主生命周期的"游戏中"阶段生效，由 RoomInstance 通过 OnGameStarted/OnGameEnded 控制。
    /// 回放记录结构是面向回放的标准化结构，不是线上传输封套原样落盘。
    /// Tick 以房间内相对 Tick 为主轴，RecordStartUnixMs 只用于文件记录与诊断。
    /// </summary>
    public sealed class ReplayRecorder
    {
        // ── 回放记录结构（面向回放的标准化结构，非线上封套原样落盘）──

        /// <summary>
        /// 单条回放记录，每条记录至少包含 Tick、MessageId、Payload、RoomId。
        /// 不直接依赖线上传输封套的完整字节布局，在线传输头部调整不影响历史回放文件。
        /// </summary>
        private sealed class ReplayFrame
        {
            /// <summary>
            /// 房间内相对 Tick，从 0 开始，是回放重演的主轴索引。
            /// </summary>
            public int Tick;

            /// <summary>
            /// 协议唯一标识，用于回放时查表反序列化。
            /// </summary>
            public int MessageId;

            /// <summary>
            /// 协议体序列化字节，与线上 Payload 一致。
            /// </summary>
            public byte[] Payload;

            /// <summary>
            /// 所属房间 RoomId，用于回放时校验房间上下文一致性。
            /// </summary>
            public string RoomId;
        }

        // ── 回放文件头结构 ────────────────────────────────────────────

        /// <summary>
        /// .sfr 回放文件头，包含版本信息、房间组件清单、录制元数据与完整性校验字段。
        /// </summary>
        private sealed class ReplayFileHeader
        {
            /// <summary>
            /// 框架版本，用于回放加载时的版本兼容性校验。
            /// </summary>
            public string FrameworkVersion;

            /// <summary>
            /// 协议版本，用于回放加载时的协议兼容性校验。
            /// </summary>
            public string ProtocolVersion;

            /// <summary>
            /// 房间配置快照格式标识，来自 IRoomSettings.SettingsFormat。
            /// </summary>
            public string SettingsFormat;

            /// <summary>
            /// 房间配置快照版本号，来自 IRoomSettings.SettingsVersion。
            /// </summary>
            public int SettingsVersion;

            /// <summary>
            /// 房间配置快照字节内容，来自 IRoomSettings.Serialize()。
            /// 框架只负责写入与透传，不解析业务字节内容。
            /// </summary>
            public byte[] SettingsPayload;

            /// <summary>
            /// 录制时房间业务组件清单，使用稳定组件注册标识，不使用运行时类型名。
            /// 数组顺序即回放房间的组件装配顺序。
            /// 即使某组件在录制期间没有产生任何公共广播帧，只要属于录制时房间结构的一部分，也必须出现在此数组中。
            /// </summary>
            public string[] RoomComponentIds;

            /// <summary>
            /// 录制总帧数（Tick 数），用于回放时长展示与进度计算。
            /// </summary>
            public int TotalTicks;

            /// <summary>
            /// 录制开始时的 Unix 毫秒时间戳，主要用于文件记录、诊断与展示，不作为主重放索引。
            /// </summary>
            public long RecordStartUnixMs;

            /// <summary>
            /// 除文件头之外的录像数据块的 MD5 哈希值，用于完整性校验。
            /// 客户端在完整接收并拼装后立即计算 MD5 与此字段比对，校验失败则丢弃文件。
            /// </summary>
            public string ContentMd5;
        }

        // ── 运行时字段 ────────────────────────────────────────────────

        private readonly string _roomId;
        private readonly IRoomSettings _settings;
        private readonly string _frameworkVersion;
        private readonly string _protocolVersion;

        // 录制时的组件清单，由外部在挂载录制器时注入
        private string[] _roomComponentIds;

        // 内存缓冲队列，主线程生产，异步线程消费
        private readonly Queue<ReplayFrame> _buffer = new Queue<ReplayFrame>();
        private readonly object _bufferLock = new object();
        private int _bufferCapacity;

        // 录制是否激活（只在 InGame 阶段为 true）
        private volatile bool _isRecording;

        // 异步写入线程控制
        private Thread _writeThread;
        private volatile bool _isRunning;

        // 录制起始信息
        private int _recordStartTick;
        private long _recordStartUnixMs;

        // 已落盘的帧列表，由写入线程消费后追加
        private readonly List<ReplayFrame> _writtenFrames = new List<ReplayFrame>();
        private readonly object _writtenFramesLock = new object();

        // 当前房间 Tick 的外部读取委托，由 RoomInstance 注入
        private System.Func<int> _currentTickGetter;

        // 落盘目录
        private readonly string _outputDirectory;

        public ReplayRecorder(
            string roomId,
            IRoomSettings settings,
            string frameworkVersion = "1.0.0",
            string protocolVersion = "1.0.0",
            int bufferCapacity = 1024,
            string outputDirectory = "Replays")
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[ReplayRecorder] 构造失败：roomId 为空。");
                return;
            }

            if (settings == null)
            {
                Debug.LogError($"[ReplayRecorder] 构造失败：settings 为 null，RoomId={roomId}。");
                return;
            }

            _roomId = roomId;
            _settings = settings;
            _frameworkVersion = frameworkVersion ?? "1.0.0";
            _protocolVersion = protocolVersion ?? "1.0.0";
            _bufferCapacity = bufferCapacity > 0 ? bufferCapacity : 1024;
            _outputDirectory = outputDirectory ?? "Replays";
            _isRecording = false;
            _isRunning = true;

            // 启动异步写入线程，后台线程不阻止进程退出
            _writeThread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = $"ReplayWriter_{roomId}"
            };
            _writeThread.Start();
        }

        /// <summary>
        /// 注入当前房间 Tick 读取委托，由 ServerRoomAssembler 在装配阶段调用。
        /// 用于录制帧时记录当前房间相对 Tick，保证 Tick 主轴的准确性。
        /// </summary>
        public void SetTickGetter(System.Func<int> tickGetter)
        {
            if (tickGetter == null)
            {
                Debug.LogError($"[ReplayRecorder] SetTickGetter 失败：tickGetter 为 null，RoomId={_roomId}。");
                return;
            }
            _currentTickGetter = tickGetter;
        }

        /// <summary>
        /// 注入录制时的房间组件清单，由 ServerRoomAssembler 在装配完成后调用。
        /// 使用稳定组件注册标识，不使用运行时类型名。
        /// 即使某组件在录制期间没有产生任何公共广播帧，只要属于房间结构的一部分，也必须出现在此清单中。
        /// </summary>
        public void SetRoomComponentIds(string[] componentIds)
        {
            if (componentIds == null)
            {
                Debug.LogError($"[ReplayRecorder] SetRoomComponentIds 失败：componentIds 为 null，RoomId={_roomId}。");
                return;
            }
            _roomComponentIds = componentIds;
        }

        // ── 录制生命周期控制 ──────────────────────────────────────────

        /// <summary>
        /// 由 RoomInstance.AdvanceToInGame() 调用，标记录制正式开始。
        /// 录制只在"游戏中"阶段生效，等待开始阶段不纳入正式录制。
        /// 即使等待开始阶段发生了公共广播，也不得自动视为正式可录制事件。
        /// </summary>
        public void OnGameStarted()
        {
            _recordStartUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _recordStartTick = _currentTickGetter?.Invoke() ?? 0;
            _isRecording = true;
            Debug.Log($"[ReplayRecorder] 录制开始，RoomId={_roomId}，RecordStartUnixMs={_recordStartUnixMs}，StartTick={_recordStartTick}。");
        }

        /// <summary>
        /// 由 RoomInstance.AdvanceToGameEnding() 调用，标记录制结束。
        /// </summary>
        public void OnGameEnded()
        {
            _isRecording = false;
            Debug.Log($"[ReplayRecorder] 录制停止，RoomId={_roomId}，已写入缓冲帧数={_writtenFrames.Count}。");
        }

        // ── 旁路写入入口 ──────────────────────────────────────────────

        /// <summary>
        /// 尝试写入一条录制记录。
        /// 由 ServerSendCoordinator 在满足录制条件时主动调用，不通过事件订阅触发。
        /// 只有 _isRecording=true 时才实际写入，否则直接跳过（高频热路径，不输出日志）。
        /// 主线程只写入内存缓冲队列，不执行任何 IO 操作，不阻塞主线程。
        /// </summary>
        public void TryWrite(int messageId, byte[] payload, string roomId)
        {
            // 录制未激活时直接跳过，高频热路径不输出日志
            if (!_isRecording)
            {
                return;
            }

            if (payload == null || payload.Length == 0)
            {
                Debug.LogWarning($"[ReplayRecorder] TryWrite 跳过：payload 为空，MessageId={messageId}，RoomId={_roomId}。");
                return;
            }

            int currentTick = _currentTickGetter?.Invoke() ?? 0;

            var frame = new ReplayFrame
            {
                Tick = currentTick,
                MessageId = messageId,
                Payload = payload,
                RoomId = roomId ?? _roomId
            };

            lock (_bufferLock)
            {
                // 背压策略：缓冲满时丢弃最旧帧并输出 Warning，绝不阻塞主线程
                if (_buffer.Count >= _bufferCapacity)
                {
                    _buffer.Dequeue();
                    Debug.LogWarning($"[ReplayRecorder] 录制缓冲区已满（容量={_bufferCapacity}），已丢弃最旧帧，" +
                                     $"RoomId={_roomId}，MessageId={messageId}，当前 Tick={currentTick}。");
                }

                _buffer.Enqueue(frame);
            }
        }

        // ── 收尾与落盘 ────────────────────────────────────────────────

        /// <summary>
        /// 录制器收尾，由 RoomInstance.Destroy() 调用。
        /// 停止录制，等待异步写入线程完成剩余帧落盘，然后写入回放文件并完成 MD5 校验写入。
        /// 设置明确超时上限（5秒），超时后强制继续销毁流程，不无限等待。
        /// </summary>
        public void Finalize()
        {
            _isRecording = false;
            _isRunning = false;

            // 等待写入线程完成，设置 5 秒超时上限
            if (_writeThread != null && _writeThread.IsAlive)
            {
                bool joined = _writeThread.Join(millisecondsTimeout: 5000);
                if (!joined)
                {
                    Debug.LogWarning($"[ReplayRecorder] 写入线程超时（5s）未完成，RoomId={_roomId}，强制继续销毁流程。");
                }
            }

            // 将剩余缓冲帧全部消费完毕，确保不遗漏
            DrainBuffer();

            // 写入最终回放文件
            WriteReplayFile();
        }

        // ── 异步写入线程 ──────────────────────────────────────────────

        /// <summary>
        /// 异步写入线程主循环，持续消费内存缓冲队列中的帧。
        /// 主线程只生产，此线程只消费，通过锁保证线程安全。
        /// </summary>
        private void WriteLoop()
        {
            while (_isRunning)
            {
                DrainBuffer();
                Thread.Sleep(16);
            }
            // 线程退出前再消费一次，确保不遗漏
            DrainBuffer();
        }

        /// <summary>
        /// 将当前缓冲队列中所有帧转移到 _writtenFrames 列表。
        /// </summary>
        private void DrainBuffer()
        {
            lock (_bufferLock)
            {
                while (_buffer.Count > 0)
                {
                    var frame = _buffer.Dequeue();
                    lock (_writtenFramesLock)
                    {
                        _writtenFrames.Add(frame);
                    }
                }
            }
        }

        // ── 文件写入 ──────────────────────────────────────────────────

        /// <summary>
        /// 将录制内容写入 .sfr 回放文件。
        /// 文件结构：文件头 JSON + 换行分隔符 + 帧数据 JSON 数组。
        /// MD5 只对帧数据块计算，不包含文件头，写入文件头的 ContentMd5 字段。
        /// </summary>
        private void WriteReplayFile()
        {
            if (_writtenFrames.Count == 0)
            {
                Debug.Log($"[ReplayRecorder] 无录制帧，跳过文件写入，RoomId={_roomId}。");
                return;
            }

            // 确保输出目录存在
            if (!Directory.Exists(_outputDirectory))
            {
                Directory.CreateDirectory(_outputDirectory);
            }

            string safeRoomId = _roomId.Replace(":", "_").Replace("/", "_");
            string filePath = Path.Combine(_outputDirectory, $"{safeRoomId}.sfr");

            List<ReplayFrame> framesToWrite;
            lock (_writtenFramesLock)
            {
                framesToWrite = new List<ReplayFrame>(_writtenFrames);
            }

            // 序列化帧数据块
            string framesJson = JsonConvert.SerializeObject(framesToWrite);
            byte[] framesBytes = Encoding.UTF8.GetBytes(framesJson);

            // 计算帧数据块的 MD5，只对帧数据块计算，不包含文件头
            string contentMd5 = ComputeMd5(framesBytes);

            // 构建文件头
            var header = new ReplayFileHeader
            {
                FrameworkVersion = _frameworkVersion,
                ProtocolVersion = _protocolVersion,
                SettingsFormat = _settings.SettingsFormat,
                SettingsVersion = _settings.SettingsVersion,
                SettingsPayload = _settings.Serialize(),
                RoomComponentIds = _roomComponentIds ?? new string[0],
                TotalTicks = _currentTickGetter?.Invoke() ?? 0,
                RecordStartUnixMs = _recordStartUnixMs,
                ContentMd5 = contentMd5
            };

            string headerJson = JsonConvert.SerializeObject(header);
            byte[] headerBytes = Encoding.UTF8.GetBytes(headerJson);

            // 写入文件：文件头 + 换行分隔符 + 帧数据
            byte[] separator = Encoding.UTF8.GetBytes("\n---FRAMES---\n");

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(headerBytes, 0, headerBytes.Length);
                fs.Write(separator, 0, separator.Length);
                fs.Write(framesBytes, 0, framesBytes.Length);
            }

            Debug.Log($"[ReplayRecorder] 回放文件写入完成，RoomId={_roomId}，路径={filePath}，" +
                      $"总帧数={framesToWrite.Count}，ContentMd5={contentMd5}。");
        }

        /// <summary>
        /// 计算字节数组的 MD5 哈希值，返回十六进制字符串。
        /// 只对帧数据块计算，不包含文件头，保证文件头调整不影响 MD5 有效性。
        /// </summary>
        private static string ComputeMd5(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(data);
                var sb = new StringBuilder(32);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
