using System;

namespace StellarNet.Shared.Identity
{
    /// <summary>
    /// 服务端签发的会话标识值类型，不等价于账号 ID、角色 ID 或客户端本地生成 ID。
    /// SessionId 的失效、续期、替换与安全校验策略由服务端控制。
    /// 客户端只能持有并回传 SessionId，不得自定义其权威值。
    /// 断线后 SessionId 可以保留用于重连恢复，直到服务端判定其过期。
    /// </summary>
    public readonly struct SessionId : IEquatable<SessionId>
    {
        /// <summary>
        /// 会话标识字符串，由服务端在登录成功时签发。
        /// </summary>
        public readonly string Value;

        /// <summary>
        /// 表示无效或未签发会话的默认值。
        /// </summary>
        public static readonly SessionId Invalid = new SessionId(string.Empty);

        public SessionId(string value)
        {
            Value = value ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(SessionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is SessionId other && Equals(other);

        public override int GetHashCode() => Value?.GetHashCode() ?? 0;

        public override string ToString() => $"SessId({Value})";

        public static bool operator ==(SessionId left, SessionId right) => left.Equals(right);

        public static bool operator !=(SessionId left, SessionId right) => !left.Equals(right);
    }
}