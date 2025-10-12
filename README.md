# TGD CombatV2 · TurnManagerV2 Rules (Calibrated V2) & Implementation Guide

> **Authoring target**: CodeX and collaborators.  
> Scope: **strict timing rules** for TurnManagerV2 (TMV2), **logging spec**, **environment sticky rules**, **event decoupling**, and **implementation milestones**.  
> This README is the **source of truth** for timing and logs.

---

## 0) TL;DR — What must always happen, in order

A full **Round = Player side + Enemy side (≈12s)**. Each side has **three mandatory timing nodes**:

```
[Phase] Begin  Tn(Side)    <-- Refresh visible stats once here (no reactions; Free-only allowed)
[Turn]  Tn(Side)           <-- The side's unit-turns happen here (normal actions run here)
[Turn]  End    Tn(Side)    <-- Tick cooldowns (-6s), buff turns (-1), side resource regen
```

> **Only two tick points per round**: Player **End** and Enemy **End**.  
> **No reactions** may be chained during **Begin**/**End** nodes (Free actions allowed).  
> UI and tests rely on the **exact logs below** — they are part of the contract.

---

## 1) Core Timeline & Terms

- **Turn (unit-turn time budget)**: `6 + Speed` seconds (integer).  
- **Round**: Player side + Enemy side (≈`12s`).  
- **Unit-time budget** is **consumed only during the side's `[Turn]` node**.

---

## 2) Cooling/Duration Units & When they tick

### Cooldowns
- Stored internally in **seconds**; display as **turns** (`ceil(sec/6)`).
- **Ticking**: at **Player `[Turn] End`** and **Enemy `[Turn] End`** — subtract `6s` from **all units'** cooldown stores.

### Buff/Debuff (turn-based)
- Stored as **turns**.
- **Ticking**: at **Player `[Turn] End`** and **Enemy `[Turn] End`** — each **unit's** turn-based buffs **-1 turn**.

### Ongoing DoT/HoT (round-based) *(reserved)*
- Unit = **Rounds** (one per Round).  
- **Ticking** will occur at **the matching side’s `[Phase] Begin`** (target-side). *(Hook reserved; may be implemented later.)*

---

## 3) Environment Sticky (MoveRate etc.)

- **Zero-turn** terrain patches must be **ignored** by the runtime (no sticky entry created; used only as instantaneous multiplier for the step).  
- **Apply/Refresh** rule:
  - At **every** `[Phase] Begin` (Player & Enemy): sample the unit’s current cell.  
  - If terrain has sticky with `turns > 0` and `mult != 1`, call `ApplyOrRefreshExclusive(tag, mult, turns)` to **refresh** (no stacking).  
- **Tick** rule: At **side `[Turn] End`**, all units’ sticky entries **-1 turn**. Entries with 0 **expire**.  
- **Visible stat refresh**: **only at `[Phase] Begin`**, after sticky refreshes — recompute and log the new **MoveRate** (and future stats).

**Required sticky logs**:
```
[Sticky] Apply   U=1P tag=Patch@x,y mult=2.00 turns=2
[Sticky] Refresh U=1P tag=Patch@x,y mult=2.00 turns=2
[Sticky] Tick    U=1P tag=Patch@x,y -> remain=1
[Sticky] Expire  U=1P tag=Patch@x,y
```

---

## 4) Event Channels — must stay decoupled

- **HexClickMover (precise move)** → emits only `HexMoveEvents.*`  
- **AttackControllerV2 (approach/attack)** → emits only `AttackEventsV2.*`  
- **Never** mix these channels inside the same operation. Anim/HUD listeners subscribe per-channel.

---

## 5) Logging Contract (exact lines & order)

Logging is part of the spec. We **must** print the following in order, every cycle:

### A) Phase begin (both sides)
```
[Phase] Begin  Tn(Player|Enemy)
[Buff]  Refresh U=<unit> mr:<old> -> <new> (recomputed tags:<...>)
[Phase] Idle   Tn(Player|Enemy) ≥1s
```

- **Refresh logs appear only here**. If nothing changed, still log the unit with current mr (or `recomputed tags:none`).

### B) Side turn (action window)
For each **unit** when it becomes active during the side turn:
```
[Turn] Begin  Tn(1P|2P|...|EnemyAI) TT=<turnTime> Prepaid=<x> Remain=<y>
...
[Turn] End    Tn(1P|2P|...|EnemyAI)             // End of this unit's active-turn window
```

> Note: **No ticking** happens at individual unit end; ticks are done at **side end** below.

### C) Side end (the only ticking point for this side)
```
[CD]   Tick  Tn(-6s all)
[Buff] Tick  Tn(-1 turn all)
[Res]  Regen Tn(Player|Enemy) <per-unit lines or totals>
[Turn] End   Tn(Player|Enemy)
```

### D) Example (one complete Round)

```
[Phase] Begin  T1(Player)
[Buff]  Refresh U=1P mr:4.00 -> 2.12 (recomputed tags:Patch@15,12:2)
[Buff]  Refresh U=2P (recomputed tags:none)
[Phase] Idle   T1(Player) ≥1s

[Turn] Begin   T1(1P) TT=9 Prepaid=0 Remain=9
... actions ...
[Turn] End     T1(1P)

[Turn] Begin   T1(2P) TT=6 Prepaid=0 Remain=6
... actions ...
[Turn] End     T1(2P)

[CD]   Tick    T1(-6s all)
[Buff] Tick    T1(-1 turn all)
[Res]  Regen   T1(Player) P1:+60->..., P2:+60->...
[Turn] End     T1(Player)

[Phase] Begin  T2(Enemy)
[Buff]  Refresh U=E1 (recomputed tags:none)
[Phase] Idle   T2(Enemy) ≥1s

[Turn] Begin   T2(EnemyAI) TT=6 Prepaid=0 Remain=6
... actions ...
[Turn] End     T2(EnemyAI)

[CD]   Tick    T2(-6s all)
[Buff] Tick    T2(-1 turn all)
[Res]  Regen   T2(Enemy) E1:+60->...
[Turn] End     T2(Enemy)
```

**During `[Phase] Begin` and `[Turn] End` nodes, reactions are disallowed (Free-only).**

---

## 6) Occupancy & movement correctness

- **Use one actor identity** for both Attack and Move when calling occupancy.  
- `TryMove(actor, to)` must internally **free old** and **set new**; temporary “reserved path” sets (if any) must be **cleared** at `AttackMoveFinished` and at side `[Turn] End` to avoid blocking precise move after approach.  
- On pathfail, print:  
  `"[Path] Blocked {hex} reason={Occupied|ReservedThisTurn|Enemy}"`

---

## 7) Action pipeline & settlement source

- When TMV2 is active:
  - Tools (`HexClickMover`, `AttackControllerV2`) run via **CombatActionManagerV2** (CAM).  
  - Tools report `UsedSeconds/RefundedSeconds` via `IActionExecReportV2`; **CAM → TMV2** performs **the only settlement** (time/energy).  
  - Tool-internal simulate flags should be **off** to avoid double charge.  
- HUD rejection happens in **Precheck** (no aim when failing).

---

## 8) Milestones (M0 → M5)

- **M0 (Done-ish)**: TMV2 shell, two-phase cadence, unified settlement, HUD gate.  
- **M1**: FullRound minimal — declare only at **side `[Turn]` begin** when time is full; execution at matching future `[Phase] Begin` (locks input while animating).  
- **M2**: DoT/HoT pipeline — caster-speed-scaled ticks at target-side `[Phase] Begin`; storage in seconds/rounds as designed.  
- **M3**: UI & Input consistency — global HUD reasons, Busy lock, clean aim cancel.  
- **M4**: Reactions — window collection, LIFO resolution, “refund time but not resource” for Standard cancellation (unit tests).  
- **M5**: Sustained (Boss) — window time recorded as `Prepaid` for next normal turn.

---

## 9) Test Scripts (must pass)

1. **Cadence-only**: Empty scene with TMV2 — see the full sequence exactly as in the example (Begin → Idle → unit turns → CD/Buff/Res → End).  
2. **Zero-turn terrain**: Standing on `turns=0` patch never creates sticky logs and never affects visible mr at refresh.  
3. **Sticky refresh**: Stand still on `turns=2` patch for 3 cycles — see refresh at each `[Phase] Begin`, ticks at each side `[Turn] End`.  
4. **Approach→Precise path**: After attack-move over A,B,C, immediately precise-move back into A — must be allowed; if blocked, the block reason must be logged.  
5. **Settlement single-source**: No duplicate energy/time logs from tools when TMV2 is present.

---

## 10) Binding Checklist (quick)

- Each unit GameObject has:
  - `HexBoardTestDriver` (UnitRef, Layout, Map, View)
  - `UnitRuntimeContext` (stats, cooldownHub, resources)
  - `HexClickMover` (precise move) — emits `HexMoveEvents.*`
  - `AttackControllerV2` (approach/attack) — emits `AttackEventsV2.*`
  - `UnitMoveAnimListener` (subscribe HexMoveEvents)  
  - `AttackMoveAnimListener` (subscribe AttackEventsV2)  
  - `AttackAnimDriver` (animation events → Strike/End)

- `CombatActionManagerV2`:
  - wired to `TurnManagerV2`
  - `tools = [HexClickMover, AttackControllerV2]`

- HUD listeners per channel (require-unit-match on):  
  `MoveHudListenerTMP` ↔ `HexMoveEvents` ; `AttackHudListenerTMP` ↔ `AttackEventsV2`.

---

## 11) Implementation Hints

- All timers/stores in **seconds** internally; convert to **turns** only for UI.  
- Phase machine structure in TMV2:
  - `PlayerPhaseBegin → PlayerUnitTurns → PlayerPhaseEnd → EnemyPhaseBegin → EnemyUnitTurns → EnemyPhaseEnd → loop`  
- The **only** places that tick cooldown/buff & regen are **PlayerPhaseEnd** and **EnemyPhaseEnd**.

---

*This README supersedes earlier drafts. Any deviation in timing/log order is considered a bug.*
