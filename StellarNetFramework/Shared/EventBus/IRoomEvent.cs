namespace StellarNet.Shared.EventBus
{
    /// <summary>
    /// 房间域领域事件标记接口。
    /// RoomEventBus 只允许处理实现了此接口的事件类型，防止跨域事件误投递。
    /// 此接口与协议基类不得混合继承，网络协议类型不得直接作为 EventBus 事件类型使用。
    /// 典型场景：房间业务组件之间的领域事件传播，例如准备状态变更、游戏开始条件满足等。
    /// </summary>
    public interface IRoomEvent
    {
    }
}