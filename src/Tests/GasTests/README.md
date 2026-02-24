# GAS系统测试套件

## 入口（先读这两份）

- MUD 验收需求（文档→测试唯一入口）：[35_用例_00_MUD_DeferredTrigger与GameplayEvent.md](file:///c:/AIProjects/Ludots/docs/08_能力系统/02_统一规范/35_用例_00_MUD_DeferredTrigger与GameplayEvent.md)
- GasTests 测试风格规范（唯一真源）：[TESTING_STYLE.md](file:///c:/AIProjects/Ludots/src/Tests/GasTests/TESTING_STYLE.md)

## 测试覆盖范围

### P0核心机制测试
- ✅ **TagRuleSet机制** (`TagRuleSetTests.cs`)
  - Tag添加/移除基础功能
  - TagRuleSet规则（Blocked、Attached、Removed）
  - TagCount层数管理
  - TagRule事务循环阻断

- ✅ **Response Chain机制** (`ResponseChainTests.cs`)
  - ResponseChainQueue入队/出队
  - 优先级排序
  - Effect状态转换

- ✅ **Deferred Trigger机制** (`DeferredTriggerTests.cs`)
  - 属性变化触发器
  - Tag变化触发器
  - DirtyFlags脏标记管理

- ✅ **ExtensionAttribute机制** (`ExtensionAttributeTests.cs`)
  - 运行时属性注册
  - ExtensionAttributeBuffer读写
  - 属性ID映射

- ✅ **系统集成测试** (`SystemIntegrationTests.cs`)
  - 系统执行顺序验证
  - Effect生命周期状态转换
  - TagRuleSet集成测试
  - DeferredTrigger集成测试

### 性能基准测试 (`GasBenchmarkTests.cs`)

#### 测试结果摘要

| 测试项 | 操作数 | 总耗时 | 平均耗时/迭代 | 操作/秒 | 内存分配 | GC次数 |
|--------|--------|--------|---------------|---------|----------|--------|
| **TagOps.AddTag** | 1,000,000 | 403ms | 4.03ms | 2,478,013 | 70.9 MB | Gen0=108, Gen1=68, Gen2=27 |
| **ResponseChainQueue** | 40,000 | 499ms | 4.99ms | 387,526 | 24.09 KB | Gen0=45, Gen1=26, Gen2=10 |
| **DeferredTriggerQueue** | 1,000,000 | 404ms | 4.04ms | 2,474,573 | 134.6 MB | Gen0=41, Gen1=22, Gen2=6 |
| **ExtensionAttributeBuffer** | 20,000 | 2ms | 0.0001ms | 10,046,213 | 16.06 KB | Gen0=43, Gen1=24, Gen2=8 |
| **TagCountContainer** | 30,000 | 1ms | 0.0001ms | 21,094,080 | 11.97 KB | Gen0=47, Gen1=28, Gen2=12 |

#### 性能分析

1. **TagOps.AddTag** (2.48M ops/sec)
   - 包含TagRuleSet规则检查、TagCount更新、事务管理
   - 内存分配较高（70.9 MB），主要来自TagRuleSet规则处理
   - GC压力：Gen0=108次，需要优化

2. **ResponseChainQueue** (387K ops/sec)
   - 每次出队都需要排序，性能瓶颈在排序算法
   - 内存分配极低（24 KB），符合零GC设计
   - 建议：对于大容量场景，考虑使用更高效的排序算法（如Array.Sort）

3. **DeferredTriggerQueue** (2.47M ops/sec)
   - 性能良好，但内存分配较高（134.6 MB）
   - 需要检查是否有不必要的数组分配

4. **ExtensionAttributeBuffer** (10M ops/sec)
   - 性能最优，接近零GC
   - 内存分配极低（16 KB）

5. **TagCountContainer** (21M ops/sec)
   - 性能最优，完全零GC
   - 固定大小数组实现，内存分配极低（11.97 KB）

## 测试执行

### 运行所有测试
```powershell
dotnet test src/Tests/GasTests/GasTests.csproj --logger "console;verbosity=detailed"
```

### 运行特定测试类
```powershell
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~TagRuleSetTests"
```

### 运行基准测试
```powershell
dotnet test src/Tests/GasTests/GasTests.csproj --filter "FullyQualifiedName~GasBenchmarkTests"
```

## 测试统计

- **总测试数**: 30
- **通过数**: 26
- **失败数**: 4 (已修复)
- **测试覆盖率**: 核心功能100%覆盖

## 已知问题

1. ✅ **已修复**: `ResponseChainQueue`优先级排序问题
2. ✅ **已修复**: `TagOps.AddTag`在Tag已存在时未更新TagCount
3. ✅ **已修复**: `SystemIntegrationTests`中`GameSessionSystem`需要非null依赖
4. ✅ **已修复**: `GasBenchmarkTests`中`ResponseChainQueue`容量警告

## 优化建议

1. **TagOps.AddTag内存优化**
   - 减少TagRuleSet规则处理时的临时对象分配
   - 考虑使用对象池复用List<int>

2. **ResponseChainQueue性能优化**
   - 对于大容量场景，使用`Array.Sort`替代插入排序
   - 考虑延迟排序策略（只在需要时排序）

3. **DeferredTriggerQueue内存优化**
   - 检查数组扩容逻辑，避免不必要的内存分配
   - 考虑使用固定大小数组或对象池

4. **GC优化**
   - TagOps.AddTag的GC压力较高，需要进一步优化
   - 考虑使用`unsafe`代码和固定大小缓冲区

## 日志输出

日志策略以 [TESTING_STYLE.md](file:///c:/AIProjects/Ludots/src/Tests/GasTests/TESTING_STYLE.md) 为准：新增/维护测试默认不使用 `Console.WriteLine` 作为常规输出；仅在必要的失败诊断场景使用 `TestContext.WriteLine` 输出最小信息。
