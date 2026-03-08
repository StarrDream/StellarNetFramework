using System;

namespace StellarNet.Shared.Identity
{
    /// <summary>
    /// 房间标识值类型，是房间隔离、消息路由、重连恢复、录制归档的关键上下文标识。
    /// RoomId 由服务端统一生成，采用：当前实时时间戳 + 随机码 + 房间名 的组合方式。
    /// 不允许采用单纯自增号、单纯时间戳、单纯随机串等低信息量方式作为正式规范。
    /// </summary>
    public readonly struct RoomId : IEquatable<RoomId>
    {
        /// <summary>
        /// 房间标识字符串，格式为：{UnixMs}_{RandomCode}_{RoomName}。
        /// 例如：1741420800000_A3F7_MyRoom
        /// </summary>
        public readonly string Value;

        /// <summary>
        /// 表示无效或未绑定房间的默认值。
        /// </summary>
        public static readonly RoomId Invalid = new RoomId(string.Empty);

        public RoomId(string value)
        {
            Value = value ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public bool Equals(RoomId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is RoomId other && Equals(other);

        public override int GetHashCode() => Value?.GetHashCode() ?? 0;

        public override string ToString() => $"RoomId({Value})";

        public static bool operator ==(RoomId left, RoomId right) => left.Equals(right);

        public static bool operator !=(RoomId left, RoomId right) => !left.Equals(right);

        /// <summary>
        /// 生成新的 RoomId，采用时间戳 + 随机码 + 房间名的组合规则。
        /// 此方法只允许在服务端建房流程中调用，客户端不得自行生成 RoomId。
        /// </summary>
        /// <param name="roomName">房间名，用于提升 RoomId 的可读性与可诊断性。</param>
        public static RoomId Generate(string roomName)
        {
            if (string.IsNullOrEmpty(roomName))
            {
                roomName = "Room";
            }

            // 采用 UnixMs + 4位随机大写字母数字码 + 房间名 的组合，保证唯一性与可读性
            long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string randomCode = GenerateRandomCode(4);

            // 对房间名中的非法字符做基础清洗，防止路由解析歧义
            string safeName = SanitizeRoomName(roomName);
            string value = $"{unixMs}_{randomCode}_{safeName}";
            return new RoomId(value);
        }

        private static string GenerateRandomCode(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = chars[random.Next(chars.Length)];
            }
            return new string(result);
        }

        private static string SanitizeRoomName(string name)
        {
            // 只保留字母、数字、中文字符，替换其他字符为空，防止下划线分隔符被污染
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            // 若清洗后为空，使用默认名
            return sb.Length > 0 ? sb.ToString() : "Room";
        }
    }
}
