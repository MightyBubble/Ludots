# Recipe: 新增输入动作

## 目标

在 Mod 中定义新的按键/鼠标操作，接入已有的输入管线。

## 文件清单

```
mods/MyMod/assets/Input/
└── default_input.json      ← 动作定义 + 按键绑定
```

## 输入配置（default_input.json）

```json
{
  "actions": [
    { "id": "MyMod_Dodge", "name": "Dodge", "type": "Button" },
    { "id": "MyMod_Aim", "name": "Aim Direction", "type": "Axis2D" }
  ],
  "contexts": [
    {
      "id": "MyMod_Gameplay",
      "name": "MyMod Gameplay",
      "priority": 100,
      "bindings": [
        { "actionId": "MyMod_Dodge", "path": "<Keyboard>/space" },
        {
          "actionId": "MyMod_Aim",
          "compositeType": "Vector2",
          "compositeParts": [
            { "path": "<Keyboard>/w" },
            { "path": "<Keyboard>/s" },
            { "path": "<Keyboard>/a" },
            { "path": "<Keyboard>/d" }
          ]
        }
      ]
    }
  ]
}
```

动作类型：`Button`、`Axis1D`、`Axis2D`、`Axis3D`。
路径格式：`<Keyboard>/q`、`<Mouse>/LeftButton`、`<Mouse>/Pos`。

## 激活 Context

在 `game.json` 中声明启动时激活的 context：

```json
{ "startupInputContexts": ["MyMod_Gameplay"] }
```

或在 Trigger 中动态推入：

```csharp
input.PushContext("MyMod_Gameplay");
```

## 在代码中读取

```csharp
bool dodged = input.PressedThisFrame("MyMod_Dodge");
Vector2 aim = input.ReadAction<Vector2>("MyMod_Aim");
```

## 挂靠点

| 基建 | 用途 |
|------|------|
| `InputConfigPipelineLoader` | 从 `Input/default_input.json` 自动合并 |
| `PlayerInputHandler` | 运行时读取动作状态 |
| `ConfigPipeline` | 多 Mod 输入配置合并 |
| `InputOrderMappingSystem` | 如果需要转为 GAS 命令，见 [new_order](new_order.md) |

## 检查清单

*   [ ] `actionId` 带 Mod 前缀避免冲突（如 `MyMod_Dodge`）
*   [ ] Context `id` 带 Mod 前缀
*   [ ] JSON 放在 `assets/Input/default_input.json`，不自建加载器
*   [ ] 如果需要将输入转为 GAS 命令，配合 `input_order_mappings.json`（见 [new_order](new_order.md)）

参考：`mods/CoreInputMod/`、`mods/Navigation2DPlaygroundMod/assets/Input/`
