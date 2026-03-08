using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using StellarNet.Client.Room;
using StellarNet.Client.State; // 修复：引入客户端状态机命名空间
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using UnityEngine;

namespace StellarNet.Client.GlobalModules.Replay
{
    /// <summary>
    /// 客户端全局级回放基础设施控制器。
    /// 负责加载回放文件、驱动时间线、按录制顺序投喂回放事件。
    /// 严格落实文档第 30 节约束：以 Tick 为主轴，直接注入本地回放房间的 Router，不参与在线权威。
    /// </summary>
    public sealed class ClientReplayPlaybackController
    {
        // 回放文件头反序列化结构（与服务端 ReplayRecorder 对应）
        private sealed class ReplayFileHeader
        {
            public string FrameworkVersion;
            public string ProtocolVersion;
            public string[] RoomComponentIds;
            public int TotalTicks;
            public string ContentMd5;
        }

        // 单条回放帧结构
        private sealed class ReplayFrame
        {
            public int Tick;
            public int MessageId;
            public byte[] Payload;
            public string RoomId;
        }

        private readonly GlobalClientManager _globalClientManager;
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;

        // 播放状态机
        public enum PlaybackState
        {
            Stopped,
            Playing,
            Paused,
            Finished
        }

        public PlaybackState State { get; private set; }

        // 本地回放房间实例
        public ClientRoomInstance LocalReplayRoom { get; private set; }

        // 回放数据缓存
        private ReplayFileHeader _header;
        private List<ReplayFrame> _frames;

        // 虚拟时钟与播放游标
        private int _currentTick;
        private int _frameIndex;
        private float _tickAccumulator;
        private const float TickInterval = 1f / 30f; // 假设 30Hz 逻辑帧率

        public ClientReplayPlaybackController(
            GlobalClientManager globalClientManager,
            MessageRegistry messageRegistry,
            ISerializer serializer)
        {
            if (globalClientManager == null || messageRegistry == null || serializer == null)
            {
                Debug.LogError("[ClientReplayPlaybackController] 构造失败：依赖项为 null。");
                return;
            }

            _globalClientManager = globalClientManager;
            _messageRegistry = messageRegistry;
            _serializer = serializer;
            State = PlaybackState.Stopped;
        }

        /// <summary>
        /// 加载回放文件并启动本地回放房间。
        /// 必须在大厅态调用。
        /// </summary>
        public bool LoadAndPlay(byte[] replayFileBytes, ClientRoomAssembler assembler)
        {
            // 修复：使用正确的 ClientAppState 枚举
            if (_globalClientManager.CurrentState != ClientAppState.InLobby)
            {
                Debug.LogError(
                    $"[ClientReplayPlaybackController] 只能从 InLobby 态进入回放，当前状态={_globalClientManager.CurrentState}。");
                return false;
            }

            if (replayFileBytes == null || replayFileBytes.Length == 0)
            {
                Debug.LogError("[ClientReplayPlaybackController] 回放文件数据为空。");
                return false;
            }

            if (assembler == null)
            {
                Debug.LogError("[ClientReplayPlaybackController] 缺少客户端房间装配器。");
                return false;
            }

            if (!ParseReplayFile(replayFileBytes)) return false;

            _globalClientManager.TransitionToReplay();

            string roomId = "Replay_" + Guid.NewGuid().ToString("N");
            if (_frames.Count > 0 && !string.IsNullOrEmpty(_frames[0].RoomId))
            {
                roomId = _frames[0].RoomId;
            }

            LocalReplayRoom = new ClientRoomInstance(roomId);

            bool assembleSuccess = assembler.Assemble(LocalReplayRoom, _header.RoomComponentIds);
            if (!assembleSuccess)
            {
                Debug.LogError("[ClientReplayPlaybackController] 回放房间装配失败，退出回放。");
                StopAndExit();
                return false;
            }

            _currentTick = 0;
            _frameIndex = 0;
            _tickAccumulator = 0f;
            State = PlaybackState.Playing;

            Debug.Log($"[ClientReplayPlaybackController] 回放加载成功，总帧数={_frames.Count}，总 Tick={_header.TotalTicks}，开始播放。");
            return true;
        }

        public void Play()
        {
            if (State == PlaybackState.Paused) State = PlaybackState.Playing;
        }

        public void Pause()
        {
            if (State == PlaybackState.Playing) State = PlaybackState.Paused;
        }

        public void StopAndExit()
        {
            State = PlaybackState.Stopped;
            if (LocalReplayRoom != null)
            {
                LocalReplayRoom.Destroy();
                LocalReplayRoom = null;
            }

            _frames?.Clear();
            _header = null;

            // 修复：使用正确的 ClientAppState 枚举
            if (_globalClientManager.CurrentState == ClientAppState.InReplay)
            {
                _globalClientManager.TransitionToLobby();
                Debug.Log("[ClientReplayPlaybackController] 已退出回放模式，安全回退到 InLobby。");
            }
        }

        public void Tick(float deltaTime)
        {
            if (State != PlaybackState.Playing) return;
            if (LocalReplayRoom == null) return;

            LocalReplayRoom.Tick(deltaTime);

            _tickAccumulator += deltaTime;
            while (_tickAccumulator >= TickInterval)
            {
                _tickAccumulator -= TickInterval;
                _currentTick++;

                while (_frameIndex < _frames.Count && _frames[_frameIndex].Tick <= _currentTick)
                {
                    InjectFrame(_frames[_frameIndex]);
                    _frameIndex++;
                }

                if (_frameIndex >= _frames.Count)
                {
                    State = PlaybackState.Finished;
                    Debug.Log("[ClientReplayPlaybackController] 回放播放结束。");
                    break;
                }
            }
        }

        private void InjectFrame(ReplayFrame frame)
        {
            var metadata = _messageRegistry.GetByMessageId(frame.MessageId);
            if (metadata == null)
            {
                Debug.LogWarning($"[ClientReplayPlaybackController] 未知 MessageId={frame.MessageId}，跳过该帧。");
                return;
            }

            if (metadata.Direction != MessageDirection.S2C || metadata.Domain != MessageDomain.Room)
            {
                Debug.LogError(
                    $"[ClientReplayPlaybackController] 回放帧数据异常：包含非 S2CRoomMessage 协议 (Type={metadata.MessageType.Name})，已丢弃。");
                return;
            }

            object message = _serializer.Deserialize(frame.Payload, metadata.MessageType);
            if (message == null)
            {
                Debug.LogError($"[ClientReplayPlaybackController] 回放帧反序列化失败，MessageId={frame.MessageId}。");
                return;
            }

            LocalReplayRoom.MessageRouter.Dispatch(metadata, message, frame.RoomId);
        }

        private bool ParseReplayFile(byte[] data)
        {
            try
            {
                string content = Encoding.UTF8.GetString(data);
                string[] parts = content.Split(new[] { "\n---FRAMES---\n" }, StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    Debug.LogError("[ClientReplayPlaybackController] 回放文件格式错误：未找到分隔符。");
                    return false;
                }

                _header = JsonConvert.DeserializeObject<ReplayFileHeader>(parts[0]);
                if (_header == null || _header.RoomComponentIds == null)
                {
                    Debug.LogError("[ClientReplayPlaybackController] 回放文件头解析失败或 RoomComponentIds 缺失。");
                    return false;
                }

                _frames = JsonConvert.DeserializeObject<List<ReplayFrame>>(parts[1]);
                if (_frames == null) _frames = new List<ReplayFrame>();

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClientReplayPlaybackController] 解析回放文件异常：{ex.Message}");
                return false;
            }
        }
    }
}