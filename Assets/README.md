# TGD CombatV2 · 规则总纲（校准版 V2）& 实施蓝图

> 面向 CodeX 的工程 README。涵盖：**TurnManagerV2 规则总纲**、**六大动作的核心设定**、**工程项目注意事项**、**分步实施与验收**、**测试剧本**。

---

## 目录
- [一、核心时间轴与术语](#一核心时间轴与术语)
- [二、冷却/持续的单位与结算](#二冷却持续的单位与结算)
- [三、回合能量/资源恢复](#三回合能量资源恢复)
- [四、动作位阶与统一流程](#四动作位阶与统一流程)
- [五、严格时序（引擎壳遵守）](#五严格时序引擎壳遵守)
- [六、显示 vs 运行时的“二层制”](#六显示-vs-运行时的二层制)
- [七、实施蓝图（M0→M3）与验收点](#七实施蓝图m0m3与验收点)
- [八、六大动作（绝对版规则）](#八六大动作绝对版规则)
- [九、工程项目注意事项（重要）](#九工程项目注意事项重要)
- [十、绑定/场景检查清单](#十绑定场景检查清单)
- [十一、测试剧本 T0–T4](#十一测试剧本-t0t4)

---

## 一、核心时间轴与术语

- **Turn（回合）**：`6 秒`（固定整数）。  
- **Round（一轮）**：`12 秒 = 我方回合 + 敌方回合`。  
- **单位可行动时间**：  
  `TurnTime(unit) = 6 + Speed`（受 Buff/Debuff 影响；**整数秒**）。

---

## 二、冷却/持续的单位与结算

### 冷却（技能/动作）
- **内部**按**秒**精确累计（支持 -2s/-3s 调整）。
- **显示**给玩家时：`ceil(secondsLeft / 6)`（以 Turn 为单位显示）。
- 任意时刻 `secondsLeft ≤ 0` 即视为就绪。
- **递减时机**：双方**各自回合结束**时，对该单位**所有冷却 -1 Turn（-6s）**。

### DoT/HoT（持续伤害/治疗）
- **单位**：`Rounds`（每 `12s` 结算一次，更直观）。
- **结算时机**：  
  - 我方回合开始 → 处理**目标为我方**的 DoT/HoT。  
  - 敌方回合开始 → 处理**目标为敌方**的 DoT/HoT。
- **量的缩放**（按**施加者当前速度**）：  
  `tickAmount = basePer6s * (CasterTurnTime / 6)`  
  （取整/四舍五入规则在实现中固定）。

---

## 三、回合能量/资源恢复

- **能量恢复（EndTurn 时）**：  
  `EnergyGain = floor( (UnitTurnTime / 2) * EnergyRegenPer2s )`  
  > TurnTime 变长时，恢复自然变多。
- **职业资源**：同样在 EndTurn，按职业各自规则。

---

## 四、动作位阶与统一流程

- **Free（自由）**：`TimeCost = 0`，任意时刻允许；**不能**用于撤销 Standard。  
- **Reaction（反应）**：我方/敌方回合均可，需满足触发条件；不链 Reaction；后续将加入“`TimeCost ≤ 被响应动作`”限制与窗口收集。  
- **FullRound（整轮）**：仅当**回合刚开始且时间满**可宣告；宣告后**立即结束本回合**，并在设定的一轮/若干轮后的**回合开始阶段**生效结算。

> **统一动作入口与三段式**（现网已用于移动/普攻）：  
> **Precheck → Aim（瞄准）→ Confirm → 执行阶段 →（可选）动画阶段 → 数值结算阶段**  
> 执行阶段开始后**锁操作**；数值与动画可解耦触发（利于测试）。

---

## 五、严格时序（引擎壳遵守）

> 备注：即使没有可处理的事件，任何“回合开始阶段”**至少停留 1 秒**（统一节拍/便于可视化）。

**① 我方回合开始阶段（玩家暂不可行动，仅允许 Free）**
- 处理**目标为我方**的 DoT/HoT（按施加者速度缩放）。
- 触发本轮应生效的 FullRound（我方宣告的到点结算）。
- **至少停留 1s**。
- 进入**我方行动阶段**。

**② 我方行动阶段（队列 A B C D，可拖拽重排）**
- 初始顺序 `A B C D`；尚未行动者可拖拽自己到任意队友之后。
  - 例：A 拖到 C 后 → 立即开始 B 的回合，队列变为 `B C A D`。
- **单位回合（Active Turn）**：
  - 获得 `TurnTime = 6 + Speed (+/− 状态修正)`。
  - **动作入口统一**：按键/UI → 预检查（冷却/Cost/目标/条件）→ 通过才进入瞄准；不通过 HUD 明示。
  - **确认后**：锁操作直到执行阶段完成（未来连锁/反应也遵循锁控）。
  - **阶段顺序**：执行 →（可选）动画 → 数值。
- **EndTurn（点击结束回合瞬间）**：
  - Buff/Debuff 持续回合 `-1`（按 Turn）。
  - 冷却 `-1 Turn（-6s）`。
  - 能量/职业资源恢复（见上）。
  - 清理预支时间/整轮排程等转场数据。

**③ 敌方回合开始阶段（玩家暂不可行动，仅允许 Free）**
- 处理**目标为敌方**的 DoT/HoT（缩放同上）。
- 触发敌方 FullRound。
- **至少停留 1s**。

**④ 敌方行动阶段**
- 敌方按 AI 动作。
- 玩家可在敌方动作的反应窗口中使用 Reaction（后续接入：需 `TimeCost ≤ 敌方动作`；Reaction 不链 Reaction）。

**⑤ 敌方 EndTurn**
- 敌方身上 Buff/Debuff `-1 Turn`。
- 敌方技能冷却 `-1 Turn（-6s）`。
- 敌方能量/资源恢复按其规则。

**回到 ①**，形成 `12s` 的循环。

---

## 六、显示 vs 运行时的“二层制”

- **运行时**：冷却/持续均以**秒**精确累计。  
- **展示层**（UI）：  
  - 冷却显示 `ceil(secondsLeft/6)`（Turn）。  
  - DoT/HoT 显示”剩余 Rounds“。  
  - `secondsLeft ≤ 0` 即标为**就绪**。

---

## 七、实施蓝图（M0→M3）与验收点

> 目标：先把**严格时序的回合壳**做起来，移动/普攻沿用现有 Standard 实现，但统一入口与锁控框架。Reaction/FullRound 的复杂度**分阶段并入**。

### 迭代 M0：TurnManagerV2 壳 + 两相位节拍
**交付**
- `TurnManagerV2`（场景服务）：
  - 状态机：`PlayerPhaseStart → PlayerTurns → EnemyPhaseStart → EnemyTurns → 循环`
  - 回合开始阶段**至少等待 1s**
  - 钩子：`OnPlayerPhaseStart / OnEnemyPhaseStart` → 触发 DoT/HoT 与 FullRound 队列
- `CooldownStoreSecV2`（新，**秒**为真相源）：
  - `Start(id, seconds)、TickEndOfOwnerTurn(6)、AddSeconds(id, delta)、SecondsLeft(id)`
  - `TurnsLeft(id) = ceil(sec/6)`
- `CombatActionManagerV2`（接口化）：
  - 统一“按键/UI→Precheck→Aim→Confirm→锁控执行→（可选）动画→数值结算”
  - Move/Attack 的预检查与结算走 `TurnManagerV2` 的预算/能量/冷却接口

**验收日志**
- 有序打印：`[Turn] PlayerPhaseStart` / `EnemyPhaseStart` 各**≥1s**  
- EndTurn 时出现：`[CD] Tick -6s`、`[Buff] -1 turn`、`[Resource] +X`  
- 动作确认后：`[Action] Precheck OK → LockInput → Execute → (Anim) → Resolve`

### 迭代 M1：FullRound（最小）
**交付**
- `FullRoundQueue`：仅能在“回合开始且时间满”宣告；宣告即 EndTurn；按 `Delay` 在对应阵营的**回合开始阶段**结算。
**验收**
- 我方宣告 `Delay=1` → **下次我方回合开始**自动结算；敌方同理。

### 迭代 M2：DoT/HoT 管线
**交付**
- `OngoingEffectRuntime`：记录“施加者引用 + basePer6s + 剩余Rounds”
- `ApplyTick(casterTurnTime)`：按缩放公式计算并记账
- `PhaseStart` 钩子按目标阵营调用 `Tick`
**验收**
- 放一个 `2 Rounds` 的 DoT → 连续两个对应**开始阶段**各触发 1 次；修改施加者速度，会影响下次 Tick 量。

### 迭代 M3：UI/交互一致化
**交付**
- `CombatActionManagerV2`：未通过检查**不进入瞄准**；HUD 明确原因  
- **忙碌期**（执行/动画/结算）**彻底锁控**，直到完成
**验收**
- 制造三种失败场景（冷却未就绪/能量不足/目标非法）→ 均被 HUD 阻止并提示

> **M4+**：Reaction 的窗口收集与顺序结算、撤销返时不返资源等再并入。

---

## 八、六大动作（绝对版规则）

> 每个动作具备：`ActionType（Standard/Reaction/Free/Derived/FullRound/Sustained）`、`TimeCostSeconds`、`ResourceCost`、`CooldownSeconds`（输入），运行时有 `CooldownRounds`；可选 `CanBeCancelledByReaction`。

1) **Standard（标准）**  
- 仅在**自己回合**执行；需 `RemainingTime ≥ TimeCost`。  
- 流程：**Intent（预占时间）→ Reaction 窗口 → Commit（结算）**。  
- 若被**自己回合内链式 Reaction 撤销**：**返还时间**（回合时间），**不返资源**；仍进入冷却。  
- 冷却入库：`CooldownRounds = ceil(CooldownSeconds/6)`，在**EndTurn**时递减。

2) **Reaction（反应）**  
- 我方/敌方回合皆可；必须满足技能/目标/资源/冷却条件。  
- **限制**：`TimeCost ≤ 被响应动作的 TimeCost`（Free 例外）。  
- 在**敌方回合**触发：`actor.PrepaidTime += TimeCost`，下次 StartTurn 从 TurnTime 中扣除。  
- 在**自己回合**触发：消耗当前 `RemainingTime`；若用于**撤销上一个 Standard**，按“返时不返资且入冷却”。  
- **不链 Reaction**（LIFO 结算；与 Free 的收集/连锁见统一流程）。

3) **Free（自由）**  
- `TimeCost = 0`；任意时刻允许；**不能撤销**上一个 Standard。  
- Reaction 窗口中始终可选（可与 Reaction 连锁）。

4) **Derived（派生）**  
- 仅能在**指定前置技能之后**立即使用；若前置后插入了 Reaction/Free，则**派生失效**。  
- 其余消耗/冷却按标准规则。

5) **FullRound（整轮）**  
- 仅在**回合开始且 `RemainingTime == TurnTime`** 时可用。  
- 使用后**立即结束本回合**，并将技能排程到未来某次**回合开始阶段**（`DelayRounds = n`）。  
- 使用后进入冷却（照常在所属单位的 EndTurn 递减）。  
- 允许同一单位**排队多个** FullRound（用 `executeAtCycle` 标记）。

6) **Sustained（持续，Boss 专属）**  
- 创建 `SustainedWindow(DurationSeconds)`：  
  - 给每位玩家一个**特殊响应回合**（顺序或并行皆可），每人拥有 `windowTime = DurationSeconds`。  
  - 玩家在窗口消耗的时间 `spentInWindow` 将在其**下一正常回合**作为 `PrepaidTime`（下回合可用时间减少）。  
  - 全员完成后，Boss 的持续效果此刻结算（可被特定技能打断）。

---

## 九、工程项目注意事项（重要）

### 1) 事件通道**低耦合**（关键约束）
- **HexClickMover（精准移动）**：**只**发 `HexMoveEvents.*`
- **AttackControllerV2（模糊移动/靠近+攻击）**：**只**发 `AttackEventsV2.*`  
  > 禁止在攻击位移阶段混用 `HexMoveEvents`。拒绝/提示也各走各自事件（不跨通道转发）。

### 2) 动画监听器**一器一责**
- `UnitMoveAnimListener`：仅订阅 `HexMoveEvents.MoveStarted/Finished` → 驱动**精准移动**的跑步/RootMotion。  
- `AttackMoveAnimListener`：仅订阅 `AttackEventsV2.AttackMoveStarted/Finished` → 驱动**模糊移动**的跑步/RootMotion。  
- 二者都按 **UnitRef（HexBoardTestDriver）** 过滤。

### 3) 结算来源**单一化**
- 若场景使用 `TurnManagerV2`：  
  - 将 `HexClickMover.simulateTurnTime = false`、`AttackControllerV2.simulateTurnTime = false`；  
  - **所有**时间/能量/冷却结算，统一由 `CombatActionManagerV2 → TurnManagerV2` 执行；  
  - 工具内的 `Spend/Refund` 仅在 **no-TM 调试模式** 才启用，避免**双重扣费**。

### 4) 执行报告接口（IActionExecReportV2）
- 工具执行后通过 `UsedSeconds / RefundedSeconds / Consume()` 报告本次**真实使用/返还的秒数**。  
- 对 Attack 的实现建议**拆分字段**（移动/攻击各自秒数），CAM 识别 AttackControllerV2 时**分别结算**（移动不受连击倍率、攻击受影响）。

### 5) 命名/目录建议
```
TGD.CombatV2/
  Turn/
    TurnManagerV2.cs
    Cooldowns/CooldownStoreSecV2.cs
    Ongoing/OngoingEffectRuntime.cs
    FullRound/FullRoundQueue.cs
  Actions/
    Interfaces/IActionToolV2.cs
    Interfaces/IActionExecReportV2.cs
    Standard/HexClickMover.cs
    Standard/AttackControllerV2.cs
    Manager/CombatActionManagerV2.cs
  Events/
    HexMoveEvents.cs
    AttackEventsV2.cs
  View/
    Anim/UnitMoveAnimListener.cs
    Anim/AttackMoveAnimListener.cs
    Anim/AttackAnimDriver.cs
  HexBoard/
    HexBoardTestDriver.cs
```

### 6) 输入/锁控
- **忙碌期**：CAM 进入 `Busy`，屏蔽其它 Aim/Confirm；完成后恢复 `Idle`。  
- **瞄准失败**不得进入 Confirm；HUD 必须提示失败原因。

### 7) 资源/冷却一致性
- 资源不足/冷却未就绪：**只在 Precheck 拒绝**；执行阶段不应再二次扣费失败。  
- 冷却与持续都以**秒**为真相源；展示时做 Turn/Round 转换。

### 8) 日志规范（Debug）
- 统一标签：`[Turn]`、`[Action]`、`[CD]`、`[Buff]`、`[DOT]`、`[HUD]`、`[Attack]`、`[Move]`。  
- 关键状态转移、扣费/退款、回卷、动画触发点**必须日志可见**。

### 9) 与 HexBoard 的桥接
- **HexBoardTestDriver** 是最小运行时上下文：聚合 Unit/Layout/Map/View。  
- **禁止**在 `TGD.HexBoard.Unit` 上直接挂载战斗组件；战斗组件挂在**角色实体**上，通过 `driver.UnitRef` 关联。

---

## 十、绑定/场景检查清单

- 角色（玩家/敌人）：
  - `HexBoardTestDriver`（UnitRef、Layout、Map、View）
  - `HexClickMover`（精准移动）
  - `AttackControllerV2`（模糊移动/靠近+攻击）
  - `UnitMoveAnimListener`（订阅 `HexMoveEvents`）
  - `AttackMoveAnimListener`（订阅 `AttackEventsV2`）
  - `AttackAnimDriver`（攻击动画剪辑事件 → Strike/End）
- `CombatActionManagerV2`：
  - 绑定 `TurnManagerV2`
  - 注册 `tools = [HexClickMover, AttackControllerV2]`
- **HUD**：
  - Move HUD ← `HexMoveEvents` 的 `MoveRejected/TimeRefunded`
  - Attack HUD ← `AttackEventsV2` 的 `AttackRejected/AttackMiss`
- **全局**：
  - 冷却/持续使用 `CooldownStoreSecV2`（秒）；UI 显示用 Turn/Round。

---

## 十一、测试剧本 T0–T4

**T0 外圈节拍**  
空场景仅挂 TMV2 与相位计时 UI：  
日志顺序稳定循环：  
`[Turn] PlayerPhaseStart → [Turn] PlayerTurns Begin/End → [Turn] EnemyPhaseStart → [Turn] EnemyTurns Begin/End → …`  
且每个 PhaseStart **≥1s**。

**T1 冷却/显示差异**  
给普攻临时设 `StartCooldownSeconds = 5`：  
释放后 UI 显示 `ceil(5/6)=1 turn`；EndTurn 一次后立刻就绪（`seconds ≤ 0`）。

**T2 能量恢复与速度缩放**  
设 `EnergyRegenPer2s = 1`：  
- 单位 A：Speed=0 → EndTurn +3  
- 单位 B：Speed=+6（Turn=12s）→ EndTurn +6

**T3 DoT/HoT 阵营与缩放**  
玩家给己方上 `HoT(2 rounds, basePer6s=10)`，给敌方上 `DoT(2 rounds, basePer6s=10)`：  
- 我方/敌方各自**回合开始**各触发一次；修改施加者速度后，下次 Tick 量随之变化。

**T4 整轮技能**  
我方宣告 `FullRound(Delay=1)` → 立即结束自身回合；  
**下一次我方回合开始阶段**自动结算；期间玩家不可行动（仅 Free）。

---

> 以上为 CodeX 的执行基线。**事件通道解耦**、**执行报告上浮**、**结算单一来源**与**严格时序**是首要工程约束；其余改动均围绕这四点展开。
