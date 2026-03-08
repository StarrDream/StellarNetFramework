using System;

namespace StellarNet.Shared.Protocol
{
    /// <summary>
    /// 协议唯一标识特性，标注在具体协议类型上，声明该协议的全局唯一 MessageId。
    /// 框架保留号段：0 - 9999，开发者自定义号段：10000 及以上。
    /// 启动扫描阶段将强制校验 MessageId 是否重复、是否落在合法号段。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class MessageIdAttribute : Attribute
    {
        /// <summary>
        /// 协议的全局唯一标识 ID。
        /// </summary>
        public int Id { get; }

        public MessageIdAttribute(int id)
        {
            Id = id;
        }
    }
}