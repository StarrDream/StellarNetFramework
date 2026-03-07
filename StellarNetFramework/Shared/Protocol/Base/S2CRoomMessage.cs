// Assets/StellarNetFramework/Shared/Protocol/Base/S2CRoomMessage.cs

namespace StellarNet.Shared.Protocol.Base
{
    // 服务端下行、房间域协议基类。
    // 继承此基类的协议类型，其方向与域归属在类型系统层即被制度化固定为：
    // 方向 = S2C，域 = Room。
    // 典型场景：房间业务组件广播、局内公共状态变化、局内公共表现结果、房间内单体私有辅助消息。
    // 必须保留可读取的房间运行时上下文，用于状态过滤、回放隔离与延迟消息污染拦截。
    // 服务端入站链若收到此类型协议，必须视为非法协议并立即阻断。
    public abstract class S2CRoomMessage
    {
    }
}