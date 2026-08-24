# Machine（机器）

一台物理机或虚拟机。部署层的最小单元，Core 不建模。

## 定义

一个 Machine 对应一个 AgentBridge discovery 目录——AgentBridge 在这个目录下写入进程发现文件（pid + port），多个 App 进程通过它互认。这是机器边界的唯一表达形式：**环回网络 + 本机文件系统**。

## 代码

| 类型 | 位置 | 职责 |
|---|---|---|
| `MachineContext` | `src/Platform/Ludots.Platform.Abstractions/Hosting/MachineContext.cs` | MachineId + DiscoveryDirectory；枚举本机进程 |
| `DiscoveredProcess` | 同上 | 一个已发现进程的 pid/port/发现文件路径/时间戳 |

## 关键操作

### 枚举进程

```csharp
MachineContext machine = new("my-machine", @"C:\agentbridge\discovery");
IReadOnlyList<DiscoveredProcess> processes = machine.GetDiscoveredProcesses();
```

扫描 discovery 目录下的 `*.json`，解析 pid 和 port。文件写一半或格式错误跳过（IO 竞态安全）。

### 创建隔离机器（CI 用）

```csharp
MachineContext ci = MachineContext.CreateIsolated("ci-shard-1", @"C:\temp\ci");
```

创建独立子目录，多个 CI 分片各自有 discovery 目录，端口不冲突。

## 与其他层的关系

- Machine 上跑 N 个 App（进程）
- AgentBridge 的 Mock 能力在这个粒度生效：一个 Machine 上所有进程可通过环回 HTTP 控制
- Core 引擎代码不感知 Machine——它是部署/CI 概念

## 已知缺口

单 discovery 目录 + 47921 起 16 个端口探测——并行跑两组三进程验收会抢端口。远端/CI 并行化时需扩展为机器维度寻址。
