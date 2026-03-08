namespace StellarNet.Server.Room
{
    /// <summary>
    /// 房间运行态枚举，表示 RoomInstance 框架对象的存活阶段。
    /// 此枚举只用于框架对象管理，不等于房间业务主生命周期。
    /// 业务阶段由 RoomBusinessPhase 独立表达，二者严格分离。
    /// </summary>
    public enum RoomLifecycleState
    {
        /// <summary>
        /// 房间正在初始化，组件装配尚未完成。
        /// </summary>
        Initializing,

        /// <summary>
        /// 房间已完成装配，正在运行中。
        /// Tick 持续存在不代表房间始终处于"正式对局进行中"，业务阶段由 RoomBusinessPhase 表达。
        /// </summary>
        Running,

        /// <summary>
        /// 房间正在销毁中，一旦进入此状态立即从可路由集合中移除。
        /// 禁止再接收任何新协议输入、新业务状态修改、新业务广播发送。
        /// 仅允许组件反初始化、总线清理、录像收尾、资源释放继续执行。
        /// </summary>
        Destroying,

        /// <summary>
        /// 房间已完全销毁，对象可被 GC 回收。
        /// </summary>
        Destroyed
    }
}