using System.Collections.Generic;

namespace StellarNet.Server.GlobalModules.RoomDispatcher
{
    /// <summary>
    /// 房间调度模块 Model，保存房间调度流程所需的运行时状态。
    /// 不承载业务逻辑，不直接驱动任何网络发送。
    /// 幂等防重由 IdempotentCache 统一处理，此 Model 只保存调度辅助状态。
    /// </summary>
    public sealed class RoomDispatcherModel
    {
        // 正在执行建房流程的 SessionId 集合，防止并发重复触发
        private readonly HashSet<string> _creatingRoomSessionIds = new HashSet<string>();

        // 正在执行加房流程的 SessionId 集合，防止并发重复触发
        private readonly HashSet<string> _joiningRoomSessionIds = new HashSet<string>();

        /// <summary>
        /// 标记指定 SessionId 正在执行建房流程。
        /// 已在建房中的 SessionId 不允许重复进入，返回 false。
        /// </summary>
        public bool TryMarkCreating(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }
            return _creatingRoomSessionIds.Add(sessionId);
        }

        /// <summary>
        /// 清除指定 SessionId 的建房中标记。
        /// </summary>
        public void ClearCreating(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                _creatingRoomSessionIds.Remove(sessionId);
            }
        }

        /// <summary>
        /// 标记指定 SessionId 正在执行加房流程。
        /// </summary>
        public bool TryMarkJoining(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }
            return _joiningRoomSessionIds.Add(sessionId);
        }

        /// <summary>
        /// 清除指定 SessionId 的加房中标记。
        /// </summary>
        public void ClearJoining(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                _joiningRoomSessionIds.Remove(sessionId);
            }
        }

        public bool IsCreating(string sessionId) =>
            !string.IsNullOrEmpty(sessionId) && _creatingRoomSessionIds.Contains(sessionId);

        public bool IsJoining(string sessionId) =>
            !string.IsNullOrEmpty(sessionId) && _joiningRoomSessionIds.Contains(sessionId);
    }
}
