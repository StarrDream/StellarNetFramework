using StellarNet.Shared.EventBus;

namespace StellarNet.Server.Room.Events
{
    /// <summary>
    /// 房间内置事件聚合脚本。
    /// 这里统一承载房间骨架级出厂事件，避免事件类型过度离散造成工程维护成本上升。
    /// 这些事件只用于单个房间内业务组件之间的横向解耦传播，不用于跨房间或全局域广播。
    /// </summary>
    /// <summary>
    /// 房间成员加入事件。
    /// 由房间基础设置组件在成员加入并完成骨架同步后发布。
    /// </summary>
    public sealed class RoomMemberJoinedEvent : IRoomEvent
    {
        public string RoomId;
        public string SessionId;
    }

    /// <summary>
    /// 房间成员离开事件。
    /// 由房间基础设置组件在成员离开并完成骨架同步后发布。
    /// </summary>
    public sealed class RoomMemberLeftEvent : IRoomEvent
    {
        public string RoomId;
        public string SessionId;
        public string Reason;
    }

    /// <summary>
    /// 房间成员准备状态变化事件。
    /// 由房间基础设置组件在成员准备状态变化时发布。
    /// </summary>
    public sealed class RoomReadyStateChangedEvent : IRoomEvent
    {
        public string RoomId;
        public string SessionId;
        public bool IsReady;
    }

    /// <summary>
    /// 房间可开始状态变化事件。
    /// 由房间基础设置组件在可开始条件发生变化时发布。
    /// </summary>
    public sealed class RoomCanStartStateChangedEvent : IRoomEvent
    {
        public string RoomId;
        public bool CanStart;
    }
}