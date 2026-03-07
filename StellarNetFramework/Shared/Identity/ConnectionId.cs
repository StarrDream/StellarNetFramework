// Assets/StellarNetFramework/Shared/Identity/ConnectionId.cs

using System;

namespace StellarNet.Shared.Identity
{
    // ConnectionId 是框架统一的连接标识值类型。
    // 底层网络库（Mirror）的原生连接句柄只允许在 Adapter 层完成映射，
    // 上层业务、路由、发送链与会话系统全部依赖此类型，不得直接持有 Mirror 原生 int 句柄。
    // 采用 readonly struct 避免装箱，保持高频传递时的零 GC 压力。
    [Serializable]
    public readonly struct ConnectionId : IEquatable<ConnectionId>
    {
        // 底层原始值，仅由 Adapter 层在映射时赋值
        public readonly int RawValue;

        // 框架保留的无效占位值，用于表示"当前不存在有效连接"
        public static readonly ConnectionId Invalid = new ConnectionId(-1);

        public ConnectionId(int rawValue)
        {
            RawValue = rawValue;
        }

        // 判断当前 ConnectionId 是否为有效值
        public bool IsValid => RawValue >= 0;

        public bool Equals(ConnectionId other) => RawValue == other.RawValue;

        public override bool Equals(object obj)
        {
            if (obj is ConnectionId other)
                return Equals(other);
            return false;
        }

        public override int GetHashCode() => RawValue.GetHashCode();

        public override string ToString() => $"ConnectionId({RawValue})";

        public static bool operator ==(ConnectionId left, ConnectionId right) => left.Equals(right);
        public static bool operator !=(ConnectionId left, ConnectionId right) => !left.Equals(right);
    }
}