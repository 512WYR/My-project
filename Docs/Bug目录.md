# Bug 目录

本文档汇总当前已知问题，按“一个 bug 一节”维护。新增问题时在索引表补一行，并按下面的固定结构追加小节。

记录日期：2026-10-18
验证环境：`dotnet build`（Ruin.Core / Runtime / Editor / EditModeTests / PlayModeTests）+ 独立 `Ruin.Core` 行为测试；当前环境无法启动 Unity，主场景表现以代码路径分析为准。

## 索引

| 编号 | 标题 | 严重程度 | 状态 | 影响范围 |
|---|---|---|---|---|
| BUG-001 | 失衡 buff 没有效果 | 中 | 已定位（规则本身正常，问题在免疫反馈与可观察性） | 战士「猛击」`w_bash`；主场景 HUD |
| BUG-002 | 群体技能在主场景只能选择无法释放 | 高 | 已定位（未修复） | 战士「追击」、盾卫「壁垒」「全队格挡反击」、奶妈「群体药雾」「驱散疗法」 |
| BUG-003 | 拖拽攻击伤害有效但拖拽无效 | 中 | 已定位（守护挡刀场景已复现） | 枪手「拖拽射击」`g_grapple`；敌方守护单位 |

---

## BUG-001 失衡 buff 没有效果

### 现象

战士使用「猛击」（`w_bash`）命中敌人后，玩家感知不到失衡生效：敌人没有状态图标，仍然会行动；连续使用猛击时，有时完全没有任何反馈。

### 复现步骤

1. 进入 `StageOne.unity`，让战士站到 1 号位（`w_bash` 的 `ActorRanks = 3`，只允许 1 号位施放）。
2. 对敌人使用「猛击」。
3. 观察敌人卡片与后续行动。
4. 下一回合继续使用「猛击」。

### 影响范围

- 只有「猛击」携带 `Imbalance = 1`（全工程唯一，见 `Assets/StageOne/Resources/StageOne/Skills/w_bash.asset`）。
- 规则层影响所有角色；表现层影响主场景 HUD 与战斗日志。

### 规则层验证结果

用独立 `Ruin.Core` 行为测试（英雄速度 20、敌人速度 10，英雄先手使用带失衡的 100% 命中攻击）得到：

```
current=hero round=1
execute=True enemyImbalance=0 immuneRound=2 round=2 current=hero
  [action] hero · bash 暴击！ target=enemy_0
  [control] foe 失衡，本回合无法行动。 target=enemy_0
  [control] foe 陷入失衡，本回合无法行动。 target=enemy_0
  [round] —— 第 2 回合 ——
enemyActed=False
```

结论：**引擎的失衡规则本身是生效的**——`ApplyOnHit` 写入失衡后，`Advance()` 在选中该目标时递减并跳过它的行动，敌人没有产生 `action` 事件。

### 根因分析

按影响程度排序：

1. **失衡免疫导致“隔一次猛击才生效”，且被拒绝时没有任何提示（最可能被感知为“没有效果”）**
   - `SkillSpec.Imbalance` 施加条件：`target.ImbalanceImmuneRound != Round`（`Assets/StageOne/Core/BattleEngine.cs:522`）。
   - 施加成功后写入 `ImbalanceImmuneRound = Round + Rules.ImbalanceImmuneRounds`（`BattleEngine.cs:525`），默认 `ImbalanceImmuneRounds = 1`（`Assets/StageOne/Core/Models.cs:206`）。
   - 因此第 1 回合猛击生效后，**第 2 回合的猛击会被静默拒绝**：不施加失衡、不写日志、HUD 也没有提示；第 3 回合才再次生效。
   - 玩家看到的现象就是“猛击有时有用、有时完全没用”。

2. **主场景完全不显示失衡状态**
   - 敌方卡片只显示 `Intent`，没有状态行（`Assets/GothicHUD/Runtime/GothicBattleCard.cs:80-83`）。
   - 我方状态行 `StatusLine` 只包含部位、流血、形态（`GothicBattleCard.cs:92-102`），不含失衡。
   - 即使失衡成功施加，玩家在界面上也看不到任何变化。

3. **跳过与施加发生在同一次推进中，缺少“敌人被阻止”的反馈**
   - 玩家行动后 `Battle.Execute` 内部立即 `Advance()`，失衡目标的跳过也在同一次调用中完成（`BattleEngine.cs:182-187`）。
   - 玩家只会看到回合直接跳到自己，没有一个“敌人试图行动但被拦下”的可感知瞬间。

4. **日志重复且文案不准确**
   - 施加时记录“失衡，本回合无法行动”（`BattleEngine.cs:526`），跳过时又记录“陷入失衡，本回合无法行动”（`BattleEngine.cs:185`），同一件事出现两条。
   - 如果目标在本回合已经行动过，实际被跳过的是**下一次行动**（通常在下一回合），文案“本回合”会误导。

### 修复建议

1. 表现层（优先，不改规则）：
   - 在敌方卡片和 `StatusLine` 中显示“失衡：下次行动跳过”，并在被免疫时显示“失衡免疫中”或输出一条 `debug/control` 日志。
   - 把施加日志改为“失衡：下一次行动将被跳过”，跳过关卡日志改为“因失衡跳过行动”，避免重复。
2. 若设计意图是“每次命中都造成失衡”，把 `CombatRules.ImbalanceImmuneRounds` 默认值改为 `0`。此时 `ImbalanceImmuneRound = Round`，只禁止同一回合内重复施加，跨回合不再免疫。
3. 若要保留“隔回合生效”，至少在被拒绝时给出明确反馈，避免玩家误判为技能失效。

### 验收标准

- 主场景中猛击命中后，敌人卡片立即出现失衡状态。
- 敌人下一次行动被跳过，日志只有一条清晰说明。
- 处于免疫回合时，再次猛击有明确提示（或按规则改为不免疫）。
- 平衡性改动后，EditMode 增加一条“失衡跨回合施加/免疫”的回归测试。

### 备注

在 Battle Lab 中直接在角色编辑区把 `Imbalance` 设为 1 不会跳过该角色**当前**这一回合：失衡检查发生在 `Advance()` 选中行动者时（`BattleEngine.cs:182`），而 Battle Lab 的“单步”在 `Current` 已是该角色时走的是 `ExecuteEnemy()`，不会再选一次行动者。要复现跳过，应在该角色行动前设置失衡，或使用“跳过当前行动”观察语义差异。

---

## BUG-002 群体技能在主场景只能选择无法释放

### 现象

选择「追击」「壁垒」「全队格挡反击」「群体药雾」「驱散疗法」后，界面提示“点击高亮目标以执行”，但场上没有任何高亮目标；点击任意角色都不会释放技能，只能按 `Esc` 取消。技能本身没有被禁用，看起来可以正常选择。

### 复现步骤

1. 进入 `StageOne.unity`，编队包含战士 / 盾卫 / 奶妈。
2. 轮到对应角色行动，点击上述任一技能。
3. 观察：没有目标被高亮；点击我方或敌方角色均无反应。

### 影响范围

| 技能 | 角色 | 效果 | `Targeting` |
|---|---|---|---|
| `w_pursuit` 追击 | 战士 | 全队获得追击 | `All` |
| `s_bulwark` 壁垒 | 盾卫 | 全队防御提升 | `All` |
| `s_phalanx` 全队格挡反击 | 盾卫 | 全队格挡 + 反击 | `All` |
| `m_mist` 群体药雾 | 奶妈 | 全队治疗 | `All` |
| `m_cleanse` 驱散疗法 | 奶妈 | 全队移除减益 | `All` |

这 5 个技能在主场景中**全部无法释放**，属于战斗核心功能缺失。Battle Lab 不受影响（`BattleLabSession.Step` 对无需目标的技能直接传 `target = null`）。

### 根因

1. `BattleEngine.NeedsTarget` 只对 `SkillTargeting.Choose` 返回 `true`（`Assets/StageOne/Core/BattleEngine.cs:223-226`）。
2. `BattleEngine.Targets(index)` 在 `NeedsTarget` 为 `false` 时直接返回 `Enumerable.Empty<ActorState>()`，因此群体技能没有任何合法目标。
3. `BattleEngine.SkillDisabledReason(index)` 只在 `NeedsTarget(skill) && !Targets(index).Any()` 时报错（`BattleEngine.cs:236`）；群体技能不满足 `NeedsTarget`，所以**不会禁用**，技能按钮保持可点。
4. `StageOneApp.SelectSkill` 只把技能记到 `PendingSkill`，并提示“点击高亮目标”（`Assets/StageOne/Runtime/StageOneApp.cs:586-594`）。
5. 真正提交动作的只有 `StageOneApp.SelectActor`，条件是点击的角色出现在 `Battle.Targets(PendingSkill.Value)` 中（`StageOneApp.cs:608-617`）。群体技能的 `Targets()` 永远为空，因此**不存在任何可以提交动作的点击路径**。
6. 结果：技能可选、目标列表为空、无提交入口，形成死路。

### 修复建议

在 `StageOneApp.SelectSkill` 中补一条“无需目标即刻执行”的路径，与 Battle Lab 的语义对齐：

```csharp
public void SelectSkill(int index)
{
    if (Busy || HelpOpen || LabOpen || ConsumableEditorOpen || AwaitingNode || Battle == null) return;
    string reason = Battle.SkillDisabledReason(index);
    if (reason != null) { Hint = reason; Refresh(); return; }
    PendingSwap = false;

    // 群体技能没有可选目标，选中即释放。
    if (!Battle.Targets(index).Any())
    {
        Submit(new ActionRequest(ActionKind.Skill, null, index));
        return;
    }

    PendingSkill = index;
    Hint = "点击高亮目标以执行「" + Battle.SkillFor(Battle.Current, index).Name + "」。Esc取消选择。";
    Refresh();
}
```

同时：

- 若希望保留“选中后再确认”的交互，可改为显示一个“确认释放”按钮，而不是依赖点击目标。
- 检查 `StageOneView` 的技能提示与目标高亮逻辑，确保群体技能不会显示“请选择目标”。
- 为 5 个 `Targeting = All` 的技能补一条 EditMode 或 PlayMode 回归测试：`Targets(index)` 为空时仍能通过 `Execute(new ActionRequest(ActionKind.Skill, null, index))` 成功释放，并且事件、消耗、回合推进正常。

### 验收标准

- 上述 5 个技能在主场景中点击后立即释放，正确作用于己方全体。
- 释放后正常消耗行动、技能次数（`s_phalanx` 每场 1 次）并推进敌方回合。
- 需要选择目标的技能（`Targeting = Choose`）仍保持“先选技能、再点目标”的原有流程。
- Esc 取消、Busy 期间屏蔽、帮助/调试面板打开时屏蔽等现有保护不被破坏。

---

## BUG-003 拖拽攻击伤害有效但拖拽无效

### 现象

枪手使用「拖拽射击」（`g_grapple`）命中后，目标正常掉血，但没有被拖到 1 号位，也没有任何“拖拽失败”的提示。换上不同的敌人组合后，有时能拖动、有时完全不动，看起来像随机失效。

### 复现步骤

1. 进入 `StageOne.unity`，队伍包含枪手，枪手站在 3 或 4 号位（`g_grapple` 的 `ActorRanks = 12`）。
2. 遇到带有「肉盾怪 / 精英肉盾怪」的编队，并让肉盾成功施放「守护」。
3. 对肉盾守护的后排敌人使用「拖拽射击」。
4. 观察：伤害由肉盾承受（日志显示“为……挡下攻击”），但被选中的后排敌人位置不变，也没有拖拽日志。

### 影响范围

- 技能：枪手「拖拽射击」`g_grapple`（`PullToFront = 1`，`PullChance = 0.3`，`TargetRanks = 12`）。
- 敌人：会施放「守护」的肉盾怪 `tank` / 精英肉盾怪 `elite_tank`（`e_tank_guard` / `e_elite_tank_guard`）。
- 表现层：主场景战斗日志与 HUD 没有拖拽失败反馈。

### 规则层验证结果

用独立 `Ruin.Core` 行为测试（枪手速度 100 先手、拖拽概率强制为 100%、肉盾在 1 号位守护 3 号位的回血怪）得到：

```
== 无守护，拖拽必中 ok=True err=
tankHp=100->100 tankRank=1 healerHp=88 healerRank=0
  [action] gunner · 拖拽射击 暴击！
  [damage] healer 受到 12 点伤害。
  [move] healer 被拖拽到 1 号位。

== 守护挡刀，拖拽必中 ok=True err=
tankHp=100->88 healerHp=100 healerRank=2
  [action] gunner · 拖拽射击 暴击！
  [guard] tank 为 healer 挡下攻击。
  [damage] tank 受到 12 点伤害。
```

结论：

- 无守护时，伤害与拖拽都正常，目标从 3 号位被拖到 1 号位。
- 有守护时，伤害被转移到肉盾身上，**拖拽效果也跟到了肉盾身上**；肉盾本来就在 1 号位，`PullToFront` 直接静默返回，所以玩家看到的就是“伤害有效、拖拽无效”。

### 根因分析

1. **守护挡刀后，命中效果被施加到了 `victim`（挡刀者），而不是玩家选择的主目标**
   - `ResolveAttack` 用 `GuardedTarget(target)` 把 `victim` 换成守护者（`Assets/StageOne/Core/BattleEngine.cs:371`），随后调用 `ApplyOnHit(actor, victim, skill, target == main)`（`BattleEngine.cs:379`）。
   - `ApplyOnHit` 里的拖拽只认传入的 `target`：`if (skill.PullToFront && !target.Player && Roll(skill.PullChance)) PullToFront(target);`（`BattleEngine.cs:540`）。
   - 因此实际被拖拽的是挡刀的肉盾，而不是被选中的后排敌人。若肉盾已经在 1 号位，`PullToFront` 的 `if (from <= 0) return;`（`BattleEngine.cs:685`）会**不打日志直接返回**，拖拽彻底不可见。
2. **拖拽概率失败时没有反馈**
   - `PullChance = 0.3`，失败时 `ApplyOnHit` 不写任何日志，玩家无法区分“30% 没触发”和“技能坏了”。
   - 这是正常的概率设计，但缺少反馈会被感知为 bug。
3. **部分失败场景只有日志、没有界面提示**
   - 目标 `Immobile` 时 `PullToFront` 会记录“固定在原地，没有被拖拽”（`BattleEngine.cs:679-683`），但主场景 HUD 不展示战斗日志，玩家仍然看不到。
   - 目标在伤害结算后死亡时不会执行 `ApplyOnHit`（`BattleEngine.cs:379` 的 `if (!victim.Dead)`），此时不拖拽是合理的，但也没有提示。
4. **目标本来就在 1 号位时静默返回**
   - `g_grapple` 的 `TargetRanks = 12` 正常情况下选不到 1 号位目标，所以这条主要影响“守护者被误拖”的情况；但作为通用逻辑，建议补一条日志。

### 修复建议

1. 明确“守护挡刀时拖拽谁”的规则，并在代码里显式表达：
   - 推荐：**拖拽效果只对玩家选择的主目标生效**。守护挡刀只转移伤害，位置效果仍应作用于 `target`；若主目标已被守护，可选择“拖拽失败并提示”，或“把主目标拖到 1 号位”两种规则之一，但不能默默拖错人。
   - 实现上可在 `ResolveAttack` 中把“伤害承受者”和“命中效果承受者”拆开：`Damage(victim, ...)` 用 `victim`，位移动/控制类效果用 `target`（或显式只对 `target == main` 施加）。
2. 给拖拽失败补日志，至少区分：
   - 概率未触发：“拖拽未生效（30% 概率）”。
   - 目标固定在原地：“固定在原地，没有被拖拽”。
   - 目标已在 1 号位：“已经在 1 号位，无需拖拽”。
3. 主场景 HUD 增加一条短暂的状态提示（或在敌人卡片上显示“被拖拽”动画），避免只依赖日志。
4. 补一条 EditMode 回归测试：守护挡刀时，伤害落在守护者、拖拽效果落在主目标（或按最终规则断言），并且不会把守护者错误拖走。

### 验收标准

- 对未受守护的合法目标使用拖拽射击：伤害与拖拽都生效，目标移动到 1 号位。
- 对受守护的目标使用拖拽射击：伤害转移给守护者，拖拽按明确规则执行，并且界面/日志能说明结果。
- 概率未触发、目标免疫拖拽、目标已在 1 号位三种情况都有可读反馈。
- 拖拽后不会出现两个单位占用同一站位，也不会把无关单位拖到 1 号位。

---

## 附录 A：验证方式

- 编译：`dotnet build Ruin.Core.csproj`、`Ruin.Runtime.csproj`、`Ruin.Editor.csproj`、`Ruin.EditModeTests.csproj`、`Ruin.PlayModeTests.csproj`。
- 规则行为：在仓库外建立临时控制台工程，引用 `Ruin.Core.dll`，构造 `RunState` + `BattleEngine`，检查事件序列与 `Current` / `Round` / `Imbalance`。
- 主场景表现：当前环境无法启动 Unity，BUG-002 由 `StageOneApp` 的调用链直接确认；BUG-001 的规则层已由独立测试确认，表现层需在 Unity 内人工复核。

## 附录 B：相关代码位置

| 位置 | 说明 |
|---|---|
| `Assets/StageOne/Core/BattleEngine.cs:182-187` | 行动者被选中时的失衡跳过 |
| `Assets/StageOne/Core/BattleEngine.cs:223-226` | `NeedsTarget` 只认 `SkillTargeting.Choose` |
| `Assets/StageOne/Core/BattleEngine.cs:520-527` | `ApplyOnHit` 施加失衡与免疫判定 |
| `Assets/StageOne/Core/BattleEngine.cs:371-379` | 守护挡刀把伤害与命中效果一起转移到 `victim` |
| `Assets/StageOne/Core/BattleEngine.cs:540` | 拖拽判定与静默失败 |
| `Assets/StageOne/Core/BattleEngine.cs:677-692` | `PullToFront` 的移动与提前返回 |
| `Assets/StageOne/Core/Models.cs:206` | `ImbalanceImmuneRounds` 默认值 |
| `Assets/StageOne/Resources/StageOne/Skills/g_grapple.asset` | 拖拽射击数值（75% 伤害 / 30% 拖拽） |
| `Assets/StageOne/Runtime/StageOneApp.cs:586-594` | `SelectSkill` 只记录 `PendingSkill` |
| `Assets/StageOne/Runtime/StageOneApp.cs:608-617` | `SelectActor` 是唯一的技能提交入口 |
| `Assets/GothicHUD/Runtime/GothicBattleCard.cs:80-102` | 主场景卡片状态行，未显示失衡 |
| `Assets/StageOne/Editor/StageOneBuild.cs:92-166` | 5 个群体技能的定义位置 |
