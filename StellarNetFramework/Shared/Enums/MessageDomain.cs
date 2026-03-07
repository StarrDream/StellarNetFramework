// Assets/StellarNetFramework/Shared/Enums/MessageDomain.cs

namespace StellarNet.Shared.Enums
{
    // 协议域归属枚举，由 MessageRegistryBuilder 从四协议基类直接推导，
    // 不允许开发者通过额外配置覆盖。
    public enum MessageDomain
    {
        // 全局域：不依赖当前房间归属上下文
        Global = 0,

        // 房间域：必须绑定有效房间上下文
        Room = 1
    }
}