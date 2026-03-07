// Assets/StellarNetFramework/Shared/Registry/MessageRegistryBuilder.cs

using System;
using System.Collections.Generic;
using System.Reflection;
using StellarNet.Shared.Enums;
using StellarNet.Shared.Protocol.Attributes;
using StellarNet.Shared.Protocol.Base;

namespace StellarNet.Shared.Registry
{
    // MessageRegistry 显式纯构建器。
    // 接受外部传入的程序集白名单并返回注册表实例，不持有任何静态状态。
    // 禁止采用静态自驱动初始化、隐式全域扫描或运行期懒构建方式生成 MessageRegistry。
    // 白名单程序集必须由客户端 / 服务端各自启动装配器显式指定，不得通过配置文件或程序集特性自动发现。
    // 编辑器程序集、测试程序集默认不参与扫描（由调用方控制白名单即可实现）。
    public static class MessageRegistryBuilder
    {
        // 框架保留号段上限，0~9999 仅供框架基础设施协议使用
        private const int FrameworkReservedIdMax = 9999;

        // 构建并返回 MessageRegistry 实例。
        // 参数 assemblies：本端程序集白名单，由调用方显式传入，不得为空。
        // 扫描过程中发现任何违规时直接抛出异常阻断启动，不允许静默跳过。
        public static MessageRegistry Build(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null)
                throw new ArgumentNullException(nameof(assemblies),
                    "[MessageRegistryBuilder] 程序集白名单不得为 null，必须由本端启动装配器显式传入");

            var idToMeta = new Dictionary<int, MessageMeta>();
            var typeToMeta = new Dictionary<Type, MessageMeta>();

            // 遍历白名单程序集，扫描所有带有 MessageIdAttribute 的协议类型
            foreach (var assembly in assemblies)
            {
                if (assembly == null)
                    continue;

                Type[] types;
                types = assembly.GetTypes();

                foreach (var type in types)
                {
                    // 只处理带有 MessageIdAttribute 的类型
                    var messageIdAttr = type.GetCustomAttribute<MessageIdAttribute>(inherit: false);
                    if (messageIdAttr == null)
                        continue;

                    // 校验：必须同时显式声明 DeliveryModeAttribute
                    var deliveryAttr = type.GetCustomAttribute<DeliveryModeAttribute>(inherit: false);
                    if (deliveryAttr == null)
                        throw new InvalidOperationException(
                            $"[MessageRegistryBuilder] 协议类型 {type.FullName} 带有 [MessageId] 但缺少 [DeliveryMode] 声明，" +
                            $"启动阶段强制阻断。所有带有 [MessageId] 的协议必须显式声明投递语义。");

                    // 校验：必须继承自四协议基类之一
                    var direction = ResolveDirection(type);
                    var domain = ResolveDomain(type);

                    if (direction == null || domain == null)
                        throw new InvalidOperationException(
                            $"[MessageRegistryBuilder] 协议类型 {type.FullName} 未继承四协议基类之一" +
                            $"（C2SGlobalMessage / C2SRoomMessage / S2CGlobalMessage / S2CRoomMessage），" +
                            $"启动阶段强制阻断。");

                    var messageId = messageIdAttr.Id;

                    // 校验：MessageId 重复检测，发现重复直接 Fatal 阻断
                    if (idToMeta.TryGetValue(messageId, out var existingMeta))
                        throw new InvalidOperationException(
                            $"[MessageRegistryBuilder] 发现重复 MessageId = {messageId}，" +
                            $"冲突类型：{existingMeta.MessageType.FullName} 与 {type.FullName}。" +
                            $"框架强制阻断启动，请检查协议 ID 分配。");

                    // 校验：同一 Type 不得重复注册（防止同一协议类型出现在多个程序集中）
                    if (typeToMeta.ContainsKey(type))
                        throw new InvalidOperationException(
                            $"[MessageRegistryBuilder] 协议类型 {type.FullName} 被重复扫描注册，" +
                            $"请检查程序集白名单是否存在重复引用。");

                    var meta = new MessageMeta(
                        messageId,
                        type,
                        direction.Value,
                        domain.Value,
                        deliveryAttr.Mode);

                    idToMeta[messageId] = meta;
                    typeToMeta[type] = meta;
                }
            }

            if (idToMeta.Count == 0)
            {
                // 允许空注册表（纯框架层启动时可能尚无业务协议），但输出提示便于排查白名单配置问题
                UnityEngine.Debug.LogWarning(
                    "[MessageRegistryBuilder] 扫描完成，注册表为空。" +
                    "请确认程序集白名单配置是否正确，以及业务协议是否已正确标注 [MessageId]。");
            }

            return new MessageRegistry(idToMeta, typeToMeta);
        }

        // 从协议类型的继承链推导消息方向。
        // 返回 null 表示未继承任何合法基类。
        private static MessageDirection? ResolveDirection(Type type)
        {
            if (typeof(C2SGlobalMessage).IsAssignableFrom(type) ||
                typeof(C2SRoomMessage).IsAssignableFrom(type))
                return MessageDirection.C2S;

            if (typeof(S2CGlobalMessage).IsAssignableFrom(type) ||
                typeof(S2CRoomMessage).IsAssignableFrom(type))
                return MessageDirection.S2C;

            return null;
        }

        // 从协议类型的继承链推导消息域归属。
        // 返回 null 表示未继承任何合法基类。
        private static MessageDomain? ResolveDomain(Type type)
        {
            if (typeof(C2SGlobalMessage).IsAssignableFrom(type) ||
                typeof(S2CGlobalMessage).IsAssignableFrom(type))
                return MessageDomain.Global;

            if (typeof(C2SRoomMessage).IsAssignableFrom(type) ||
                typeof(S2CRoomMessage).IsAssignableFrom(type))
                return MessageDomain.Room;

            return null;
        }
    }
}
