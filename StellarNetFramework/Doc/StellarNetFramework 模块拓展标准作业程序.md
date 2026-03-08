# StellarNetFramework 模块拓展标准作业程序 (SOP)

**版本**: 1.0.0
**适用场景**: 开发一个需要“房间内玩法”与“全局系统”交互的功能（例如：房间内杀敌 -> 触发全局成就）。
**核心口诀**: **5 脚本 + 1 插线**

---

## 目录

1.  [核心概念：5+1 结构](#1-核心概念-51-结构)
2.  [阶段一：定义契约 (Shared)](#2-阶段一定义契约-shared)
3.  [阶段二：服务端业务 (Server)](#3-阶段二服务端业务-server)
4.  [阶段三：客户端业务 (Client)](#4-阶段三客户端业务-client)
5.  [阶段四：插线与装配 (Infrastructure) ★最重要★](#5-阶段四插线与装配-infrastructure-最重要)
6.  [开发完成检查清单 (Checklist)](#6-开发完成检查清单-checklist)

---

## 1. 核心概念：5+1 结构

要完成一个完整的跨模块功能，你需要创建 **5 个脚本文件**，并进行 **1 次关键的“插线”操作**。

| 序号 | 类型 | 脚本名称示例 | 作用 |
| :--- | :--- | :--- | :--- |
| **1** | **协议 (Protocol)** | `MyFeatureMessages.cs` | 定义客户端和服务端怎么说话（消息ID）。 |
| **2** | **事件 (Event)** | `MyFeatureEvents.cs` | 定义房间怎么通知全局（跨模块通讯）。 |
| **3** | **服务端组件** | `ServerMyFeatureHandle.cs` | 房间内的逻辑（如：判断杀敌）。 |
| **4** | **服务端全局** | `ServerMySystemHandle.cs` | 全局的逻辑（如：记录成就）。 |
| **5** | **客户端组件** | `ClientMyFeatureHandle.cs` | 客户端的表现（如：UI显示）。 |
| **+1** | **插线 (Wiring)** | `Infrastructure.cs` | **把上面写的代码注册到框架里，否则不运行！** |

---

## 2. 阶段一：定义契约 (Shared)

### 1. 写协议 (Protocol)
**位置**: `Assets/Scripts/Shared/Protocol/...`
**内容**:
```csharp
// 定义消息 ID (必须唯一，建议 20000+)
[MessageId(20001)] 
public class C2S_MyAction : C2SRoomMessage // 继承 RoomMessage 防止串房
{ 
    public int Data; 
}

[MessageId(20002)] 
public class S2C_MyResult : S2CRoomMessage 
{ 
    public bool Success; 
}
```

### 2. 写事件 (Event)
**位置**: `Assets/Scripts/Shared/Events/...`
**内容**:
```csharp
// 必须实现 IGlobalEvent 接口
public class MyGlobalEvent : IGlobalEvent 
{ 
    public string PlayerId; 
    public int Value;
}
```

---

## 3. 阶段二：服务端业务 (Server)

### 3. 写房间组件 (Room Component)
**位置**: `Assets/Scripts/Server/Room/Components/...`
**关键点**:
1.  定义 `public const string ComponentId = "room.my_feature";`。
2.  构造函数注入 `GlobalEventBus`。
3.  在逻辑中调用 `_globalEventBus.Publish(...)` 发送事件。

### 4. 写全局模块 (Global Module)
**位置**: `Assets/Scripts/Server/GlobalModules/...`
**关键点**:
1.  构造函数注入 `GlobalEventBus`。
2.  在 `RegisterAll()` 中调用 `_eventBus.Subscribe<MyGlobalEvent>(OnEvent)`。

---

## 4. 阶段三：客户端业务 (Client)

### 5. 写客户端组件 (Client Component)
**位置**: `Assets/Scripts/Client/Room/Components/...`
**关键点**:
1.  实现 `IInitializableClientRoomComponent`。
2.  `ComponentId` 必须和服务端完全一致 (`"room.my_feature"`)。
3.  在 `GetHandlerBindings()` 中注册监听 `S2C_MyResult`。

---

## 5. 阶段四：插线与装配 (Infrastructure) ★最重要★

**注意**：很多新手写完上面代码发现跑不通，99% 都是因为忘了这一步！

### 步骤 A：服务端插线 (`GlobalInfrastructure.cs`)

1.  **实例化全局模块**:
    *   找到 `AssembleGlobalModules()` 方法。
    *   添加：`_mySystemHandle = new ServerMySystemHandle(_globalEventBus);`
2.  **注册全局模块**:
    *   找到 `RegisterAllHandles()` 方法。
    *   添加：`_mySystemHandle.RegisterAll();`
3.  **注册房间组件工厂**:
    *   找到 `Initialize()` 方法。
    *   添加：
        ```csharp
        _componentRegistry.Register(
            ServerMyFeatureHandle.ComponentId, 
            room => new ServerMyFeatureHandle(_roomSender, _globalEventBus)
        );
        ```

### 步骤 B：配置房间默认加载 (`RoomDispatcherHandle.cs`)

*   找到 `GetDefaultRoomComponentIds()` 方法。
*   添加你的组件 ID：
    ```csharp
    return new[] { ..., "room.my_feature" };
    ```

### 步骤 C：客户端插线 (`ClientInfrastructure.cs`)

1.  **注册客户端组件工厂**:
    *   找到 `RegisterClientComponents()` 方法。
    *   添加：
        ```csharp
        _clientComponentRegistry.Register(
            "room.my_feature", // 必须和服务端 ID 一致
            room => new ClientMyFeatureHandle()
        );
        ```

---

## 6. 开发完成检查清单 (Checklist)

每完成一个功能，请对照此表打钩：

- [ ] **Protocol**: 消息 ID 没有重复（建议 10000+ 或 20000+）。
- [ ] **Protocol**: 房间消息继承了 `C2SRoomMessage`，全局消息继承了 `C2SGlobalMessage`。
- [ ] **Event**: 实现了 `IGlobalEvent` 接口。
- [ ] **Server**: 房间组件定义了唯一的 `ComponentId`。
- [ ] **Server**: 全局模块订阅了事件 (`Subscribe`)。
- [ ] **Client**: 客户端组件 ID 和服务端一致。
- [ ] **Infrastructure**: 服务端 `Register` 了组件工厂。
- [ ] **Infrastructure**: 服务端 `new` 并 `RegisterAll` 了全局模块。
- [ ] **Infrastructure**: 客户端 `Register` 了组件工厂。
- [ ] **RoomDispatcher**: 把组件 ID 加进默认列表了。

**全部打钩后，点击 Unity 运行，你的功能一定能跑通！**
