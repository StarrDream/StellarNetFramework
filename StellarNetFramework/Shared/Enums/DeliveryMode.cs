// Assets/StellarNetFramework/Shared/Enums/DeliveryMode.cs

namespace StellarNet.Shared.Enums
{
    // 协议投递语义枚举，框架固定支持三类。
    // 每个协议类型必须通过 DeliveryModeAttribute 显式声明其中一种，
    // 缺失声明时 MessageRegistryBuilder 在启动扫描阶段直接报错阻断。
    public enum DeliveryMode
    {
        // 可靠有序：保证送达且保证顺序，适用于状态变更、结算等关键协议
        ReliableOrdered = 0,

        // 可靠无序：保证送达但不保证顺序，适用于独立事件通知
        ReliableUnordered = 1,

        // 不可靠最新：不保证送达，只保留最新包，适用于高频位置/状态更新
        UnreliableLatest = 2
    }
}