// Assets/StellarNetFramework/Shared/Registry/MessageRegistry.cs

using System;
using System.Collections.Generic;
using StellarNet.Shared.Enums;

namespace StellarNet.Shared.Registry
{
    // 协议注册表，同时服务于发送链与接收链，双端共用同一份元数据结构。
    // 客户端与服务端必须在各自启动阶段基于本端程序集白名单独立构建本端注册表实例。
    // 双端共享的是注册规则与元数据结构，不共享运行时实例，不共享本端装配结果。
    // 注册表仅在启动阶段由 MessageRegistryBuilder 构建一次，运行期禁止动态覆盖或静默替换。
    // 不允许发送侧再维护平行的类型到 ID 私有映射表。
    public sealed class MessageRegistry
    {
        // 接收链查询路径：MessageId → MessageMeta
        private readonly Dictionary<int, MessageMeta> _idToMeta;

        // 发送链查询路径：协议 Type → MessageMeta
        private readonly Dictionary<Type, MessageMeta> _typeToMeta;

        // 由 MessageRegistryBuilder 在构建完成后调用，外部不得直接实例化
        internal MessageRegistry(
            Dictionary<int, MessageMeta> idToMeta,
            Dictionary<Type, MessageMeta> typeToMeta)
        {
            _idToMeta = idToMeta;
            _typeToMeta = typeToMeta;
        }

        // 接收链：通过 MessageId 查询协议元数据
        // 查找失败返回 null，由调用方决定是否阻断
        public MessageMeta GetMetaById(int messageId)
        {
            _idToMeta.TryGetValue(messageId, out var meta);
            return meta;
        }

        // 发送链：通过协议 Type 查询协议元数据
        // 查找失败返回 null，由调用方决定是否阻断
        public MessageMeta GetMetaByType(Type messageType)
        {
            if (messageType == null)
                return null;

            _typeToMeta.TryGetValue(messageType, out var meta);
            return meta;
        }

        // 发送链泛型重载，避免调用方手动传入 typeof
        public MessageMeta GetMetaByType<T>()
        {
            return GetMetaByType(typeof(T));
        }

        // 判断指定 MessageId 是否已注册
        public bool ContainsId(int messageId)
        {
            return _idToMeta.ContainsKey(messageId);
        }

        // 判断指定协议 Type 是否已注册
        public bool ContainsType(Type messageType)
        {
            if (messageType == null)
                return false;
            return _typeToMeta.ContainsKey(messageType);
        }

        // 获取当前注册表中所有已注册协议元数据的只读快照，用于启动自检与诊断
        public IReadOnlyCollection<MessageMeta> GetAllMeta()
        {
            return _idToMeta.Values;
        }

        // 获取当前注册表中已注册协议总数
        public int Count => _idToMeta.Count;

        // 按方向与域归属过滤，用于启动自检阶段验证本端注册表是否存在反向协议误注册
        public IEnumerable<MessageMeta> GetMetaByDirectionAndDomain(
            MessageDirection direction,
            MessageDomain domain)
        {
            foreach (var meta in _idToMeta.Values)
            {
                if (meta.Direction == direction && meta.Domain == domain)
                    yield return meta;
            }
        }
    }
}
