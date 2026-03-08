using System;

namespace StellarNet.Shared.Identity
{
    /// <summary>
    /// 底层连接标识值类型，表示当前底层网络连接的唯一句柄映射。
    /// 底层网络库原生连接句柄必须在 Adapter 层映射为此统一值类型，上层业务不得直接依赖底层原生类型。
    /// ConnectionId 属于服务端接收链的运行时上下文，不属于 NetworkEnvelope 字段。
    /// 客户端业务逻辑不得持有或上传有效 ConnectionId 作为身份依据。
    /// 断线后 ConnectionId 可以失效，SessionId 可以保留用于重连恢复。
    /// </summary>
    public readonly struct ConnectionId : IEquatable<ConnectionId>
    {
        /// <summary>
        /// 底层连接的整型标识值，来源于 Mirror 的 NetworkConnection.connectionId。
        /// </summary>
        public readonly int Value;

        /// <summary>
        /// 表示无效或未绑定连接的默认值。
        /// </summary>
        public static readonly ConnectionId Invalid = new ConnectionId(-1);

        public ConnectionId(int value)
        {
            Value = value;
        }

        public bool IsValid => Value >= 0;

        public bool Equals(ConnectionId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is ConnectionId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => $"ConnId({Value})";

        public static bool operator ==(ConnectionId left, ConnectionId right) => left.Equals(right);

        public static bool operator !=(ConnectionId left, ConnectionId right) => !left.Equals(right);
    }
}