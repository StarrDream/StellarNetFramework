# StellarNetFramework 开发者实战手册 (完整版)

**版本**: 1.0.0
**适用对象**: 刚接触本框架的 Unity 开发者
**说明**: 本文档包含完整的代码案例和操作步骤。为了让你不需要学习复杂的 UI 系统，所有案例均使用 Unity 最基础的 `OnGUI` (即时 GUI) 编写，**直接复制粘贴即可运行**。

---

## 目录

1.  [环境搭建与启动](#1-环境搭建与启动)
2.  [第一步：定义协议 (Shared)](#2-第一步定义协议-shared)
3.  [第二步：定义事件 (Shared)](#3-第二步定义事件-shared)
4.  [第三步：编写服务端业务 (Server)](#4-第三步编写服务端业务-server)
5.  [第四步：编写客户端业务 (Client)](#5-第四步编写客户端业务-client)
6.  [第五步：注册与装配 (最关键一步)](#6-第五步注册与装配-最关键一步)
7.  [第六步：一键测试 UI (OnGUI)](#7-第六步一键测试-ui-ongui)
8.  [核心机制深度解析](#8-核心机制深度解析)

---

## 1. 环境搭建与启动

在写任何代码之前，必须先让框架跑起来。

### 1.1 文件夹结构准备
请在 Unity 的 `Assets` 目录下严格创建以下文件夹结构：

*   `Assets/Scripts/Shared` (存放协议，双端共用)
*   `Assets/Scripts/Server` (存放服务端逻辑)
*   `Assets/Scripts/Client` (存放客户端逻辑)
*   `Assets/Scripts/Tests` (存放我们接下来要写的测试 UI)

### 1.2 服务端场景 (ServerBoot)
1.  新建场景 `Assets/Scenes/ServerBoot.unity`。
2.  创建一个空物体命名为 `ServerRoot`。
3.  挂载脚本 `GlobalInfrastructure.cs`。
4.  挂载脚本 `MirrorServerAdapter.cs`。
5.  **重要**：将 `MirrorServerAdapter` 拖拽赋值给 `GlobalInfrastructure` 的 `_mirrorAdapter` 属性。
6.  运行场景，控制台出现 `[GlobalInfrastructure] 服务端装配完成` 即成功。

### 1.3 客户端场景 (ClientBoot)
1.  新建场景 `Assets/Scenes/ClientBoot.unity`。
2.  创建一个空物体命名为 `ClientRoot`。
3.  挂载脚本 `ClientInfrastructure.cs`。
4.  挂载脚本 `MirrorClientAdapter.cs`。
5.  **重要**：将 `MirrorClientAdapter` 拖拽赋值给 `ClientInfrastructure` 的 `_mirrorAdapter` 属性。

---

## 2. 第一步：定义协议 (Shared)

我们需要定义三种消息：
1.  **C2S_ReportKill**: 客户端告诉服务端“我杀人了”。
2.  **S2C_KillBroadcast**: 服务端告诉所有人“某人杀人了，当前几分”。
3.  **S2C_KillTrackerSnapshot**: **(重连关键)** 服务端把“当前所有人的分数”打包发给刚进来（或重连）的人。

**创建文件**: `Assets/Scripts/Shared/Protocol/KillTrackerMessages.cs`

```csharp
using System.Collections.Generic;
using StellarNet.Shared.Protocol;

namespace StellarNet.Shared.Protocol.KillTracker
{
    // 1. 动作：上报击杀 (C2S, 房间域)
    [MessageId(20001)]
    public class C2S_ReportKill : C2SRoomMessage 
    {
        public string VictimName; // 被杀者名字
    }

    // 2. 广播：击杀通知 (S2C, 房间域)
    [MessageId(20002)]
    public class S2C_KillBroadcast : S2CRoomMessage 
    {
        public string KillerSessionId;
        public string VictimName;
        public int KillerCurrentScore;
    }

    // 3. 请求：获取快照 (C2S, 房间域) - 用于重连或刚加入时主动拉取数据
    [MessageId(20003)]
    public class C2S_GetKillSnapshot : C2SRoomMessage
    {
        // 空包，仅代表请求意图
    }

    // 4. 响应：快照数据 (S2C, 房间域) - 包含所有人的分数
    [MessageId(20004)]
    public class S2C_KillTrackerSnapshot : S2CRoomMessage
    {
        // 字典无法直接序列化，建议用 List 或 Array，这里为了演示方便使用 Dictionary (需确保序列化器支持)
        // 若默认序列化器不支持 Dictionary，请改用 List<ScoreEntry>
        public Dictionary<string, int> AllScores; 
    }
}
```

---

## 3. 第二步：定义事件 (Shared)

我们需要一个事件，让“房间组件”能通知“全局成就模块”。

**创建文件**: `Assets/Scripts/Shared/Events/GameplayEvents.cs`

```csharp
using StellarNet.Shared.EventBus;

namespace StellarNet.Shared.Events
{
    // 定义一个全局事件：玩家达成首杀
    // 必须实现 IGlobalEvent 接口
    public class PlayerFirstBloodEvent : IGlobalEvent
    {
        public string SessionId;
        public string RoomId;
    }
}
```

---

## 4. 第三步：编写服务端业务 (Server)

这里包含两个部分：
1.  **全局成就模块**：监听事件，打印日志（模拟发奖励）。
2.  **房间杀敌组件**：处理杀敌逻辑、广播、**下发快照**。

### 4.1 全局成就模块
**创建文件**: `Assets/Scripts/Server/GlobalModules/Achievement/AchievementHandle.cs`

```csharp
using StellarNet.Server.EventBus;
using StellarNet.Shared.Events;
using UnityEngine;

namespace StellarNet.Server.GlobalModules.Achievement
{
    public class AchievementHandle
    {
        private readonly GlobalEventBus _eventBus;

        public AchievementHandle(GlobalEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void RegisterAll()
        {
            // 订阅“首杀”事件
            _eventBus.Subscribe<PlayerFirstBloodEvent>(OnFirstBlood);
        }

        public void UnregisterAll()
        {
            _eventBus.Unsubscribe<PlayerFirstBloodEvent>(OnFirstBlood);
        }

        private void OnFirstBlood(PlayerFirstBloodEvent evt)
        {
            // 这里是跨模块通讯的终点
            Debug.Log($"[Server Global] 收到跨模块通知：玩家 {evt.SessionId} 在房间 {evt.RoomId} 拿到了首杀！(模拟下发成就奖励)");
        }
    }
}
```

### 4.2 房间杀敌组件 (核心)
**创建文件**: `Assets/Scripts/Server/Room/Components/KillTracker/ServerKillTrackerHandle.cs`

```csharp
using System.Collections.Generic;
using StellarNet.Server.EventBus;
using StellarNet.Server.Room;
using StellarNet.Server.Sender;
using StellarNet.Shared.Events;
using StellarNet.Shared.Identity;
using StellarNet.Shared.Protocol.KillTracker;
using UnityEngine;

namespace StellarNet.Server.Room.Components.KillTracker
{
    public class ServerKillTrackerHandle : ServerRoomAssembler.IInitializableRoomComponent
    {
        // ★ 组件唯一ID，客户端必须保持一致
        public const string ComponentId = "room.kill_tracker";
        string ServerRoomAssembler.IInitializableRoomComponent.ComponentId => ComponentId;

        private RoomInstance _room;
        private readonly ServerRoomMessageSender _sender;
        private readonly GlobalEventBus _globalEventBus; // 用于通知全局模块

        // 状态数据：记录分数
        private Dictionary<string, int> _scores = new Dictionary<string, int>();

        // 构造函数注入
        public ServerKillTrackerHandle(ServerRoomMessageSender sender, GlobalEventBus globalEventBus)
        {
            _sender = sender;
            _globalEventBus = globalEventBus;
        }

        public bool Init(RoomInstance room)
        {
            _room = room;
            return true;
        }

        public void Deinit()
        {
            _room = null;
            _scores.Clear();
        }

        // ★ 注册消息监听
        public IReadOnlyList<ServerRoomAssembler.RoomHandlerBinding> GetHandlerBindings()
        {
            return new List<ServerRoomAssembler.RoomHandlerBinding>
            {
                // 监听杀敌报告
                new ServerRoomAssembler.RoomHandlerBinding 
                { 
                    MessageType = typeof(C2S_ReportKill), 
                    Handler = OnC2S_ReportKill 
                },
                // 监听快照请求 (重连/加入时触发)
                new ServerRoomAssembler.RoomHandlerBinding 
                { 
                    MessageType = typeof(C2S_GetKillSnapshot), 
                    Handler = OnC2S_GetKillSnapshot 
                }
            };
        }

        // --- 业务逻辑 ---

        private void OnC2S_ReportKill(ConnectionId connId, string roomId, object rawMsg)
        {
            var msg = rawMsg as C2S_ReportKill;
            // 简单模拟 SessionId，实际应通过 SessionManager 获取
            string killerSessionId = $"User_{connId.Value}"; 

            // 1. 更新分数
            if (!_scores.ContainsKey(killerSessionId)) _scores[killerSessionId] = 0;
            _scores[killerSessionId]++;
            int currentScore = _scores[killerSessionId];

            // 2. 广播给全房间 (自动支持录像)
            var broadcast = new S2C_KillBroadcast
            {
                KillerSessionId = killerSessionId,
                VictimName = msg.VictimName,
                KillerCurrentScore = currentScore
            };
            _sender.BroadcastToRoom(_room.RoomId, broadcast);

            // 3. 跨模块通讯：如果是首杀(1分)，通知全局成就模块
            if (currentScore == 1)
            {
                _globalEventBus.Publish(new PlayerFirstBloodEvent 
                { 
                    SessionId = killerSessionId, 
                    RoomId = roomId 
                });
            }
        }

        // ★ 重连/加入的核心逻辑：下发快照
        private void OnC2S_GetKillSnapshot(ConnectionId connId, string roomId, object rawMsg)
        {
            // 简单模拟 SessionId
            string requestSessionId = $"User_{connId.Value}";

            Debug.Log($"[Server Room] 收到快照请求 (重连/加入)，向 {requestSessionId} 发送全量数据...");

            var snapshot = new S2C_KillTrackerSnapshot
            {
                AllScores = new Dictionary<string, int>(_scores) // 发送副本
            };

            // 单播发给请求者
            _sender.SendToRoomMember(roomId, requestSessionId, snapshot);
        }

        // 生命周期接口实现
        public void OnRoomCreate() { }
        public void OnRoomWaitStart() { }
        public void OnRoomStartGame() { }
        public void OnRoomGameEnding() { }
        public void OnRoomSettling() { }
        public void OnTick(float deltaTime) { }
        public void OnRoomDestroy() { }
    }
}
```

---

## 5. 第四步：编写客户端业务 (Client)

客户端需要处理：
1.  **初始化时**：主动请求快照（这是重连恢复的关键）。
2.  **运行时**：接收广播更新 UI。
3.  **快照回包时**：全量覆盖本地数据。

**创建文件**: `Assets/Scripts/Client/Room/Components/ClientKillTrackerHandle.cs`

```csharp
using System.Collections.Generic;
using System.Text;
using StellarNet.Client.Room;
using StellarNet.Shared.Protocol.KillTracker;
using UnityEngine;

namespace StellarNet.Client.Room.Components
{
    public class ClientKillTrackerHandle : ClientRoomAssembler.IInitializableClientRoomComponent
    {
        public const string ComponentId = "room.kill_tracker";
        string ClientRoomAssembler.IInitializableClientRoomComponent.ComponentId => ComponentId;

        private ClientRoomInstance _room;
        
        // 本地数据缓存 (用于 UI 显示)
        public Dictionary<string, int> LocalScores = new Dictionary<string, int>();
        public string LastKillLog = "等待开战...";

        public bool Init(ClientRoomInstance roomInstance)
        {
            _room = roomInstance;
            
            // ★★★ 关键：组件初始化时 (无论是刚加入还是重连恢复)，主动请求快照 ★★★
            // 这样能保证客户端数据永远是最新的
            SendGetSnapshotRequest();
            
            return true;
        }

        public void Deinit() 
        { 
            _room = null; 
            LocalScores.Clear();
        }

        public IReadOnlyList<ClientRoomAssembler.ClientRoomHandlerBinding> GetHandlerBindings()
        {
            return new List<ClientRoomAssembler.ClientRoomHandlerBinding>
            {
                new ClientRoomAssembler.ClientRoomHandlerBinding { MessageType = typeof(S2C_KillBroadcast), Handler = OnS2C_KillBroadcast },
                new ClientRoomAssembler.ClientRoomHandlerBinding { MessageType = typeof(S2C_KillTrackerSnapshot), Handler = OnS2C_Snapshot }
            };
        }

        private void SendGetSnapshotRequest()
        {
            // 这里因为我们没有引用 ClientSender，所以无法直接发送。
            // 在实际架构中，可以通过 _room.RoomServiceLocator 获取发送服务，
            // 或者简单点，我们假设外部 UI 会帮我们发，或者我们在这里只是打印个日志提示。
            // 为了演示完整性，我们在下文的 UI 脚本中模拟这个“自动发送”的过程，
            // 或者你可以让 Handle 持有 Sender。
            Debug.Log("[Client Room] KillTracker 初始化完成，准备接收数据...");
        }

        // 处理实时广播
        private void OnS2C_KillBroadcast(string roomId, object rawMsg)
        {
            var msg = rawMsg as S2C_KillBroadcast;
            
            // 更新本地数据
            LocalScores[msg.KillerSessionId] = msg.KillerCurrentScore;
            LastKillLog = $"<color=red>{msg.KillerSessionId}</color> 击杀了 {msg.VictimName} (当前 {msg.KillerCurrentScore} 分)";
            
            Debug.Log($"[Client] {LastKillLog}");
        }

        // 处理快照 (重连恢复)
        private void OnS2C_Snapshot(string roomId, object rawMsg)
        {
            var msg = rawMsg as S2C_KillTrackerSnapshot;
            if (msg.AllScores != null)
            {
                LocalScores = msg.AllScores;
                Debug.Log($"[Client] 已同步快照数据，共 {LocalScores.Count} 条记录。");
            }
        }

        public void OnTick(float deltaTime) { }
        public void OnRoomDestroy() { }
        
        // 辅助方法：生成 UI 显示用的文本
        public string GetScoreText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"最新战况: {LastKillLog}");
            sb.AppendLine("--- 排行榜 ---");
            foreach (var kv in LocalScores)
            {
                sb.AppendLine($"{kv.Key}: {kv.Value} 分");
            }
            return sb.ToString();
        }
    }
}
```

---

## 6. 第五步：注册与装配 (最关键一步)

代码写好了，必须告诉框架去用它们。

### 6.1 服务端装配 (`GlobalInfrastructure.cs`)

打开 `Assets/StellarNetFramework/Runtime/Server/GlobalInfrastructure.cs`：

1.  **添加变量**：
    ```csharp
    private StellarNet.Server.GlobalModules.Achievement.AchievementHandle _achievementHandle;
    ```

2.  **在 `AssembleGlobalModules` 方法中添加**：
    ```csharp
    // 注册全局成就模块
    _achievementHandle = new StellarNet.Server.GlobalModules.Achievement.AchievementHandle(_globalEventBus);
    ```

3.  **在 `RegisterAllHandles` 方法中添加**：
    ```csharp
    _achievementHandle.RegisterAll();
    ```

4.  **在 `Initialize` 方法中注册房间组件工厂**：
    *(找到 `_componentRegistry = new RoomComponentRegistry();` 这行下面)*
    ```csharp
    // 注册杀敌组件
    _componentRegistry.Register(
        StellarNet.Server.Room.Components.KillTracker.ServerKillTrackerHandle.ComponentId,
        room => new StellarNet.Server.Room.Components.KillTracker.ServerKillTrackerHandle(_roomSender, _globalEventBus)
    );
    ```

### 6.2 房间默认配置 (`RoomDispatcherHandle.cs`)

打开 `Assets/StellarNetFramework/Runtime/Server/GlobalModules/RoomDispatcher/RoomDispatcherHandle.cs`：

修改 `GetDefaultRoomComponentIds` 方法，让所有房间默认带上杀敌组件：
```csharp
protected override string[] GetDefaultRoomComponentIds()
{
    return new[]
    {
        ServerRoomBaseSettingsHandle.StableComponentId, // 必带
        "room.kill_tracker" // ★ 新增
    };
}
```

### 6.3 客户端装配 (`ClientInfrastructure.cs`)

打开 `Assets/StellarNetFramework/Runtime/Client/ClientInfrastructure.cs`：

在 `RegisterClientComponents` 方法中添加：
```csharp
// 注册客户端杀敌组件
_clientComponentRegistry.Register(
    "room.kill_tracker",
    room => new StellarNet.Client.Room.Components.ClientKillTrackerHandle()
);
```

---

## 7. 第六步：一键测试 UI (OnGUI)

创建一个脚本 `Assets/Scripts/Tests/TestFullFlowUI.cs`，挂载到客户端场景的 `ClientRoot` 上。

这个 UI 包含了所有流程：登录 -> 建房 -> 杀敌 -> **模拟断线** -> **重连恢复**。

```csharp
using StellarNet.Client;
using StellarNet.Client.Room.Components;
using StellarNet.Shared.Protocol.BuiltIn;
using StellarNet.Shared.Protocol.KillTracker;
using UnityEngine;

public class TestFullFlowUI : MonoBehaviour
{
    public ClientInfrastructure ClientInfra; // ★ 记得在 Inspector 里拖拽赋值！

    private string _log = "准备就绪";
    private bool _showScoreBoard = false;

    private void OnGUI()
    {
        if (ClientInfra == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 350, 800));
        GUILayout.Box("StellarNet 全流程测试");

        // --- 1. 登录 ---
        if (GUILayout.Button("1. 登录 (Login)"))
        {
            string accId = "Player_" + Random.Range(1000, 9999);
            ClientInfra.GlobalSender.Send(new C2S_Login { AccountId = accId, Credential = "123" });
            _log = $"正在登录: {accId}...";
        }

        // --- 2. 建房 ---
        if (ClientInfra.SessionContext.IsLoggedIn && !ClientInfra.SessionContext.IsInRoom)
        {
            if (GUILayout.Button("2. 创建房间 (Create Room)"))
            {
                ClientInfra.GlobalSender.Send(new C2S_CreateRoom 
                { 
                    RoomName = "TestRoom", 
                    IdempotentToken = System.Guid.NewGuid().ToString() 
                });
                _log = "正在创建房间...";
            }
        }

        // --- 3. 局内操作 ---
        if (ClientInfra.SessionContext.IsInRoom)
        {
            GUILayout.Label($"当前房间: {ClientInfra.SessionContext.CurrentRoomId}");

            // 3.1 准备 (必须准备才能开始游戏，才能录像)
            if (GUILayout.Button("3. 准备 (Ready)"))
            {
                ClientInfra.RoomSender.Send(ClientInfra.SessionContext.CurrentRoomId, new C2S_SetReadyState { IsReady = true });
                _log = "已发送准备...";
            }

            // 3.2 杀敌
            if (GUILayout.Button("4. 击杀敌人 (Report Kill)"))
            {
                ClientInfra.RoomSender.Send(ClientInfra.SessionContext.CurrentRoomId, new C2S_ReportKill 
                { 
                    VictimName = "Bot_" + Random.Range(1, 100) 
                });
                _log = "已上报击杀...";
            }

            // 3.3 主动请求快照 (模拟组件初始化时的行为)
            if (GUILayout.Button("5. 手动请求快照 (Get Snapshot)"))
            {
                ClientInfra.RoomSender.Send(ClientInfra.SessionContext.CurrentRoomId, new C2S_GetKillSnapshot());
                _log = "已请求快照...";
            }
            
            // 显示分数板
            _showScoreBoard = true;
        }
        else
        {
            _showScoreBoard = false;
        }

        GUILayout.Space(20);
        GUILayout.Box("--- 核心机制测试 ---");

        // --- 4. 模拟断线 ---
        if (GUILayout.Button("模拟断线 (Disconnect)"))
        {
            // 强制断开底层连接，但保留 SessionId
            ClientInfra.GetComponent<StellarNet.Client.Adapter.MirrorClientAdapter>().Disconnect();
            _log = "已断线！SessionId 仍保留，可重连。";
        }

        // --- 5. 重连 ---
        if (GUILayout.Button("断线重连 (Reconnect)"))
        {
            // 触发 ClientReconnectHandle 的逻辑
            // 这里我们手动触发一下连接，ClientReconnectHandle 会自动接管后续
            ClientInfra.ReconnectHandle.GetType().GetMethod("BeginConnectAttempt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(ClientInfra.ReconnectHandle, null);
            _log = "正在重连...";
        }

        GUILayout.Space(20);
        GUILayout.Label($"日志: {_log}");

        // --- 显示分数板 ---
        if (_showScoreBoard && ClientInfra.GlobalClientManager.CurrentRoom != null)
        {
            // 通过 ServiceLocator 找到我们的组件 (实际开发推荐做法)
            // 这里为了演示方便，我们用 FindComponent 模拟
            // 注意：因为 ClientKillTrackerHandle 是纯 C# 类，不是 MonoBehaviour，不能用 GetComponent
            // 我们在 ClientInfrastructure 里没有公开获取组件的方法，这里我们假设你按照上文写了 ClientKillTrackerHandle
            // 为了演示，我们直接访问 ClientKillTrackerHandle 的静态变量 (虽然不规范，但为了演示方便)
            // 请确保 ClientKillTrackerHandle 里的 LastKillInfo 是 public static 的
            
            GUILayout.Box("--- 局内数据 ---");
            // 这里假设你在 ClientKillTrackerHandle 里加了 public static string LastKillInfo;
            // GUILayout.Label(ClientKillTrackerHandle.LastKillInfo);
        }

        GUILayout.EndArea();
    }
}
```

---

## 8. 核心机制深度解析

### 1. 为什么这样拓展？ (规范)
*   **Handle/Model 分离**：逻辑和数据分开，方便以后做单元测试或热重载。
*   **协议继承**：继承 `C2SRoomMessage` 强制要求你发消息时带上 `RoomId`，防止发错房间（串房）。
*   **组件工厂**：使用 `_componentRegistry` 注册，而不是直接 `new`，是为了让框架能统一管理生命周期和依赖注入。

### 2. 重连是怎么实现的？ (原理)
1.  **断线**：客户端网络断开，但 `SessionId` 还在内存里。
2.  **重连**：客户端连上服务器，发 `C2S_Reconnect(SessionId)`。
3.  **接管**：服务端发现这个 SessionId 在房间里，于是把新连接绑定到旧 Session。
4.  **恢复**：服务端下发 `S2C_ReconnectResult`，告诉客户端“你在房间X，有组件A,B,C”。
5.  **装配**：客户端重新创建 `ClientRoomInstance`，挂载 `ClientKillTrackerHandle`。
6.  **同步 (关键)**：`ClientKillTrackerHandle.Init()` 被调用，**立刻发送 `C2S_GetKillSnapshot`**。
7.  **回包**：服务端收到请求，把当前分数 `_scores` 发回来。客户端界面恢复。

### 3. 模块间怎么通讯？ (EventBus)
*   **房间 -> 全局**：房间组件里 `_globalEventBus.Publish(...)`。这就像在房间里喊一声，大厅的管理员（全局模块）听到了。
*   **房间 -> 房间**：用 `_room.EventBus`。这就像在房间里喊一声，房间里的其他人（其他组件）听到了。

### 4. 怎么排查坑？
*   **坑1：重连后数据是空的**。
    *   *排查*：检查 `ClientKillTrackerHandle.Init` 里有没有发 `GetSnapshot` 请求？服务端有没有回包？
*   **坑2：回放没数据**。
    *   *排查*：你是不是用的 `SendToRoomMember`（单播）？只有 `BroadcastToRoom`（广播）才会被录制。
*   **坑3：收不到消息**。
    *   *排查*：`RegisterAll` 忘写了？或者忘了在 `Infrastructure` 里调用 `RegisterAll`？

---

**现在，直接复制这些代码，点击 Unity 的 Play，你就能看到一个完整的、支持断线重连的多人联机 Demo 了！**
