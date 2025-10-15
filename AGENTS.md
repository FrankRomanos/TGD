# TGD CombatV2 · 规则总纲（校准版 V2.4）& 实施蓝图

> 面向 **CodeX** 与团队协作的工程 README。  
> 涵盖：**TurnManagerV2 时间轴与规则**、**六大动作与统一流程（W1→W4→W4.5）**、**事件/结算解耦约束**、**调试日志规范**、**分步实施与验收**、**测试剧本**。  
> 本文是**单一真相源**（source of truth），所有实现均以此为准。

<span style="color:red">⚠️ 任何逻辑都不得从 TestDriver/UnitRef/Map 读取“起点/站位”用作规则判断；起点只读 IActorOccupancyBridge.CurrentAnchor，计划和预览都须配合 AnchorVersion 做版本化失效。所有位移（含强制）必须调用 MoveCommit()，由 Bridge 统一触发 AnchorChanged。</span>

---

## 目录
- [一、核心时间轴与术语](#一核心时间轴与术语)
- [二、冷却/持续单位与结算时点](#二冷却持续单位与结算时点)
- [三、能量与职业资源](#三能量与职业资源)
- [四、严格时序（引擎壳遵守）](#四严格时序引擎壳遵守)
- [五、动作系统统一流程（W1→W4→W4.5）](#五动作系统统一流程w1w4w45)
- [六、连锁弹窗与取消规则（W2 专属）](#六连锁弹窗与取消规则w2-专属)
- [七、环境/陷阱/Sticky 规则](#七环境陷阱sticky-规则)
- [八、事件解耦与结算单一来源](#八事件解耦与结算单一来源)
- [九、日志规范（必须可观测）](#九日志规范必须可观测)
- [十、实施计划（M0→M3+）与验收点](#十实施计划m0m3-与验收点)
- [十一、工程项目注意事项](#十一工程项目注意事项)
- [十二、绑定/场景检查清单](#十二绑定场景检查清单)
- [十三、测试剧本 T0–T7](#十三测试剧本-t0t7)

---

## 一、核心时间轴与术语

- **Turn（回合）**：`6 秒`（固定整数）。  
- **Round（一轮）**：`12 秒 = 我方回合 + 敌方回合`。  
- **单位可行动时间**（在自己**行动阶段**可用）：  
  `TurnTime(unit) = 6 + Speed`（受 Buff/Debuff 影响；**整数秒**）。

> 统一基调：**以秒为运行真相源**，展示层再映射为回合/轮。

---

## 二、冷却/持续单位与结算时点

### 2.1 冷却（技能/动作）
- **内部**按**秒**精确累计（支持 -2s/-3s 等调整）；展示为 `ceil(secondsLeft/6)`。  
- **就绪判定**：`secondsLeft ≤ 0` 即就绪。  
- **唯一递减时点**：**每个回合结束**（我方 EndTurn、敌方 EndTurn）**各 -6s**。  
  > 即一轮（我方+敌方）会发生 **两次** 冷却 -6s。

### 2.2 DoT/HoT（持续伤害/治疗）
- **单位**：`Rounds`（每一轮结算一次）。  
- **结算时点**（**开始阶段**）：  
  - `PlayerPhaseStart`：处理**目标为“玩家阵营单位”**的 DoT/HoT Tick。  
  - `EnemyPhaseStart`：处理**目标为“敌方阵营单位”**的 DoT/HoT Tick。  
- **缩放**：按**施加者当前速度**：  
  `tickAmount = basePer6s * (CasterTurnTime / 6)`（四舍五入规则在实现中固定）。

> 说明：冷却与持续**分别**在「行动结束」与「相位开始」两个不同时点运行，便于平衡与可视化。

---

## 三、能量与职业资源

- **能量恢复（EndTurn 时）**：  
  `EnergyGain = floor( (UnitTurnTime / 2) * EnergyRegenPer2s )`  
  （TurnTime 变长时，恢复自然变多）
- **职业资源**：也在 **EndTurn** 按职业规则结算；是否按**一回合**或**一轮**刷新，留待职业系统落地时定义（此文不强约）。

> 重要：**能量/冷却/回合级 BuffDebuff 的递减均在 EndTurn**（玩家与敌方各一次）。

---

## 四、严格时序（引擎壳遵守）

> 任何“回合开始阶段”**至少停留 1 秒**（统一节拍/便于可视化/测试）。  
> 以下“时点”是**唯一允许**系统性处理的窗口（未来**Reaction/Free**接入也依附其上）。

**① PlayerPhaseStart（我方回合开始）**
- 仅允许 `Free`；玩家暂不可行动。
- 触发 **DoT/HoT**（目标=玩家阵营）。
- 刷新“已到期”的 Buff/Debuff 对**可见属性**（例如 MoveRate）。
- 播报：`[Turn] Tn(P1) Begin`（含回合计数、单位名）。

**② PlayerTurns（我方行动阶段：A B C D，可重排）**
- **Active Turn**（以当前单位为主角）：  
  - 获得 `RemainingTime = TurnTime = 6 + Speed`。  
  - 通过统一入口触发动作：**W1→W4→W4.5**（见下节）。

**③ Player EndTurn（我方回合结束）**
- 结算：`Cooldown -6s`、`Buff/Debuff 持续 -1 turn`、`Energy +X`、清理预支时间等。
- 播报：`[Turn] Tn(P1) End`。

**④ EnemyPhaseStart（敌方回合开始）**
- 同 ①，但目标为敌方阵营：`[Turn] Tn(Enemy) Begin`。

**⑤ EnemyTurns（敌方行动阶段）**
- 敌方 AI 动作；玩家的 **Reaction/Free** 可能在此阶段通过窗口收集。

**⑥ Enemy EndTurn（敌方回合结束）**
- 同 ③：`[Turn] Tn(Enemy) End`。  
- 一轮结束；下一轮 `T(n+1)(P1) Begin`。

> **日志顺序必须完整可见**：`T1(P1) Begin → T1(P1) → T1(P1) End → T1(Enemy) Begin → T1(Enemy) → T1(Enemy) End → T2(P1) Begin → …`

---

## 五、动作系统统一流程（W1→W4→W4.5）

> 所有动作都走**相同流程**（包括无目标技能，统一进入 W1）。  
> 术语：**BaseAction=W2 的原始动作**；W2 连锁可包含 **Reaction / Free**；**Derived 改为 W4.5**。

### W1：瞄准阶段（Aim）
- 入口：按键/UI 打开。  
- **先做 Precheck**（冷却/条件/资源**估算**/回合时间**估算**）。  
- 若通过：显示范围/高亮（无目标也进入本阶段，保证统一）。  
- 允许**右键/ESC**取消（退出 W1，不产生任何开销）。  
- **不触发连锁窗口**。

### W2：确认释放（Confirm）
- **先扣费再开窗（关键约束）**：
  - 立刻扣除：**时间/资源/起始冷却**（进入冷却计时）。
  - Reaction 的连锁合法性：`Reaction.TimeCost ≤ 被响应动作.TimeCost`。
  - 判断“当前剩余可行动时间/资源”是否足够支持**可用连锁**；不足则**不弹**对应连锁项。
- **W2 连锁窗只包含 Reaction / Free**（**不包含 Derived**）。  
- 若无可用连锁且未取消 → 继续到 W3。

### W2.1 / W2.1.1 / …：连锁确认
- 每一层确认**都要先扣费**，然后再进行下一层连锁可用项检测（同 W2 规则）。  
- 当上一层选择的是 Reaction 时，下一层 Reaction 的比较对象为**刚确认的动作**的 `TimeCost`。

### W3：执行阶段（Execute）
- **动作序列已定型**（按照规则顺序执行）：  
  - 敌方回合的 Reaction：**后发先至（LIFO）**；Free 在第二次窗体中按选择序顺位执行。  
- W3 中**不再发生任何连锁/取消**；仅系统推进（路径/动画打点/命中计算）。

### W4：动作结束（Resolve & Cleanup）
- 执行数值结算与收尾：  
  - 更新日志、推进冷却、刷新可能被动效果（如：击中返还、部分技能冷却-秒）。  
  - 同步 UI 状态；**若冷却经过减秒已 ≤0**，应立刻显示已就绪。

### W4.5：派生窗口（Derived after Base Resolve）
- 仅当**前置动作满足下列条件**才出现派生窗：  
  1) `Base.kind == Attack`（当前阶段仅允许从**攻击**派生；移动不触发派生）；  
  2) `Base.execResult.AttackSuccess == true`（**命中/未回卷/未截断**）；  
  3) `Derived.RequiresTag` 与 `Base.ProvidesTag` 匹配（如 `AttackLanded`）。  
- **派生窗与 W2 解耦**：不受 W2 的 Free/Reaction 影响，也不参与后发先至。  
- 交互规则：  
  - 允许 **ESC/右键**取消当前派生窗 → `DerivedPromptAbort(cancel)`，不再出现后续窗，直接返回 Idle；  
  - 选择某个派生后：  
    - **W2’ PreDeduct（Derived）**：基于**W4 结算后的最新预算**再次做可支付/冷却/条件检查并预扣；  
    - **W3’ Execute（Derived）** → **W4’ Resolve（Derived）**；  
    - 顺序保证为：**先前置、后派生**（不是后发先至）。

---

## 六、连锁弹窗与取消规则（W2 专属）

- 本节“连锁窗”指 **W2 / W2.1 / W2.1.1 … 的 Reaction/Free 窗口**。  
- **取消操作**：按 **ESC / 右键** = **取消当前连锁弹窗**。  
- **取消效果**：
  1) **不撤销**已确认的动作；  
  2) **不再出现**任何后续连锁窗；  
  3) **直接进入 W3**，仅执行**已确认且已扣费**的动作序列；  
  4) **不产生 LinkCancelled**（UI 取消≠技能被 Reaction 取消）；  
  5) **不退还**任何已扣的时间/资源/冷却。

---

## 七、环境/陷阱/Sticky 规则

### 7.1 术语
- **Environment Multiplier**：纯粹的地形倍率（如 x0.5 / x2），**不带持续**。  
- **Sticky**：带持续回合的移动速率修饰（进入/站在上面会得到 n 回合的 MR Buff/Debuff）。

### 7.2 应用与刷新
- **Zero-turn**（持续=0）的环境：**忽略 Sticky**（只作为即时倍率使用，不入库）。
- **进入格子**：若该格有 Sticky（`turns > 0`），则**应用或刷新**（同一 tag 独占；不叠数值，仅刷新持续）。
- **站在原地**：在每个 **PhaseStart**（玩家或敌方回合开始）**刷新一次**该格的 Sticky（如果仍在格内）。
- **移动穿过**：每经过一格即触发一次“on-move”效果（伤害/治疗/提示等）。

### 7.3 结束时点与属性刷新
- **EndTurn**：仅递减 Sticky 持续 `-1 turn`（与冷却一致，双方回合各一次）。  
- **下一个 PhaseStart**：若 Sticky 已到 `0`，**此刻刷新可见属性**（例如 MoveRate 恢复为无 Sticky 时的值），并打印变更日志。

### 7.4 调试要点
- 必须打印：`[Sticky] Apply/Refresh/Tick/Expire`。  
- 在 PhaseStart 打印**属性刷新**（例如：`[Stats] MoveRate: 2 → 4 (reason=StickyExpired)`）。

---

## 八、事件解耦与结算单一来源

### 8.1 事件通道（低耦合）
- **HexClickMover（精准移动）**：只发 `HexMoveEvents.*`。  
- **AttackControllerV2（靠近/模糊移动+攻击）**：只发 `AttackEventsV2.*`。  
> 禁止在攻击位移阶段混用 `HexMoveEvents`，拒绝/提示也各走各自事件。

### 8.2 结算来源（单一化）
- 使用 `TurnManagerV2` 的场景：  
  - 将 `HexClickMover.simulateTurnTime = false`、`AttackControllerV2.simulateTurnTime = false`。  
  - **所有**时间/能量/冷却结算，统一由 `CombatActionManagerV2 → TurnManagerV2` 执行；  
  - 工具内 `Spend/Refund` 仅在 **无 TM 的调试模式** 才启用，避免**双重扣费**。

### 8.3 执行报告接口（IActionExecReportV2）
- 工具执行后通过 `UsedSeconds / RefundedSeconds / Consume()` 报告本次**真实使用/返还**秒数。  
- 对 Attack 建议**拆分记录**（移动/攻击各自秒数与能量），CAM 识别 AttackControllerV2 时**分别结算**。

---

## 九、日志规范（必须可观测）

### 9.1 Turn / Phase 日志（严格顺序）
- `"[Turn] T{n}(P1) Begin"` / `"[Turn] T{n}(P1)"` / `"[Turn] T{n}(P1) End"`  
- `"[Turn] T{n}(Enemy) Begin"` / `"[Turn] T{n}(Enemy)"` / `"[Turn] T{n}(Enemy) End"`

### 9.2 EndTurn 日志
- `"[CD] Tick -6s (skills:...)"`  
- `"[Buff] Tick -1 turn (tags:...)"`  
- `"[Res] Regen T{n}(Unit) +X (EndTurnRegen)"`

### 9.3 PhaseStart 刷新与持续日志
- `"[DOT] Tick {amount} (target=.., caster=.., speed=..)"`  
- `"[Sticky] Apply/Refresh/Tick/Expire (tag=.., left=..)"`  
- `"[Buff] Refresh U=.. mr:4.00 -> 2.12 (recomputed tags:..)"`（或其它可见属性）

### 9.4 动作日志（W1→W4→W4.5）
- `"[Action] <unit> [<kind>] W1_AimBegin/Cancel"`  
- `"[Action] ... W2_PrecheckOk"` / `W2_PreDeductCheckOk/Fail(reason=...)`  
- `"[Action] ... W2_ChainPromptOpen(count=K)"` / `W2_ChainPromptAbort(auto-skip|cancel)`  
- `"[Action] ... W3_ExecuteBegin(budgetBefore=?, energyBefore=?)"` / `W3_ExecuteEnd`  
- `"[Action] ... W4_ResolveBegin(used=?, refunded=?, net=?, energyMove=?, energyAtk=? [FreeMove]?)"`  
- `"[Action] ... W4_ResolveEnd(budgetAfter=?, energyAfter=?)"`  
- **派生**（W4.5）：  
  - `[Chain] DerivedPromptOpen(from=Attack, count=K, baseSuccess=true)` / `DerivedPromptAbort(cancel)`  
  - `[Derived] Select(id=..., kind=Derived)`  
  - `[Gate] W2' PreDeduct planSecs=?, planEnergy=? before=Time:?/Energy:?`  
  - `[Action] ... (Derived) W3_ExecuteBegin/End → W4_ResolveBegin/End`

### 9.5 字段释义
- **used / refunded / net**：来自工具的真实执行报告（秒）；`net = max(0, used - refunded)`。  
- **budgetBefore / budgetAfter**：回合时间（秒）的**剩余可用**值（由 TMV2 维护）。  
- **energyBefore / energyAfter**：能量池在动作前/后的值。  
- **energyMove / energyAtk**：Attack 工具拆分的两段能量净值；Move 工具仅有 `energy`.  
- **[FreeMove]**：以敌人为目标时，若 `moveSecs < 0.2s`，在 W4 以 “FreeMove” 标记返还 1s 与对应能量。  
- **count=K**：可用的连锁项数量（过滤掉冷却中/条件不满足/预算不足的项后）。

---

## 十、实施计划（M0→M3+）与验收点

### **M0：TurnManagerV2 壳 + 两相位节拍（已上线/优化中）**
- 状态机：`PlayerPhaseStart → PlayerTurns → PlayerEnd → EnemyPhaseStart → EnemyTurns → EnemyEnd → 循环`  
- **每个 PhaseStart ≥1s**。  
- 统一入口（CAM）驱动 Move/Attack，工具通过 `IActionExecReportV2` 上浮执行报告。  
- **验收**：完整 Turn/End 日志、双侧 EndTurn 冷却/能量/持续递减，PhaseStart 属性刷新。

### **M1：FullRound（最小集合） + W1→W4 骨架 + 连锁一跳**
- 仅在“回合开始且时间满”可宣告；宣告即 EndTurn。  
- 在对应的未来 `PhaseStart` 自动结算。  
- CAM 统一 W1→W4 日志、W2 预算预扣与一跳 Reaction/Free 连锁。  
- **验收**：延时 1 回合的 FullRound 正确触发；W2 先扣费再开窗；取消行为符合日志。

### **M2：DoT/HoT 管线**
- `OngoingEffectRuntime`：记录“施加者引用 + basePer6s + 剩余Rounds”。  
- `ApplyTick(casterTurnTime)`：按缩放公式计算并记账。  
- `PhaseStart` 钩子按目标阵营调用 `Tick`。  
- **验收**：放一个 2 Rounds 的 DoT → 连续两个对应开始阶段各触发 1 次；修改施加者速度影响下次 Tick 量。

### **M3：动作系统连锁（UI/逻辑一致化） + 派生后置化（W4.5）**
- 统一连锁窗（W2/W2.1/...）：**先扣费，再开窗**；Reaction 需 `TimeCost ≤ 被响应动作`。  
- **Derived** 改为 **W4.5**（仅在攻击命中后弹出；按前置→派生顺序执行）。  
- **验收**：构造三类失败场景（冷却未就绪/能量不足/目标非法）→ 均被 HUD 阻止；派生窗只在命中后弹出，取消/执行日志吻合。

> **M4+（后续）**：Sustained 窗口、Boss 专属机制、职业资源精细化等。

---

## 十一、工程项目注意事项

1) **解耦**：事件通道、结算来源、动画监听**一器一责**。  
2) **工具内扣费**：仅在 **simulate mode** 下启用；默认由 TMV2 结算。  
3) **UnitAutoWireV2**：现阶段是**临时装配工具**（接线 driver/ctx/turnManager）；未来将被角色构建管线替代。  
4) **公共常量**：`TurnSeconds=6` 只在单点定义；其它系统读取常量而非魔数。  
5) **避免静态全局状态**：Turn/Phase 序号由 TMV2 产出，外部仅读。

---

## 十二、绑定/场景检查清单

- 角色（玩家/敌人）
  - `HexBoardTestDriver`（UnitRef/Layout/Map/View）
  - `HexClickMover`（精准移动）→ 只发 `HexMoveEvents`
  - `AttackControllerV2`（靠近/模糊移动+攻击）→ 只发 `AttackEventsV2`
  - `UnitMoveAnimListener`（订阅 HexMoveEvents）
  - `AttackMoveAnimListener`（订阅 AttackEventsV2）
  - `AttackAnimDriver`（攻击动画事件 Strike/End）
  - `MoveRateStatusRuntime`（挂 TurnManager/Driver，收 Sticky）
- `CombatActionManagerV2`：绑定 `TurnManagerV2`，注册工具列表。  
- HUD：
  - Move HUD ← `HexMoveEvents.MoveRejected/TimeRefunded/...`（按 UnitRef 过滤）
  - Attack HUD ← `AttackEventsV2.AttackRejected/AttackMiss/...`（按 UnitRef 过滤）

---

## 十三、测试剧本 T0–T7

- **T0 外圈节拍**：空场景仅挂 TMV2 与计时 UI → 验证完整日志序列与 1s PhaseStart。  
- **T1 冷却/显示差异**：给普攻设 `StartCooldownSeconds=5` → UI 显示 1 turn；EndTurn 一次后就绪。  
- **T2 能量恢复与速度缩放**：设 `EnergyRegenPer2s=1`；Speed=0 单位 EndTurn +3；Speed=+6 单位 EndTurn +6。  
- **T3 DoT/HoT 阵营与缩放**：己方 HoT(2R)、敌方 DoT(2R) → 各在对应 PhaseStart Tick 两次；修改施加者速度影响下次 Tick。  
- **T4 Sticky 刷新**：在加速/减速格静止与移动 → 验证 Apply/Refresh/Tick/Expire + PhaseStart 属性刷新。  
- **T5 连锁取消**：W2 弹窗按 ESC/右键取消 → 仅执行已确认动作；无后续窗；日志吻合。  
- **T6 解耦回归**：模糊移动→精准移动路径可达，A 点不被错误标记为障碍；两通道事件互不干扰。  
- **T7 派生后置触发**：普攻命中 → `DerivedPromptOpen`；回卷/未命中 → 不出现。派生成本与预算以 W4 结算后的数值为准，日志顺序**先前置后派生**。

---

> **本 README 版本：V2.4（严格时点 + 派生后置版）**  
> 任何实现与此文不符均视为 Bug；修改规则前务必在此文先行更新。
