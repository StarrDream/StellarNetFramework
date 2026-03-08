// ════════════════════════════════════════════════════════════════
// 文件：ProtocolScanner.cs
// 路径：Assets/StellarNetFramework/Editor/Scaffold/Core/ProtocolScanner.cs
// 职责：协议扫描服务，负责扫描当前工程中所有已编译的协议类。
//       利用 Unity TypeCache API 快速查找带有 [MessageId] 特性的类，
//       构建已用 ID 和已用类名的索引，供脚手架校验器进行全量防重检查。
// ════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Reflection;
using StellarNet.Shared.Protocol;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Editor.Scaffold
{
    /// <summary>
    /// 协议扫描器，提供对工程内现有协议的 ID 与命名冲突检测数据源。
    /// </summary>
    public sealed class ProtocolScanner
    {
        /// <summary>
        /// 已被占用的 MessageId 集合。
        /// </summary>
        public readonly HashSet<int> UsedIds = new HashSet<int>();

        /// <summary>
        /// 已被占用的协议类名集合（不含命名空间）。
        /// </summary>
        public readonly HashSet<string> UsedClassNames = new HashSet<string>();

        /// <summary>
        /// ID 到类名的映射，用于错误提示。
        /// </summary>
        public readonly Dictionary<int, string> IdToNameMap = new Dictionary<int, string>();

        /// <summary>
        /// 执行全量扫描。
        /// 建议在窗口打开、获得焦点或生成前调用。
        /// </summary>
        public void Scan()
        {
            UsedIds.Clear();
            UsedClassNames.Clear();
            IdToNameMap.Clear();

            // 使用 TypeCache 快速获取所有带有 MessageIdAttribute 的类型
            // 这比遍历 AppDomain 程序集要快得多，且包含所有已编译的用户代码
            var types = TypeCache.GetTypesWithAttribute<MessageIdAttribute>();

            foreach (var type in types)
            {
                // 获取特性实例
                var attr = type.GetCustomAttribute<MessageIdAttribute>();
                if (attr == null) continue;

                int id = attr.Id;
                string className = type.Name;

                // 记录 ID
                if (!UsedIds.Add(id))
                {
                    // 扫描阶段发现重复 ID，说明现有代码中已有冲突，记录 Warning 但不中断扫描
                    if (IdToNameMap.TryGetValue(id, out var existingName))
                    {
                        Debug.LogWarning($"[ProtocolScanner] 发现现有代码中存在重复 ID {id}: {existingName} 与 {className}");
                    }
                }
                else
                {
                    IdToNameMap[id] = className;
                }

                // 记录类名
                UsedClassNames.Add(className);
            }
        }

        /// <summary>
        /// 获取下一个建议的可用 ID（从 startId 开始向后查找）。
        /// </summary>
        public int GetNextAvailableId(int startId)
        {
            int candidate = startId;
            // 简单的线性查找，防止死循环设置上限
            while (UsedIds.Contains(candidate) && candidate < int.MaxValue)
            {
                candidate++;
            }

            return candidate;
        }
    }
}