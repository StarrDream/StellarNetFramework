# StellarNet 房间业务组件扩展手册

> **版本**: 1.0  
> **适用角色**: 服务端开发 / 客户端开发  
> **核心原则**: 意图分离、服务端权威、组件化装配

---

## 1. 核心概念

在 StellarNet 中，房间功能被拆分为一个个独立的**业务组件 (Room Component)**。
- **服务端组件**：负责逻辑校验、状态维护、数据同步。
- **客户端组件**：负责表现层渲染、输入转发。
- **关联纽带**：通过 `StableComponentId`（字符串常量）进行跨端映射。

**装配流程**：
1. 客户端请求建房（不带组件列表）。
2. 服务端根据配置（或默认策略）决定房间挂载哪些组件。
3. 服务端实例化组件并初始化。
4. 服务端将组件 ID 列表下发给客户端。
5. 客户端根据 ID 列表实例化对应的客户端组件。

---

## 2. 开发流程 (Step-by-Step)

假设我们要开发一个 **“房间内聊天组件” (RoomChat)**。

### 第一步：定义协议 (Shared)

在 `Shared/Protocol/Business/RoomChat/` 下定义协议。

```csharp
// C2S_RoomChat.cs
[MessageId(3001)] // 确保 ID 不冲突
public class C2S_RoomChat : C2SRoomMessage // 注意继承 C2SRoomMessage
{
    public string Content;
}

// S2C_RoomChat.cs
[MessageId(3002)]
public class S2C_RoomChat : S2CRoomMessage
{
    public string SenderId;
    public string Content;
}
```

### 第二步：服务端实现 (Server)

创建 `Server/Room/Components/RoomChat/ServerRoomChatHandle.cs`。

```csharp
using StellarNet.Server.Room;

public class ServerRoomChatHandle : ServerRoomAssembler.IInitializableRoomComponent
{
    // [关键] 跨端唯一标识，必须与客户端一致
    public const string StableComponentId = "room.chat"; 
    public string ComponentId => StableComponentId;

    private readonly ServerRoomMessageSender _sender;
    private RoomInstance _room;

    // 构造函数支持依赖注入 (从 GlobalInfrastructure 传入)
    public ServerRoomChatHandle(ServerRoomMessageSender sender)
    {
        _sender = sender;
    }

    public bool Init(RoomInstance room)
    {
        _room = room;
        // 初始化逻辑...
        return true;
    }

    public void Deinit() { /* 清理逻辑 */ }

    // 绑定协议路由
    public IReadOnlyList<RoomHandlerBinding> GetHandlerBindings()
    {
        return new[]
        {
            RoomHandlerBinding.Create<C2S_RoomChat>(OnC2S_RoomChat)
        };
    }

    private void OnC2S_RoomChat(ConnectionId connId, C2S_RoomChat msg)
    {
        // 广播给房间内所有人
        _sender.BroadcastToRoom(_room.RoomId, new S2C_RoomChat 
        { 
            SenderId = _room.GetMemberId(connId),
            Content = msg.Content 
        });
    }
}
```

### 第三步：客户端实现 (Client)

创建 `Client/Room/Components/RoomChat/ClientRoomChatHandle.cs`。

```csharp
using StellarNet.Client.Room;

public class ClientRoomChatHandle : ClientRoomAssembler.IInitializableClientRoomComponent
{
    // [关键] 必须与服务端一致
    public const string StableComponentId = "room.chat";
    public string ComponentId => StableComponentId;

    public void Init(ClientRoomInstance room)
    {
        // 监听服务端消息
        room.MessageRouter.Register<S2C_RoomChat>(OnS2C_RoomChat);
    }

    public void Deinit() { /* ... */ }

    private void OnS2C_RoomChat(S2C_RoomChat msg)
    {
        Debug.Log($"[聊天] {msg.SenderId}: {msg.Content}");
        // 触发 UI 事件...
    }
}
```

---

## 3. 注册与装配 (Dependency Injection)

这是最容易遗漏的一步。写了类不代表框架能识别它，必须手动注册到工厂中。

### 服务端注册
修改 `GlobalInfrastructure.cs` -> `RegisterBuiltInRoomComponents` 方法：

```csharp
private void RegisterBuiltInRoomComponents()
{
    // ... 原有代码 ...
    
    // [新增] 注册聊天组件
    _componentRegistry.Register(
        ServerRoomChatHandle.StableComponentId,
        // 这里的参数由 DI 容器自动填充
        room => new ServerRoomChatHandle(_roomSender) 
    );
}
```

### 客户端注册
修改 `ClientInfrastructure.cs` -> `RegisterClientComponents` 方法：

```csharp
private void RegisterClientComponents()
{
    // ... 原有代码 ...

    // [新增] 注册聊天组件
    _clientComponentRegistry.Register(
        ClientRoomChatHandle.StableComponentId,
        room => new ClientRoomChatHandle()
    );
}
```

---

## 4. 启用组件 (Assembly Strategy)

最后，你需要决定哪些房间拥有这个组件。

修改 `RoomDispatcherHandle.cs` -> `GetDefaultRoomComponentIds` 方法：

```csharp
protected virtual string[] GetDefaultRoomComponentIds()
{
    return new[]
    {
        ServerRoomBaseSettingsHandle.StableComponentId, // 基础骨架 (必须)
        ServerRoomChatHandle.StableComponentId          // [新增] 聊天组件
    };
}
```

**高级用法**：你也可以在 `OnC2S_CreateRoom` 中根据客户端传来的参数（如 `CreateRoomMessage.EnableChat`）动态构建这个列表。

---

## 5. 常见错误排查

| 错误现象 | 可能原因 | 解决方案 |
| :--- | :--- | :--- |
| `CreateComponent 失败...未找到对应工厂` | 忘记在 `Infrastructure` 中注册组件 | 检查步骤 3 |
| 客户端进房后报错，服务端正常 | 客户端 `StableComponentId` 与服务端不一致 | 检查字符串常量是否完全相同 |
| 消息发不出去 / 收不到 | 未在 `GetHandlerBindings` 或 `Init` 中注册协议 | 检查步骤 2 和 3 的协议绑定代码 |