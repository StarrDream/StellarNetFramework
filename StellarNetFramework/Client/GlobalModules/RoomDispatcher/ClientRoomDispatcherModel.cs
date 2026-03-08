namespace StellarNet.Client.GlobalModules.RoomDispatcher
{
    /// <summary>
    /// 客户端房间调度模块 Model，保存建房/加房流程的本地运行时状态。
    /// 不承载业务逻辑，不直接驱动任何网络发送。
    /// 建房与加房的请求中间态（等待服务端响应期间）由此 Model 维护。
    /// </summary>
    public sealed class ClientRoomDispatcherModel
    {
        /// <summary>
        /// 当前是否正在等待建房响应。
        /// </summary>
        public bool IsWaitingCreateResult { get; private set; }

        /// <summary>
        /// 当前是否正在等待加房响应。
        /// </summary>
        public bool IsWaitingJoinResult { get; private set; }

        /// <summary>
        /// 最近一次建房失败原因。
        /// </summary>
        public string LastCreateFailReason { get; private set; }

        /// <summary>
        /// 最近一次加房失败原因。
        /// </summary>
        public string LastJoinFailReason { get; private set; }

        /// <summary>
        /// 当前加入的房间的组件清单，加房成功后写入，用于客户端装配本地房间结构。
        /// </summary>
        public string[] CurrentRoomComponentIds { get; private set; }

        public ClientRoomDispatcherModel()
        {
            IsWaitingCreateResult = false;
            IsWaitingJoinResult = false;
            LastCreateFailReason = string.Empty;
            LastJoinFailReason = string.Empty;
            CurrentRoomComponentIds = new string[0];
        }

        public void SetWaitingCreate(bool waiting) => IsWaitingCreateResult = waiting;
        public void SetWaitingJoin(bool waiting) => IsWaitingJoinResult = waiting;

        public void SetCreateFailed(string reason)
        {
            IsWaitingCreateResult = false;
            LastCreateFailReason = reason ?? string.Empty;
        }

        public void SetJoinFailed(string reason)
        {
            IsWaitingJoinResult = false;
            LastJoinFailReason = reason ?? string.Empty;
        }

        public void SetJoinSucceeded(string roomId, string[] componentIds)
        {
            IsWaitingJoinResult = false;
            LastJoinFailReason = string.Empty;
            CurrentRoomComponentIds = componentIds ?? new string[0];
        }

        public void SetCreateSucceeded()
        {
            IsWaitingCreateResult = false;
            LastCreateFailReason = string.Empty;
        }

        public void ClearRoomState()
        {
            CurrentRoomComponentIds = new string[0];
        }
    }
}
