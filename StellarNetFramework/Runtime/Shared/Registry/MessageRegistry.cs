using System;
using System.Collections.Generic;
using System.Reflection;
using StellarNet.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Shared.Registry
{
    /// <summary>
    /// 协议注册表，是唯一的协议元数据来源。
    /// 采用显式纯构建器形态：接受外部传入的程序集白名单，返回已构建的注册表实例。
    /// 禁止静态自驱动初始化、隐式全域扫描或运行期懒构建。
    /// 双端共享的是注册规则与元数据结构，不共享运行时实例。
    /// 客户端与服务端必须在各自启动阶段基于本端程序集白名单独立构建本端注册表实例。
    /// 同时服务于接收链（MessageId → Type）与发送链（Type → MessageId）两侧。
    /// </summary>
    public sealed class MessageRegistry
    {
        // 接收链查表：MessageId → 元数据
        private readonly Dictionary<int, MessageMetadata> _idToMetadata = new Dictionary<int, MessageMetadata>();

        // 发送链查表：Type → 元数据
        private readonly Dictionary<Type, MessageMetadata> _typeToMetadata = new Dictionary<Type, MessageMetadata>();

        // 框架保留号段上限，开发者号段从 10000 开始
        private const int FrameworkReservedMaxId = 9999;

        private MessageRegistry()
        {
        }

        /// <summary>
        /// 构建协议注册表实例。
        /// 只扫描传入的程序集白名单，只处理带有 MessageIdAttribute 的协议类型。
        /// 任一校验失败（重复 ID、未继承四协议基类、非法传输模式特性）将直接抛出异常阻断启动。
        /// </summary>
        public static MessageRegistry Build(IEnumerable<Assembly> assemblyWhiteList)
        {
            if (assemblyWhiteList == null)
            {
                throw new ArgumentNullException(nameof(assemblyWhiteList),
                    "[MessageRegistry] 程序集白名单不能为 null，必须由启动装配器显式传入。");
            }

            var registry = new MessageRegistry();

            foreach (var assembly in assemblyWhiteList)
            {
                if (assembly == null)
                {
                    Debug.LogWarning("[MessageRegistry] 白名单中存在 null 程序集，已跳过。");
                    continue;
                }

                Type[] types = assembly.GetTypes();
                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type == null)
                    {
                        continue;
                    }

                    if (!type.IsClass || type.IsAbstract)
                    {
                        continue;
                    }

                    var attr = type.GetCustomAttribute<MessageIdAttribute>(inherit: false);
                    if (attr == null)
                    {
                        continue;
                    }

                    if (!TryInferDirectionAndDomain(type, out var direction, out var domain))
                    {
                        throw new InvalidOperationException(
                            $"[MessageRegistry] 协议类型 {type.FullName} 携带 [MessageId({attr.Id})]，" +
                            "但未继承四协议基类之一（C2SGlobalMessage / C2SRoomMessage / S2CGlobalMessage / S2CRoomMessage），启动阻断。");
                    }

                    ValidateNoDeliveryModeAttribute(type);

                    int id = attr.Id;
                    if (registry._idToMetadata.ContainsKey(id))
                    {
                        var existing = registry._idToMetadata[id];
                        throw new InvalidOperationException(
                            $"[MessageRegistry] 检测到重复 MessageId={id}，已注册类型={existing.MessageType.FullName}，" +
                            $"冲突类型={type.FullName}，启动阻断。");
                    }

                    var metadata = new MessageMetadata(id, type, direction, domain);
                    registry._idToMetadata[id] = metadata;
                    registry._typeToMetadata[type] = metadata;
                }
            }

            Debug.Log($"[MessageRegistry] 构建完成，共注册协议数量={registry._idToMetadata.Count}。");
            return registry;
        }

        /// <summary>
        /// 接收链查表：通过 MessageId 获取协议元数据。
        /// 查询失败返回 null。
        /// </summary>
        public MessageMetadata GetByMessageId(int messageId)
        {
            _idToMetadata.TryGetValue(messageId, out var metadata);
            return metadata;
        }

        /// <summary>
        /// 发送链查表：通过协议 CLR 类型获取元数据。
        /// 查询失败返回 null。
        /// </summary>
        public MessageMetadata GetByType(Type messageType)
        {
            if (messageType == null)
            {
                return null;
            }

            _typeToMetadata.TryGetValue(messageType, out var metadata);
            return metadata;
        }

        /// <summary>
        /// 发送链查表的泛型重载。
        /// </summary>
        public MessageMetadata GetByType<T>()
        {
            return GetByType(typeof(T));
        }

        /// <summary>
        /// 判断指定 MessageId 是否属于框架保留号段（0 - 9999）。
        /// </summary>
        public static bool IsFrameworkReservedId(int messageId)
        {
            return messageId >= 0 && messageId <= FrameworkReservedMaxId;
        }

        /// <summary>
        /// 从协议类型继承关系推导消息方向与域归属。
        /// </summary>
        private static bool TryInferDirectionAndDomain(Type type, out MessageDirection direction,
            out MessageDomain domain)
        {
            if (typeof(C2SGlobalMessage).IsAssignableFrom(type))
            {
                direction = MessageDirection.C2S;
                domain = MessageDomain.Global;
                return true;
            }

            if (typeof(C2SRoomMessage).IsAssignableFrom(type))
            {
                direction = MessageDirection.C2S;
                domain = MessageDomain.Room;
                return true;
            }

            if (typeof(S2CGlobalMessage).IsAssignableFrom(type))
            {
                direction = MessageDirection.S2C;
                domain = MessageDomain.Global;
                return true;
            }

            if (typeof(S2CRoomMessage).IsAssignableFrom(type))
            {
                direction = MessageDirection.S2C;
                domain = MessageDomain.Room;
                return true;
            }

            direction = default;
            domain = default;
            return false;
        }

        /// <summary>
        /// 校验协议类型上是否存在被禁止的传输模式标注。
        /// 协议层只允许 MessageId 一个元数据来源，不允许继续堆叠 DeliveryMode 一类语义。
        /// </summary>
        private static void ValidateNoDeliveryModeAttribute(Type type)
        {
            object[] attrs = type.GetCustomAttributes(inherit: false);
            for (int i = 0; i < attrs.Length; i++)
            {
                object attr = attrs[i];
                if (attr == null)
                {
                    continue;
                }

                string attrTypeName = attr.GetType().Name;
                if (string.IsNullOrEmpty(attrTypeName))
                {
                    continue;
                }

                if (attrTypeName.Contains("DeliveryMode") ||
                    attrTypeName.Contains("Reliable") ||
                    attrTypeName.Contains("Unreliable"))
                {
                    throw new InvalidOperationException(
                        $"[MessageRegistry] 协议类型 {type.FullName} 上检测到被禁止的传输模式标注 [{attrTypeName}]。" +
                        "框架明文禁止在业务协议上使用传输模式标注，启动阻断。");
                }
            }
        }
    }
}