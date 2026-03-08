namespace StellarNet.Server.Room
{
    /// <summary>
    /// 房间业务主生命周期阶段枚举，表示房间当前所处的业务阶段。
    /// 与 RoomLifecycleState 严格分离：RoomLifecycleState 管理框架对象存活，此枚举管理业务语义阶段。
    /// 录制边界、重连恢复边界、业务单元阶段行为均挂靠此枚举，而不是仅看 RoomLifecycleState。
    /// </summary>
    public enum RoomBusinessPhase
    {
        /// <summary>
        /// 房间刚创建，尚未进入等待开始阶段。
        /// </summary>
        Created,

        /// <summary>
        /// 等待开始阶段，成员可加入、准备，但游戏尚未正式开始。
        /// 此阶段不纳入正式录制，即使发生公共广播也不自动视为可录制事件。
        /// </summary>
        WaitingForStart,

        /// <summary>
        /// 游戏进行中，正式对局阶段。
        /// 录制只在此阶段生效，服务端公共广播旁路录制在此阶段启用。
        /// </summary>
        InGame,

        /// <summary>
        /// 游戏结束处理中，对局已结束但结算尚未完成。
        /// </summary>
        GameEnding,

        /// <summary>
        /// 结算中，正在处理结算逻辑与数据落地。
        /// </summary>
        Settling,

        /// <summary>
        /// 房间业务已结束，等待 RoomInstance 完成框架级销毁流程。
        /// </summary>
        Ended
    }
}