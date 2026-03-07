// Assets/StellarNetFramework/Server/Modules/Replay/ReplayModule.cs

using UnityEngine;
using StellarNet.Server.Infrastructure.GlobalScope;
using StellarNet.Server.Room;

namespace StellarNet.Server.Modules.Replay
{
    // 回放模块，负责回放录制的全局生命周期管理。
    // 职责：为指定房间创建并挂载 ReplayRecorder，管理录制开启/停止，
    //       不负责回放文件的读取与回放逻辑（回放读取由客户端 ReplayPlaybackController 负责）。
    // 录制开启时机由业务层决定（如对局开始），不由框架自动触发。
    public sealed class ReplayModule : IGlobalService
    {
        private readonly GlobalRoomManager _roomManager;

        // 持久化写入委托，由业务层注入
        private System.Action<string, System.Collections.Generic.IReadOnlyList<ReplayFrame>> _flushWriter;

        public ReplayModule(GlobalRoomManager roomManager)
        {
            if (roomManager == null)
            {
                Debug.LogError("[ReplayModule] 初始化失败：roomManager 不得为 null");
                return;
            }

            _roomManager = roomManager;
        }

        // 注入持久化写入委托
        public void SetFlushWriter(
            System.Action<string, System.Collections.Generic.IReadOnlyList<ReplayFrame>> writer)
        {
            if (writer == null)
            {
                Debug.LogError("[ReplayModule] SetFlushWriter 失败：writer 不得为 null");
                return;
            }

            _flushWriter = writer;
        }

        // 为指定房间开启录制，创建 ReplayRecorder 并挂载到 RoomInstance
        // 参数 roomId：目标房间 ID
        // 参数 nowUnixMs：录制开始时间戳
        // 返回创建的 ReplayRecorder，失败返回 null
        public ReplayRecorder StartRecording(string roomId, long nowUnixMs)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[ReplayModule] StartRecording 失败：roomId 不得为空");
                return null;
            }

            var room = _roomManager.GetRoutableRoom(roomId);
            if (room == null)
            {
                Debug.LogError(
                    $"[ReplayModule] StartRecording 失败：RoomId={roomId} 不存在或不可路由");
                return null;
            }

            if (room.GetReplayRecorder() != null)
            {
                Debug.LogWarning(
                    $"[ReplayModule] StartRecording 警告：RoomId={roomId} 已挂载 ReplayRecorder，" +
                    $"本次开启录制已忽略。");
                return null;
            }

            var recorder = new ReplayRecorder(roomId, nowUnixMs);

            if (_flushWriter != null)
                recorder.SetFlushWriter(_flushWriter);

            room.SetReplayRecorder(recorder);
            return recorder;
        }

        // 停止指定房间的录制
        public void StopRecording(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Debug.LogError("[ReplayModule] StopRecording 失败：roomId 不得为空");
                return;
            }

            var room = _roomManager.GetRoutableRoom(roomId);
            if (room == null)
            {
                Debug.LogWarning(
                    $"[ReplayModule] StopRecording 警告：RoomId={roomId} 不存在或不可路由，" +
                    $"本次停止录制已忽略。");
                return;
            }

            var recorder = room.GetReplayRecorder() as ReplayRecorder;
            if (recorder == null)
            {
                Debug.LogWarning(
                    $"[ReplayModule] StopRecording 警告：RoomId={roomId} 未挂载有效 ReplayRecorder，" +
                    $"本次停止录制已忽略。");
                return;
            }

            recorder.Stop();
        }
    }
}
