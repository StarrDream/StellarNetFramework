// Assets/StellarNetFramework/Shared/Identity/SessionId.cs

using System;

namespace StellarNet.Shared.Identity
{
    // SessionId 是服务端签发的会话标识值类型。
    // 客户端只能持有并回传 SessionId，不得自定义其权威值。
    // SessionId 不等价于账号 ID、角色 ID 或客户端本地生成 ID。
    // 失效、续期、替换与安全校验策略由服务端 SessionManager 控制。
    // 采用 readonly struct + string 内部存储，保持语义清晰且可序列化。
    [Serializable]
    public readonly struct SessionId : IEquatable<SessionId>
    {
        // 底层字符串值，由服务端签发时生成
        public readonly string Value;

        // 框架保留的无效占位值，用于表示"当前不存在有效会话"
        public static readonly SessionId Invalid = new SessionId(string.Empty);

        public SessionId(string value)
        {
            Value = value ?? string.Empty;
        }

        // 判断当前 SessionId 是否为有效值
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(SessionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj)
        {
            if (obj is SessionId other)
                return Equals(other);
            return false;
        }

        public override int GetHashCode() => Value != null ? Value.GetHashCode() : 0;

        public override string ToString() => $"SessionId({Value})";

        public static bool operator ==(SessionId left, SessionId right) => left.Equals(right);
        public static bool operator !=(SessionId left, SessionId right) => !left.Equals(right);
    }
}