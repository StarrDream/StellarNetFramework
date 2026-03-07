// Assets/StellarNetFramework/Shared/Protocol/Base/C2SRoomMessage.cs

namespace StellarNet.Shared.Protocol.Base
{
    // 客户端上行、房间域协议基类。
    // 继承此基类的协议类型，其方向与域归属在类型系统层即被制度化固定为：
    // 方向 = C2S，域 = Room。
    // 典型场景：房间业务组件请求、局内操作上报。
    // 必须进入房间归属一致性校验链：
    // ConnectionId 有效 → 已绑定会话 → 会话存在 CurrentRoomId → RoomId 与 CurrentRoomId 完全一致。
    // 任意一步失败则框架直接丢弃消息，严禁继续向房间业务组件派发。
    public abstract class C2SRoomMessage
    {
    }
}