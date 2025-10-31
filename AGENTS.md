TGD CombatV2 · 规则总纲（V2.4 校准版）

（面向 CodeX 的唯一真相源 · 请严格按本文实现和修改）

0. 开发模式宣言（必须读）
忽略Legacy文件夹（里面的内容全部废弃，不要引用任何文件夹相关的内容）
TurnManagerV2（TMV2）
负责整场战斗的回合循环、行动顺序、双方回合开始/结束、剩余时间预算、冷却/能量/持续效果递减等。它是“战斗回合大脑”。

CombatActionManagerV2（CAMV2）
负责驱动本回合里实际执行的动作：移动、攻击、连锁操作、派生操作。它决定何时进入 W1/W2/W3/W4/W4.5，并把真实耗时/能量结算回 TMV2。

UI（HUD、时间轴、连锁弹窗、回合banner 等）
只能通过 BattleUIService 获取状态和回调。UI 控制器本身不直接操作 TMV2 / CAMV2，不乱扣资源，不乱改回合状态。

代码必须围绕这三块：TMV2 是回合权威，CAMV2 是行动权威，BattleUIService 是 UI 权威。
谁越权，谁错。

1. 回合 / 相位 / 秒制预算：时间轴的根

Turn（回合）：基础为 6 秒。

单位的本回合可用时间：
TurnTime = 6 + Speed（整数秒，受 Buff / Debuff 影响）。

Round（一轮） = 我方回合 + 敌方回合，12 秒左右的循环节拍。

行动阶段结构（严格顺序）：

PlayerPhaseStart（我方回合开始）

PlayerTurns（我方单位依次行动，可能重排/插队）

PlayerEndTurn（我方回合结束）

EnemyPhaseStart（敌方回合开始）

EnemyTurns（敌方单位行动，含我方可触发的 Reaction/Free）

EnemyEndTurn（敌方回合结束）

回到下一轮 PlayerPhaseStart…

PhaseStart 必须至少停 1 秒，不是立即跳过。
这是我们所有 UI / 节奏 / Banner 的锚点。

TMV2 是剩余时间权威：
角色进入自己行动时拿到 RemainingTime = TurnTime，然后 CAMV2 执行动作（移动 / 攻击 / 连锁等）时真实花掉的秒数，都会通过报告接口（IActionExecReportV2）结算回 TMV2。UI 不做小算盘。

CAMV2 不直接改冷却/能量/持续状态，它上报耗时和能量，TMV2 负责最终账本。这是为了防止“双重扣费”。

2. 冷却 / 持续性效果 / 资源恢复
冷却（技能 CD）

内部按秒存，展示按“回合数”是 ceil(secondsLeft / 6)。

技能一旦在 W2 确认释放，就立刻进入冷却计时（不等到命中）。

全局结算点：

EndTurn（每个阵营结束时）：把当前阵营所有技能冷却 -6s。

所以一轮里冷却会被扣两次（玩家回合末、敌方回合末）。

DoT / HoT（持续伤害、持续治疗）

存的是“还剩几轮”，不是几秒。

Tick 的时点是 PhaseStart：

PlayerPhaseStart：对我方阵营目标结算他们身上的效果

EnemyPhaseStart：对敌方阵营目标结算

每次 Tick 的数值 = BasePer6s * (施加者当时的 TurnTime / 6)，四舍五入在实现里固定。

Sticky / 地形减速加速

踩到“Sticky”地形就给你挂一个移动速率状态（有持续回合数）。

这个状态会在每个 EndTurn 递减持续回合数。

当持续到 0，在下一个 PhaseStart 才会刷新角色的最终可见移动速率回到正常值，并且打印日志。

回合结束时（EndTurn）统一处理的东西：

技能冷却 -6s

Buff/Debuff 持续 -1

能量恢复（按角色 TurnTime 和能量恢复率算）

职业资源的回合刷新

返还/扣除那些“连击计数”“预支秒数”的状态

打印日志

UI 不参与这些结算；TMV2 参与；CAMV2 只是汇报消耗；HUD 只读结果。

3. 动作统一流程：W1 → W2 →（W2.1…）→ W3 → W4 → W4.5

所有动作都走同一套管线，包括普通攻击、移动等等。

W1 瞄准 (Aim)

玩家开始准备一个动作。

做预检（冷却OK？距离OK？目标合法？预计要花几秒/能量够不够？）

高亮范围，允许取消。

不弹连锁窗。

W2 确认 (Confirm)

玩家按下确认后，立刻扣费（时间/能量/技能CD开始算）。

决定有没有连锁弹窗（Reaction / Free 等）。

Reaction 必须满足：Reaction 的 TimeCost ≤ 被响应动作的 TimeCost。

不能 afford 的选项不能出现在弹窗里。

如果有更多可叠加的选择，就进入 W2.1 / W2.1.1…（一层层继续确认，每层同样“先扣费再问”）。

取消连锁弹窗（按 ESC / 右键）：

不会回滚已经确认的动作

只是停止继续叠加新动作

然后直接进入 W3 执行

W3 执行 (Execute)

执行动画 / 跑位 / 命中判定 / 伤害计算。

这个阶段不再进行连锁 UI 或扣费判断。

W4 收尾 (Resolve)

结算能量、返还“没走满的移动秒数”、记录击中/未命中状态等等。

打日志，让 UI/HUD 刷新。

W4.5 派生 (Derived After Resolve)

只在攻击成功命中之后，可能弹出一个“后置派生窗”。

这个派生窗和 W2 的连锁窗是完全不同的概念，属于“攻击后的奖励/追加技”。

如果玩家选某个派生：

会再做一次 W2 风格的“先扣费再确认”

然后执行它自己的 W3’ / W4’。

如果玩家取消，派生窗关掉，回合继续，没回滚。

重点：派生永远在 W4 之后（W4.5），不能和 W2 混在一起。

4. 日志要求（我们以后拿日志喂UI，不要乱改格式）

TMV2 / CAMV2 / 行为执行层必须打印以下标准日志。
UI 依赖它们来显示 Banner 等（后面会详细说）。

最低要求日志：

回合节拍：

[Turn] T1(Player) Begin

[Turn] T1(Player) Idle

[Turn] T1(Player) End

[Turn] T1(Enemy) Begin

[Turn] T1(Enemy) Idle

[Turn] T1(Enemy) End

Bonus Turn 时必须包含 “BonusT” 关键字，比如：[Turn] BonusT(1P) 之类（蓝色）

动作管线：

[Action] ... W1_AimBegin / W1_AimCancel

[Action] ... W2_PreDeductCheckOk / W2_PreDeductCheckFail(reason=...)

[Action] ... W2_ChainPromptOpen(count=3) / W2_ChainPromptAbort(cancel)

[Action] ... W3_ExecuteBegin(...) / W3_ExecuteEnd

[Action] ... W4_ResolveBegin(...) / W4_ResolveEnd(...)

[Chain] DerivedPromptOpen(...) / DerivedPromptAbort(cancel)

[Derived] Select(id=..., kind=Derived)

资源和持续：

[CD] Tick -6s (...)

[Res] Regen T3(UnitX) +6 (EndTurnRegen)

[Sticky] Apply/Refresh/Tick/Expire ...

[DOT] Tick {amount} (...)

禁改项：
这些日志行的前缀（[Turn], [Action], [CD], [Sticky], …）和关键字段（Begin, End, BonusT, Idle, W2_ChainPromptOpen）是协议。
BattleUIService 直接依赖它们。
改日志 = 你必须同步更新 BattleUIService 的解析。否则 Banner / HUD 会错乱。

5. UI & BattleUIService 工程约定（新，必须遵守）

这是我们这几天做的最重要的新东西。
它决定 codeX 以后怎么写 UI，怎么不把战斗逻辑炸掉。

5.1 BattleUIService 是全场 UI 总控

BattleUIService 现在是战斗 UI 的“唯一入口”。它负责：

获取并缓存：

TurnManagerV2

CombatActionManagerV2

BattleAudioManager

以下所有 UI 视图对象

TurnTimelineController

ChainPopupPresenter

TurnHudController

TurnBannerController（新的回合提示Banner）

在 OnEnable() 时：

AutoFind 这些引用（我们现在允许自动搜对象，后面会说为什么可以）

调用每个子 UI 的 Initialize(...)

订阅 TMV2/CAMV2 的事件

把初始状态立即推送给 HUD / Timeline / Banner 等

在 OnDisable() / OnDestroy()：

取消订阅

告诉子 UI Shutdown() 或 ForceHideImmediate()

停掉音频事件

非常关键：
子 UI 控制器不再自己订阅 TMV2/CAMV2，也不再自己 OnEnable/OnDisable 里做这件事。
所有订阅都放到 BattleUIService。
这叫“解耦”，这就是我们这几天辛苦干出来的。

5.2 TurnHudController（回合中当前角色HUD）

现在是“哑视图（dumb view）”：

有 Initialize(turnManager, combatManager)

有 Shutdown()

提供一组 HandleTurnStarted(...), HandlePhaseBegan(...), HandleUnitRuntimeChanged(...), HandleBonusTurnStateChanged() 等等的公开方法

不再自己去抓 TMV2 的事件。

不再自己负责订阅/退订。

BattleUIService 统一把 TMV2 的事件转发给它。

状态刷新

TurnHudController 内部会：

计算人物HP/能量条

更新沙漏（剩余秒数 vs 已花秒数）

根据当前是玩家回合/敌人回合/bonus turn 决定可见性

这个文件是目前“最干净、最稳定”的 UI。

→ codeX 在改 HUD 相关功能时，必须继续保持这个范式。

5.3 TurnTimelineController（时间轴）

现状（今天的状态）：

Timeline 自己不应该再直接引用 TMV2 / CAMV2 进行订阅。

它应当提供：

Initialize(turnManager, combatManager)

Shutdown()（或 Deinitialize()，最终我们会统一叫 Shutdown()）

若需要从回合事件更新，就暴露公共回调，例如：

NotifyPhaseBeganExternal(isPlayerPhase)

NotifyTurnStartedExternal(unit)

NotifyTurnOrderChangedExternal(isPlayerSide)

NotifyBonusTurnStateChangedExternal()

BattleUIService 在收到 TMV2/CAMV2 的事件后，转发给 Timeline。

测试状态：

如果 BattleUIService 关闭，我们允许 Timeline 完全不显示（包括不显示任何“默认静态竖条占位UI”）。

禁止 Timeline 自己还在场景里渲染一根“静态柱子”或“测试占位头像”。
这个行为必须彻底删除。

codeX 做 Timeline 后续工作时，要按这个合同来。
禁止 Timeline 自己 OnEnable() 里偷偷监听 TurnManagerV2。那是老做法，已经判死刑。

5.4 ChainPopupPresenter（连锁弹窗 / 反应技能弹窗）

状态：

现在它有 Initialize(turnManager, combatManager) 和 Shutdown()

它仍然是一个 UIToolkit 驱动的弹窗（UIDocument + VisualTreeAsset）

它会在玩家进入 W2 / W2.1... 时弹出

它能播放 ChainPopupOpened 的事件给音频

它会根据 OpenWindow(...), UpdateStage(...), CloseWindow() 来更新 UI

目前我们接受的工程妥协：

如果整个 BattleUIRig 在运行时被关掉再打开（“热插拔”），这个弹窗不会恢复到中间态，它只会保持隐藏，直到下一次真实进入 W2 时才重新正确弹出来。

这是一个已知限制，我们允许它存在。

codeX 不要为了“热插拔后立即恢复之前那半个连锁弹窗状态”去改 CAMV2 / W2 流程 / 派生流程。禁止。

换句话说：
ChainPopupPresenter 热插拔恢复是“以后也许做”的豪华功能，不是现在的需求。
别在单机版上硬搞成Dota2掉线重连级别的复杂度。

5.5 TurnBannerController（回合提示 Banner）

这个是 UGUI 的小浮条，用来显示

[Turn] T1(Player) Begin

[Turn] T1(Player) Idle

BonusT(...) (蓝色)

[Turn] ... End
全部用英文，不做本地化。同一个关键词映射固定颜色：玩家绿、敌人红、Bonus蓝。

它有接口 EnqueueMessage(string message, TurnBannerTone tone)，自己管理淡入、停留、淡出。

它完全不依赖TMV2，它只吃 BattleUIService 喂过来的消息。

BattleUIService 怎么知道要发什么？
BattleUIService 订阅 Application.logMessageReceived，拦 [Turn] ... 这样的日志，解析是谁的回合 / 是否 Bonus / 是 Begin 还是 End，然后把结果投给 TurnBannerController。

这个行为叫“日志驱动 UI banner”。

我们接受这点。原因：

我们的日志格式是我们自己定的，没人乱动。

就算解析出问题，战斗逻辑也不会被破坏，顶多是 banner 显示错颜色。

这是小团队能承受并且开发效率极高的做法。

codeX 允许继续做这种日志解析，禁止嫌弃它 dirty 然后擅自改整套 TMV2 回调接口。

5.6 BattleUIService 的 AutoFind 行为

BattleUIService 会在 Awake / OnEnable 里用 FindFirstObjectByType<T> 之类的方式自动把 turnManager / combatManager / timeline / chainPopup / hud / audioManager / bannerController 都抓出来。

这样做在大团队/多人在线/多场景 Additive 加载时很危险，因为可能找到错的对象。

但我们现在是单场景、单副本、单 UI rig，AutoFind 提高了容错率和装配效率。

所以：目前允许 AutoFind 保留。
但 codeX 必须遵守：

不要在其它无关 prefab 里再放第二套 TurnManagerV2/BattleUIRig，避免 AutoFind 混乱。

不要在同一场景里复制出第二个 BattleUIRig 并同时激活两个。

未来什么时候要去掉 AutoFind？
当我们开始做“多战斗场景、场景跳转、观战视角”等复杂流程。那时会统一改成手工拖引用。

6. 我们刻意留下的坑（允许存在，别乱补）

下面这些坑是我们刻意“暂时接受”的工程债，不是你可以自由尝试修的“任务灵感”。请不要擅自进坑。

ChainPopupPresenter 热插拔恢复不中断

关掉 BattleUIRig -> 再打开 -> 弹窗不自动还原。

接受。以后需要多人掉线重连级别时再修。现在别动。

Timeline 重新启用后拖拽功能未完全恢复 / 首帧静态状态

热插拔测试时能看到头像列表恢复，但是拖拽排序、插队预览 等复杂交互，可能不会第一时间恢复。

接受。因为在正式单机流程中，玩家不会在战斗中“Disable→Enable Timeline”。

BattleUIService 通过 Application.logMessageReceived 来驱动 TurnBanner

这是我们的“日志=协议”。

允许保留。

任何想把它改成“Banner 直接监听 TMV2 事件”的冲动都必须压住，除非我们专门排期做 UI 多语言 / 观战模式化。

BattleUIService 知道所有人的存在（高耦合）

Service 现在是个“上帝类”，能看到 TMV2、CAMV2、Audio、Timeline、HUD、Popup、Banner。

允许。因为这让我们能一键关掉整个 UI rig，不担心某个子控件偷偷还在订阅战斗事件。

以后如果我们做第二种 UI 皮肤（比如观战HUD vs 玩家HUD），才会分多种 Service 或 Adapter。暂时不用。

Unit 构建链仍然是临时拼接

目前玩家/敌人单位还靠 UnitAutoWireV2、HexBoardTestDriver 这种临时脚手架来接在棋盘上。

下个月要开始 UnitFactory，把角色“怎样出生+怎样接线+怎样注册到 TMV2/CAMV2/HexSpace”做成正式管线。

在 UnitFactory 尚未完成前，不要试图“顺手”把这些旧脚手架删光。先保证能跑。

7. codeX 的工作红线 / 开放区

这是给 codeX 的直接指令。照抄到 agent.md 里。

✅ 允许你改 / 你可以扩展的区域

你可以在 TurnHudController 内部，改善UI表现（比如血条动画、能量条动画、沙漏动画表现），但：

不要改它的对外方法签名（Initialize, Shutdown, HandleTurnStarted(...) 等）。

不要让它重新去订阅 TMV2。订阅必须还是 BattleUIService 做。

你可以修 TurnTimelineController 的内部UI布局、样式、头像框、拖拽手感等，但：

仍然不允许它自己订阅 TMV2 / CAMV2。

它必须依靠 BattleUIService 的回调（NotifyTurnStartedExternal(...) 等）。

你可以改 TurnBannerController 的视觉（颜色、淡入淡出曲线、字体），但：

EnqueueMessage(string, TurnBannerTone) 这个接口必须保持。

TurnBannerTone 枚举必须保持四种（Friendly / Enemy / Bonus / Neutral）。

它不准自己解析战斗状态，仍然只能吃 BattleUIService 喂的信息。

你可以调整 ChainPopupPresenter 的 UI Toolkit 布局、按钮样式、anchor 算法、缩放逻辑等，但：

Initialize(turnManager, combatManager) / Shutdown() 仍然保留。

不要尝试在它内部自己抓 CAMV2 并发起逻辑调用。

不要试图在 UI Enable 的瞬间恢复上一次半成品连锁窗状态。我们现在不支持这个。

你可以修改 BattleUIService 的内部实现细节（比如更安全地退订，或者更少的重复 AutoFind 调用），但：

不能把订阅逻辑重新塞回各个子UI。

不能让 Timeline/Popup/HUD/Banner 直接订 TurnManagerV2。

不能把 CAMV2 改成 “UI主导”。

❌ 禁止你碰 / 禁止你“顺手优化”的区域

禁止修改 TMV2 / CAMV2 的核心时序（PhaseStart → Turns → EndTurn），特别是 1秒停顿的 PhaseStart，不要删。

禁止改变连锁弹窗（W2/W2.1...） vs 派生弹窗（W4.5）的关系。不要把它们合并成一个窗，不要把 W4.5 提前。

禁止让 UI 直接扣时间、能量、冷却。扣费只能在 CAMV2→TMV2。

禁止改 Log 的前缀和关键字段（比如 [Turn], Begin, End, BonusT, Idle, W2_ChainPromptOpen）。这些是协议。

禁止在 UXML 里手写 <Style src="... GUID=... fileID=...> 等引用。
这些必须由 Unity UI Builder 自己生成。人工写会把 UI Builder 弄崩（这个我们踩过大坑）。

禁止把第二套 TMV2 / 第二套 BattleUIRig 同时激活在同一场景里。我们还没准备好多实例支持。

8. 下一个月的重点工作（roadmap 提前写死，别走偏）
8.1 UnitFactory 正式落地

目标：不再用临时 HexBoardTestDriver / UnitAutoWireV2 这种脚手架来往场景里手塞对象。

我们要一个“UnitFactory”，它负责：

创建一个 Unit（玩家/敌人）

给它挂对的组件（UnitRuntimeContext、HexClickMover、AttackControllerV2、动画监听器等）

注册到 TurnManagerV2 / CombatActionManagerV2

把它放进 HexSpace（统一坐标来源）

成功标准：以后新增敌人/角色 = 调用工厂 + 配配置文件，而不是手动拷 prefab 然后手绑十几个引用。

8.2 把现有 UI Prefab 全部挂到 BattleUIRig 下面

现在 Timeline / HUD / ChainPopup / Banner 都已经在 Rig 里了。

要求所有新 UI（未来还会有技能条、状态列表等）也走同样套路：都在 Rig 下，由 BattleUIService 管。

测试方式固定：

启动游戏时：BattleUIRig 启用 → 全部UI正常

禁用 BattleUIRig（整个 GameObject 设 inactive）→ 所有UI一起消失

再启用 → HUD 必须恢复正常（Timeline和ChainPopup热插拔后部分状态不恢复是允许的，见“坑”章节）

8.3 CombatV2 日志补全

确保 [Turn] ... Begin, Idle, End, BonusT 这些日志在正确的时点打印，字段一致。

确保 W1/W2/W3/W4/W4.5 每个阶段都有日志。

这些日志一方面是debug，一方面是UI数据源（Banner/将来详细战斗log面板）。

