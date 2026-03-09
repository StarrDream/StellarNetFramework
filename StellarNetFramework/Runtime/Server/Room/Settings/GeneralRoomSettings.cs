// ════════════════════════════════════════════════════════════════
// 文件：GeneralRoomSettings.cs
// 路径：Assets/StellarNetFramework/Runtime/Server/Room/Settings/GeneralRoomSettings.cs
// 职责：通用房间配置快照实现（IRoomSettings）。
//       作为房间的“蓝图”或“出生证明”，承载建房时的初始静态参数。
//       负责序列化自身以便写入回放文件头，供回放端还原房间初始环境。
//       不承载任何运行时逻辑，仅作为数据容器 (DTO)。
// ════════════════════════════════════════════════════════════════

using System.Text;
using Newtonsoft.Json;
using StellarNet.Shared.RoomSettings;

namespace StellarNet.Server.Room.Settings
{
    /// <summary>
    /// 通用房间配置实现。
    /// 包含房间名、最大人数、密码等基础参数。
    /// </summary>
    public class GeneralRoomSettings : IRoomSettings
    {
        // ── 业务参数 ──────────────────────────────────────────────
        public string RoomName;
        public int MaxMemberCount;
        public string Password;

        // ── 接口实现 ──────────────────────────────────────────────

        /// <summary>
        /// 配置格式标识，回放端根据此字段决定反序列化为哪个类。
        /// </summary>
        public string SettingsFormat => "GeneralJson";

        /// <summary>
        /// 配置版本号，用于版本兼容性校验。
        /// </summary>
        public int SettingsVersion => 1;

        /// <summary>
        /// 将配置序列化为字节数组。
        /// 框架将此数据原样写入回放文件头。
        /// </summary>
        public byte[] Serialize()
        {
            // 使用 JSON 序列化作为快照数据，简单且兼容性好
            string json = JsonConvert.SerializeObject(this);
            return Encoding.UTF8.GetBytes(json);
        }
    }
}