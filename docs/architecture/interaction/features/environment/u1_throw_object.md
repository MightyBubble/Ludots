# U1: Throw Object

## 机制描述
拾取环境中的可投掷物体（石头、桶等），然后投掷向目标方向造成伤害。

## 交互层设计
- **Input**: Down
- **Selection**: None / Direction
- **Resolution**: ContextScored（自动检测附近可投掷物）

## 实现要点
```
ContextGroup:
  candidate: "ThrowObject"
    precondition: env entity HasTag("throwable") in radius 200cm
    score: 100

Phase Graph:
  1. SelectTarget: nearest throwable entity
  2. AttachToActor(throwable, caster)  // 拾取
  3. InputGate: 等待玩家选择投掷方向
  4. LaunchProjectile(throwable, direction, speed=1000cm/s)
  5. OnProjectileHit:
       ApplyEffect(target, damage)
       DestroyEntity(throwable)
```
- 可投掷物 entity 有 Tag("throwable") + Projectile 组件
- 拾取后物体跟随玩家，等待投掷输入

## 新增需求
| 需求 | 优先级 | 说明 |
|------|--------|------|
| Environment tag scanning | P1 | 扫描附近可投掷物体 |
| AttachToActor | P2 | 物体附着到玩家身上 |

## 参考案例
- **Zelda BotW**: 拾取投掷物体
- **Half-Life 2**: 重力枪抓取投掷
- **Dark Souls**: 投掷飞刀/炸弹
