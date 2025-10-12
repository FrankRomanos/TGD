# TGD CombatV2 · 规则总纲（校准版 V2）& 实施蓝图

> 面向 CodeX 的工程 README。内容：**TurnManagerV2 规则总纲**、**六大动作**、**事件与日志规范**、**Sticky/环境规则**、**实施与验收**。

---

## 目录
- [一、核心时间轴与术语](#一核心时间轴与术语)
- [二、冷却/持续：真相源与显示](#二冷却持续真相源与显示)
- [三、回合结点与严格时序（引擎壳必须遵守）](#三回合结点与严格时序引擎壳必须遵守)
- [四、Sticky / 环境（定死的规则）](#四sticky--环境定死的规则)
- [五、动作位阶与统一流程](#五动作位阶与统一流程)
- [六、事件解耦（强约束）](#六事件解耦强约束)
- [七、统一 Debug 日志规范](#七统一-debug-日志规范)
- [八、实施蓝图（M0→M3）与验收点](#八实施蓝图m0m3与验收点)
- [九、场景与绑定清单](#九场景与绑定清单)
- [十、测试剧本（含期望日志）](#十测试剧本含期望日志)

---

## 一、核心时间轴与术语

- **Turn（回合）**：`6 秒`（固定整数）。  
- **Round（一轮）**：`12 秒 = 我方回合 + 敌方回合`。  
- **单位可行动时间**：`TurnTime(unit) = 6 + Speed`（受 Buff/Debuff 影响；**整数秒**）。

---

## 二、冷却/持续：真相源与显示

- **冷却真相源**：以**秒**计；显示给玩家时用 Turn：`ceil(secondsLeft / 6)`。  
- **DoT/HoT**：以 **Rounds** 计；在**阵营回合开始**时结算（我方→目标为我方；敌方→目标为敌方）。  
- **任意时刻** `secondsLeft ≤ 0` 即视为就绪。

---

## 三、回合结点与严格时序（引擎壳 *必须* 遵守）

> 回合开始阶段**至少停留 1 秒**（统一节拍，便于可视化）。

1) **PlayerPhaseStart**
- 处理**目标为我方**的 DoT/HoT（按施加者速度缩放）。  
- 触发本轮应生效的 FullRound。  
- **刷新窗口**：对**全体单位**执行属性重算（清除 remain=0 的状态并重算 MR 等）。  
- Idle ≥ 1s → 进入 `PlayerTurns`。

2) **PlayerTurns（单位队列，可重排）**
- **StartTurn(unit)**：  
  - `Remain = clamp(TurnTime(unit) - PrepaidTime(unit), 0, TurnTime(unit))`；然后 `PrepaidTime=0`。  
  - 进入动作阶段（预检→瞄准→执行→动画→数值），执行期间锁操作。
- **EndTurn(unit)**：  
  - Buff/Debuff `-1 Turn`（仅该单位自身）。  
  - 冷却 `-6s`。  
  - 能量/职业资源恢复（随 TurnTime 缩放）。  
  - `Remain = 0`，清理转场数据。

3) **EnemyPhaseStart**
- 处理**目标为敌方**的 DoT/HoT。  
- 触发敌方 FullRound。  
- **刷新窗口**（同上）。  
- Idle ≥ 1s → 进入 `EnemyTurns`。

4) **EnemyTurns**
- 敌方 AI 行动；玩家可在反应窗口中使用 Reaction（未来接入：`TimeCost ≤ 敌方动作`；Reaction 不链 Reaction）。

5) 循环回到 1。

**关键提醒**
- **不要**在 `StartTurn` 无条件把 `Remain` 复位为满。必须先扣掉 `PrepaidTime` 后再设定。  
- 未来 Reaction/Sustained 会写 `PrepaidTime`，所以这个顺序至关重要。

---

## 四、Sticky / 环境（**定死的规则**）

- **持久化条件**：仅当 `turns > 0` 的环境效果才写入 `MoveRateStatusRuntime`；  
  `turns == 0` 的加/减速为**瞬时**，只影响**当前步**的 MR，离开格子立刻恢复。
- **扣减时机**：仅在**该单位自己的 EndTurn** 扣 1；不在 PhaseStart/别人的 EndTurn/按 Round 扣。
- **永久学习禁用**：删除/关闭任何“进入减速地形后永久降低 MR”的逻辑。
- **日志**：见“统一 Debug 日志规范”。

---

## 五、动作位阶与统一流程

- **Free**：`TimeCost=0`，任意时刻可用；**不能撤销**上一个 Standard。  
- **Reaction**：我方/敌方回合均可；不链 Reaction；未来限制 `TimeCost ≤ 被响应动作`；  
  - 在**敌方回合**触发：`PrepaidTime += TimeCost`，下一次 `StartTurn` 扣除。  
  - 在**自己回合**触发：消耗 `Remain`；若撤销上一个 Standard：**返时不返资**，并进入冷却。  
- **FullRound**：仅在**回合开始且 `Remain==TurnTime`** 时可用；宣告后**立即 EndTurn**；  
  在未来某次**回合开始阶段**生效（可排程多个）。  
- **Standard / Derived / Sustained**：同原设计（略）。

> **统一动作入口**：预检（冷却/资源/目标）→ 瞄准 → 确认 → **执行** →（可选）动画 → **数值结算**（由 TurnManagerV2 执行）。  
> 工具层仅**报告**：`UsedSeconds / RefundedSeconds / EnergyNet`，不本地扣费（避免双扣）。

---

## 六、事件解耦（**强约束**）

- `HexClickMover`（精准移动）**只**发 `HexMoveEvents.*`。  
- `AttackControllerV2`（靠近/模糊移动+攻击）**只**发 `AttackEventsV2.*`。  
- 拒绝/提示也各走各自事件；**禁止跨通道**转发。

---

## 七、统一 Debug 日志规范

> 一切关键节点**必须可见**，格式统一、便于脚本比对。`T{idx}` 为相位计数（Player/Enemy 每开始一次自增）。

### Phase
```
[Phase] Begin  T{idx}(Player|Enemy)
[Phase] Idle1s T{idx}(Player|Enemy)
```

### Unit Turn
```
[Turn]  Begin  T{idx}({UnitLabel}) TT={turnTime} Prepaid={pre} Remain={remain}
[CD]    Tick   T{idx}({UnitLabel}) -6s  ({n} skills)
[Buff]  Tick   T{idx}({UnitLabel}) -1 turn  (tags:{t1:remain},{t2:remain} / none)
[Res]   Regen  T{idx}({UnitLabel}) +{energy} -> {cur}/{max} (EndTurnRegen)
[Turn]  End    T{idx}({UnitLabel})
```

### Sticky（MoveRateStatusRuntime 打）
```
[Sticky] Apply   U={UnitLabel} tag={tag} mult={m:F2} turns={n}
[Sticky] Refresh U={UnitLabel} tag={tag} mult={m:F2} turns={n}
[Sticky] Tick    U={UnitLabel} tag={tag} -> remain={n-1}
[Sticky] Expire  U={UnitLabel} tag={tag}
```

### 动作摘要（去掉旧 spam）
```
[Move]   Use secs={used} refund={ref} energy={net} U={UnitLabel}
[Attack] Use moveSecs={mUsed} atkSecs={aUsed} energyMove={eM} energyAtk={eA} U={UnitLabel}
```

---

## 八、实施蓝图（M0→M3）与验收点

> 目标：时序壳稳定，移动/普攻统一入口与锁控。Reaction/FullRound 复杂度后续并入。

### M0：TurnManagerV2 壳 + 两相位节拍
**交付**
- `TurnManagerV2`：`PlayerPhaseStart → PlayerTurns → EnemyPhaseStart → EnemyTurns` 循环；每个 PhaseStart Idle ≥ 1s；提供 `OnPlayerPhaseStart/OnEnemyPhaseStart` 钩子。
- `CooldownStoreSecV2`（秒为真相源）：`Start/SecondsLeft/AddSeconds/TickEndOfOwnerTurn(-6s)`；`TurnsLeft = ceil(sec/6)`。
- `CombatActionManagerV2`：统一“预检→瞄准→确认→执行→动画→结算”；Move/Attack 的扣费走 TMV2。

**验收日志**
- `[Phase] Begin/Idle1s` 正确出现；
- EndTurn 打印 `[CD] Tick`、`[Buff] Tick`、`[Res] Regen`；
- 动作后有 `[Move]/[Attack]` 摘要。

### M1：FullRound（最小）
- 仅在“回合开始且时间满”可宣告；宣告即 EndTurn；在对应阵营下一/若干个 PhaseStart 结算。

### M2：DoT/HoT 管线
- `OngoingEffectRuntime`（施加者 + basePer6s + 剩余Rounds）。
- PhaseStart 对目标阵营 Tick；按 `CasterTurnTime/6` 缩放。

### M3：UI/交互一致化
- 预检不通过不进入瞄准；HUD 明示原因；忙碌期彻底锁控。

---

## 九、场景与绑定清单

- 角色：
  - `HexBoardTestDriver`、`HexClickMover`、`AttackControllerV2`
  - `UnitMoveAnimListener`（订 `HexMoveEvents`）、`AttackMoveAnimListener`（订 `AttackEventsV2`）
  - `AttackAnimDriver`（动画事件 Strike/End）
- `CombatActionManagerV2`：接 `TurnManagerV2`，注册工具，统一结算。
- HUD（全局或每人一份，二选一）：
  - 全局：**不**勾 `RequireUnitMatch`，可选过滤 `CurrentUnit`；
  - 每人：HUD 放到角色子物体，勾 `RequireUnitMatch`，`UnitAutoWireV2` 自动注 driver。

---

## 十、测试剧本（含期望日志）

- **T0 外圈节拍**  
  `[Phase] Begin T1(Player)` → `[Phase] Idle1s T1(Player)` → … → 循环稳定。

- **T1 冷却显示**  
  给普攻 `StartCooldownSeconds=5`：释放后 UI 显示 `ceil(5/6)=1 turn`；EndTurn 一次后就绪（秒数 ≤ 0）。

- **T2 能量恢复与速度缩放**  
  `EnergyRegenPer2s = 1`：Speed=0 的 +3；Speed=+6 的 +6（Turn=12）。

- **T3 DoT/HoT**  
  玩家给己方上 `HoT(2 rounds, basePer6s=10)`，给敌方上 `DoT(2 rounds, basePer6s=10)`：  
  各自在**对应 PhaseStart** 触发一次；修改施加者速度后，下次 Tick 量随之变化。

- **T4 Sticky**  
  P1 入 `sticky(4, mult=0.5)` → 仅在 **P1 EndTurn** 扣 1；P2 入 `sticky(2, mult=2)` → 仅在 **P2 EndTurn** 扣 1。  
  非 sticky 地形离格即恢复；日志仅在有持久化时出现 `[Sticky] Apply/Refresh/Tick/Expire`。

---

> 事件通道解耦、执行报告上浮、结算单一来源与严格时序是首要工程约束；其余改动均围绕这四点展开。
