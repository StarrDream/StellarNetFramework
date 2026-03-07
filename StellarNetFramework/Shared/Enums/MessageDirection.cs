// Assets/StellarNetFramework/Shared/Enums/MessageDirection.cs

namespace StellarNet.Shared.Enums
{
    // 协议方向枚举，由 MessageRegistryBuilder 从四协议基类直接推导，
    // 不允许开发者通过额外配置覆盖。
    public enum MessageDirection
    {
        // 客户端上行
        C2S = 0,

        // 服务端下行
        S2C = 1
    }
}