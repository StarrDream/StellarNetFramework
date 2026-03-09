// ════════════════════════════════════════════════════════════════
// 文件：FrameworkRawMessage.cs
// 路径：Assets/StellarNetFramework/Runtime/Shared/Network/FrameworkRawMessage.cs
// 职责：Mirror 底层字节透传消息结构。
//       必须定义在 Shared 层且为 public，确保 Client 与 Server 引用同一类型，
//       保证 Mirror Weaver 生成一致的 MessageId。
// ════════════════════════════════════════════════════════════════

using Mirror;

namespace StellarNet.Shared.Network
{
    /// <summary>
    /// 框架底层字节透传消息结构。
    /// 用于在 Mirror 的 NetworkMessage 系统中承载框架的 NetworkEnvelope 序列化字节。
    /// </summary>
    public struct FrameworkRawMessage : NetworkMessage
    {
        public byte[] Data;
    }
}