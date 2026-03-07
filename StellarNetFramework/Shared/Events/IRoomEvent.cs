// Assets/StellarNetFramework/Shared/Events/IRoomEvent.cs

namespace StellarNet.Shared.Events
{
    // 房间域领域事件标记接口。
    // RoomEventBus 只允许处理实现了此接口的事件类型。
    // 该接口与协议基类严格分层，不得混合继承形成语义污染。
    // 任何普通 DTO 或协议类型不得直接实现此接口后作为网络协议发送。
    public interface IRoomEvent
    {
    }
}