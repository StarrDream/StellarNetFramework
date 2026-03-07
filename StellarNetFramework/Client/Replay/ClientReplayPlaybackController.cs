// Assets/StellarNetFramework/Client/Replay/ClientReplayPlaybackController.cs

using System.Collections.Generic;
using UnityEngine;
using StellarNet.Shared.Registry;
using StellarNet.Shared.Serialization;
using StellarNet.Shared.Protocol.Base;
using StellarNet.Server.Modules.Replay;
using StellarNet.Client.Network.Router;

namespace StellarNet.Client.Replay
{
    // 客户端回放控制器，负责从持久化存储加载回放帧数据并按时序回放。
    // 回放期间不建立真实网络连接，直接将帧数据反序列化后注入 ClientRoomMessageRouter 派发。
    // 回放速度倍率可动态调整，支持暂停/继续/跳帧。
    // 回放控制器与正常游戏逻辑完全隔离，回放期间不允许发送任何上行消息。
    public sealed class ClientReplayPlaybackController
    {
        private readonly MessageRegistry _messageRegistry;
        private readonly ISerializer _serializer;
        private readonly ClientRoomMessageRouter _roomRouter;

        // 当前加载的回放帧序列
        private IReadOnlyList<ReplayFrame> _frames;

        // 当前回放帧索引
        private int _currentFrameIndex = 0;

        // 回放开始时的实际时间戳（Unix 毫秒），用于计算帧播放时机
        private long _playbackStartRealMs = 0;

        // 回放开始时第一帧的录制时间戳，用于计算相对时间偏移
        private long _playbackStartRecordMs = 0;

        // 回放速度倍率，默认 1.0（正常速度）
        private float _playbackSpeed = 1.0f;

        // 是否正在回放
        private bool _isPlaying = false;

        // 是否已暂停
        private bool _isPaused = false;

        // 回放完成回调
        private System.Action _onPlaybackCompleted;

        public ClientReplayPlaybackController(
            MessageRegistry messageRegistry,
            ISerializer serializer,
            ClientRoomMessageRouter roomRouter)
        {
            if (messageRegistry == null)
            {
                Debug.LogError("[ClientReplayPlaybackController] 初始化失败：messageRegistry 不得为 null");
                return;
            }

            if (serializer == null)
            {
                Debug.LogError("[ClientReplayPlaybackController] 初始化失败：serializer 不得为 null");
                return;
            }

            if (roomRouter == null)
            {
                Debug.LogError("[ClientReplayPlaybackController] 初始化失败：roomRouter 不得为 null");
                return;
            }

            _messageRegistry = messageRegistry;
            _serializer = serializer;
            _roomRouter = roomRouter;
        }

        // 加载回放帧数据，由业务层负责从持久化存储读取后传入
        public void LoadFrames(IReadOnlyList<ReplayFrame> frames)
        {
            if (frames == null || frames.Count == 0)
            {
                Debug.LogError("[ClientReplayPlaybackController] LoadFrames 失败：frames 不得为 null 或空");
                return;
            }

            _frames = frames;
            _currentFrameIndex = 0;
            _isPlaying = false;
            _isPaused = false;
        }

        // 开始回放，从当前帧索引位置开始
        // 参数 nowUnixMs：当前实际时间戳，用于计算帧播放时机
        public void Play(long nowUnixMs)
        {
            if (_frames == null || _frames.Count == 0)
            {
                Debug.LogError("[ClientReplayPlaybackController] Play 失败：未加载回放数据，请先调用 LoadFrames()");
                return;
            }

            if (_isPlaying && !_isPaused)
            {
                Debug.LogWarning("[ClientReplayPlaybackController] Play 警告：当前已在回放中，本次调用已忽略。");
                return;
            }

            _playbackStartRealMs = nowUnixMs;
            _playbackStartRecordMs = _frames[_currentFrameIndex].TimestampUnixMs;
            _isPlaying = true;
            _isPaused = false;
        }

        // 暂停回放
        public void Pause()
        {
            if (!_isPlaying)
            {
                Debug.LogWarning("[ClientReplayPlaybackController] Pause 警告：当前未在回放中，本次调用已忽略。");
                return;
            }

            _isPaused = true;
        }

        // 继续回放（从暂停状态恢复）
        // 参数 nowUnixMs：当前实际时间戳，重新校准播放时间基准
        public void Resume(long nowUnixMs)
        {
            if (!_isPlaying || !_isPaused)
            {
                Debug.LogWarning(
                    "[ClientReplayPlaybackController] Resume 警告：当前未处于暂停状态，本次调用已忽略。");
                return;
            }

            // 重新校准时间基准，以当前帧为新的起点
            _playbackStartRealMs = nowUnixMs;
            if (_currentFrameIndex < _frames.Count)
                _playbackStartRecordMs = _frames[_currentFrameIndex].TimestampUnixMs;

            _isPaused = false;
        }

        // 停止回放并重置状态
        public void Stop()
        {
            _isPlaying = false;
            _isPaused = false;
            _currentFrameIndex = 0;
        }

        // 跳转到指定帧索引
        // 注意：跳帧不会补偿跳过帧的状态，业务层需自行处理跳帧后的状态重建
        public void SeekToFrame(int frameIndex, long nowUnixMs)
        {
            if (_frames == null || _frames.Count == 0)
            {
                Debug.LogError("[ClientReplayPlaybackController] SeekToFrame 失败：未加载回放数据");
                return;
            }

            if (frameIndex < 0 || frameIndex >= _frames.Count)
            {
                Debug.LogError(
                    $"[ClientReplayPlaybackController] SeekToFrame 失败：frameIndex={frameIndex} 越界，" +
                    $"有效范围 0~{_frames.Count - 1}");
                return;
            }

            _currentFrameIndex = frameIndex;
            _playbackStartRealMs = nowUnixMs;
            _playbackStartRecordMs = _frames[_currentFrameIndex].TimestampUnixMs;
        }

        // 设置回放速度倍率
        public void SetPlaybackSpeed(float speed)
        {
            if (speed <= 0f)
            {
                Debug.LogError(
                    $"[ClientReplayPlaybackController] SetPlaybackSpeed 失败：speed 必须大于 0，" +
                    $"当前值={speed}");
                return;
            }

            _playbackSpeed = speed;
        }

        // 注入回放完成回调
        public void SetOnPlaybackCompletedCallback(System.Action callback)
        {
            if (callback == null)
            {
                Debug.LogError(
                    "[ClientReplayPlaybackController] SetOnPlaybackCompletedCallback 失败：callback 不得为 null");
                return;
            }

            _onPlaybackCompleted = callback;
        }

        // 每帧驱动，由 ClientInfrastructure.Update() 在回放模式下调用
        // 参数 nowUnixMs：当前实际时间戳（Unix 毫秒）
        public void Tick(long nowUnixMs)
        {
            if (!_isPlaying || _isPaused)
                return;

            if (_frames == null || _currentFrameIndex >= _frames.Count)
                return;

            // 计算当前回放进度对应的录制时间戳
            var elapsedRealMs = nowUnixMs - _playbackStartRealMs;
            var elapsedRecordMs = (long)(elapsedRealMs * _playbackSpeed);
            var currentRecordTime = _playbackStartRecordMs + elapsedRecordMs;

            // 派发所有录制时间戳 <= 当前回放进度的帧
            while (_currentFrameIndex < _frames.Count)
            {
                var frame = _frames[_currentFrameIndex];
                if (frame.TimestampUnixMs > currentRecordTime)
                    break;

                DispatchFrame(frame);
                _currentFrameIndex++;
            }

            // 检查回放是否已完成
            if (_currentFrameIndex >= _frames.Count)
            {
                _isPlaying = false;
                _onPlaybackCompleted?.Invoke();
            }
        }

        // 将单帧数据反序列化后注入 ClientRoomMessageRouter 派发
        private void DispatchFrame(ReplayFrame frame)
        {
            if (frame == null)
                return;

            var meta = _messageRegistry.GetMetaById(frame.MessageId);
            if (meta == null)
            {
                Debug.LogError(
                    $"[ClientReplayPlaybackController] DispatchFrame 失败：" +
                    $"未知 MessageId={frame.MessageId}，帧序号={frame.Sequence}，已跳过。");
                return;
            }

            var messageObj = _serializer.Deserialize(frame.Payload, meta.MessageType);
            if (messageObj == null)
            {
                Debug.LogError(
                    $"[ClientReplayPlaybackController] DispatchFrame 失败：反序列化失败，" +
                    $"MessageId={frame.MessageId}，Type={meta.MessageType.Name}，帧序号={frame.Sequence}，已跳过。");
                return;
            }

            var roomMsg = messageObj as S2CRoomMessage;
            if (roomMsg == null)
            {
                Debug.LogError(
                    $"[ClientReplayPlaybackController] DispatchFrame 失败：" +
                    $"协议类型 {meta.MessageType.Name} 不是 S2CRoomMessage，帧序号={frame.Sequence}，已跳过。");
                return;
            }

            _roomRouter.Dispatch(roomMsg);
        }

        // 当前是否正在回放（含暂停状态）
        public bool IsPlaying => _isPlaying;

        // 当前是否已暂停
        public bool IsPaused => _isPaused;

        // 当前帧索引
        public int CurrentFrameIndex => _currentFrameIndex;

        // 总帧数
        public int TotalFrameCount => _frames?.Count ?? 0;

        // 回放进度（0.0 ~ 1.0）
        public float Progress => _frames == null || _frames.Count == 0
            ? 0f
            : (float)_currentFrameIndex / _frames.Count;
    }
}
