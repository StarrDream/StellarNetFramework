using System;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace StellarNet.Shared.Serialization
{
    /// <summary>
    /// 基于 Newtonsoft.Json 的默认序列化实现，归属 Shared 层。
    /// 实现 ISerializer 抽象接口，框架默认提供此实现，开发者可替换为其他序列化方案。
    /// 序列化失败时输出详细 Error 日志并返回 null，不抛出异常掩盖错误。
    /// 客户端层与服务端持有一致的同实现实例，确保序列化一致。
    /// </summary>
    public sealed class NewtonsoftJsonSerializer : ISerializer
    {
        private readonly JsonSerializerSettings _settings;

        public NewtonsoftJsonSerializer()
        {
            _settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None,
                DateFormatHandling = DateFormatHandling.IsoDateFormat
            };
        }

        /// <summary>
        /// 将对象序列化为 UTF-8 编码的 JSON 字节数组。
        /// </summary>
        public byte[] Serialize(object obj)
        {
            if (obj == null)
            {
                Debug.LogError("[NewtonsoftJsonSerializer] Serialize 失败：传入对象为 null。");
                return null;
            }

            string json = JsonConvert.SerializeObject(obj, _settings);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError($"[NewtonsoftJsonSerializer] Serialize 失败：对象 {obj.GetType().Name} 序列化结果为空字符串。");
                return null;
            }

            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// 将 UTF-8 编码的 JSON 字节数组反序列化为指定类型的对象实例。
        /// </summary>
        public object Deserialize(byte[] data, Type targetType)
        {
            if (data == null || data.Length == 0)
            {
                Debug.LogError($"[NewtonsoftJsonSerializer] Deserialize 失败：data 为 null 或空数组，目标类型={targetType?.Name}。");
                return null;
            }

            if (targetType == null)
            {
                Debug.LogError("[NewtonsoftJsonSerializer] Deserialize 失败：targetType 为 null。");
                return null;
            }

            string json = Encoding.UTF8.GetString(data);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError(
                    $"[NewtonsoftJsonSerializer] Deserialize 失败：字节数组转换为 JSON 字符串后为空，目标类型={targetType.Name}。");
                return null;
            }

            object result = JsonConvert.DeserializeObject(json, targetType, _settings);
            if (result == null)
            {
                Debug.LogError(
                    $"[NewtonsoftJsonSerializer] Deserialize 失败：JSON 反序列化结果为 null，目标类型={targetType.Name}，JSON={json}。");
                return null;
            }

            return result;
        }

        /// <summary>
        /// 泛型反序列化重载。
        /// </summary>
        public T Deserialize<T>(byte[] data)
        {
            object result = Deserialize(data, typeof(T));
            if (result == null)
            {
                return default;
            }

            return (T)result;
        }
    }
}