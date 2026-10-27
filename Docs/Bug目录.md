# Bug 目录

本文档汇总当前已知问题，按“一个 bug 一节”维护。新增问题时在索引表补一行，并按下面的固定结构追加小节。

记录日期：2026-10-18
验证环境：`dotnet build`（Ruin.Core / Runtime / Editor / EditModeTests / PlayModeTests）+ 独立 `Ruin.Core` 行为测试；当前环境无法启动 Unity，主场景表现以代码路径分析为准。

## 索引

| 编号 | 标题 | 严重程度 | 状态 | 影响范围 |
|---|---|---|---|---|
| BUG-001 | 失衡 buff 没有效果 | 中 | 已修复（待 Unity 验收） | 战士「猛击」`w_bash`；主场景 HUD |
| BUG-002 | 群体技能在主场景只能选择无法释放 | 高 | 已修复（待 Unity 验收） | 战士「追击」、盾卫「壁垒」「全队格挡反击」、奶妈「群体药雾」「驱散疗法」 |
| BUG-003 | 拖拽攻击伤害有效但拖拽无效 | 中 | 已修复（待 Unity 验收） | 枪手「拖拽射击」`g_grapple`；敌方守护单位 |
| BUG-004 | 复制首领死亡单位未清理，隐形怪物持续攻击且战斗不结束 | 高 | 已修复（待 Unity 验收） | 复制首领 `copy_boss`；复制体召唤；组合首领操作怪补召；敌方站位与 HUD 卡片映射 |
| BUG-005 | Boss 战击败首领后无法结算，卡在战斗 | 高 | 已修复（根因随 BUG-004 修复；待按 Boss 逐一验收） | Boss01-复制、Boss03-组合；Boss02-巨型待确认；战斗结算流程 |

---

## BUG-001 失衡 buff 没有效果

### 现象

战士使用「猛击」（`w_bash`）命中敌人后，玩家感知不到失衡生效：敌人没有状态图标，仍然会行动；连续使用猛击时，有时完全没有任何反馈。

### 复现步骤

1. 进入 `Assets/Scenes/mainScene.unity`，让战士站到 1 号位（`w_bash` 的 `ActorRanks = 3`，只允许 1 号位施放）。
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

### 修复记录（2026-10-18）

保留“隔回合免疫”的原规则，补齐可观察性与反馈：

1. `BattleEngine.Intent` 新增三种状态文案：`已被击败`、`失衡：下次行动跳过`、`下一次行动被打断`。主场景敌方卡片现在能直接看到失衡。
2. `ApplyOnHit` 在免疫回合被拒绝时写入日志“处于失衡免疫，本次失衡未生效”，不再静默失败。
3. 施加日志改为“失衡：下一次行动将被跳过”，`Advance` 跳过日志改为“因失衡跳过行动”，消除同一事件的重复与“本回合”误导。
4. 新增 EditMode 回归测试 `ImbalanceIsShownOnTheIntentAndImmuneReapplicationIsLogged`，断言 `Intent` 展示、施加日志与免疫日志。
5. 独立控制台验证通过：`imbalance: value=1 intent=True appliedLog=True immuneLog=True`。

---

## BUG-002 群体技能在主场景只能选择无法释放

### 现象

选择「追击」「壁垒」「全队格挡反击」「群体药雾」「驱散疗法」后，界面提示“点击高亮目标以执行”，但场上没有任何高亮目标；点击任意角色都不会释放技能，只能按 `Esc` 取消。技能本身没有被禁用，看起来可以正常选择。

### 复现步骤

1. 进入 `Assets/Scenes/mainScene.unity`，编队包含战士 / 盾卫 / 奶妈。
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
4. `StageOneApp.SelectSkill` 只把技能记到 `PendingSkill`，并提示“点击高亮目标”（`Assets/MainGame/Runtime/StageOneApp.cs`）。
5. 真正提交动作的只有 `StageOneApp.SelectActor`，条件是点击的角色出现在 `Battle.Targets(PendingSkill.Value)` 中。群体技能的 `Targets()` 永远为空，因此**不存在任何可以提交动作的点击路径**。
6. 结果：技能可选、目标列表为空、无提交入口，形成死路。

### 修复建议

在 `StageOneApp.SelectSkill` 中补一条“无需目标即刻执行”的路径，与 Battle Lab 的语义对齐：

```csharp
public void SelectSkill(int index)
{
    if (Busy || CharacterPanelOpen || DebugOverlayOpen || AwaitingNode || Battle == null) return;
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
- 检查 `GothicBattleHudController` 的技能提示与目标高亮逻辑，确保群体技能不会显示“请选择目标”。
- 为 5 个 `Targeting = All` 的技能补一条 EditMode 或 PlayMode 回归测试：`Targets(index)` 为空时仍能通过 `Execute(new ActionRequest(ActionKind.Skill, null, index))` 成功释放，并且事件、消耗、回合推进正常。

### 验收标准

- 上述 5 个技能在主场景中第一次点击只选中并显示说明，再点击任意高亮目标才释放，正确作用于己方全体。
- 释放后正常消耗行动、技能次数（`s_phalanx` 每场 1 次）并推进敌方回合。
- 需要选择目标的技能（`Targeting = Choose`）仍保持“先选技能、再点目标”的原有流程。
- 不可用的技能（站位不符、次数用尽、没有合法目标）仍可点击，说明区显示技能详情与不可用原因，但不会进入待释放状态。
- Esc 取消、Busy 期间屏蔽、帮助/调试面板打开时屏蔽等现有保护不被破坏。

### 修复记录（2026-10-18，释放交互已被 2026-10-19 调整取代）

在主场景补上“无需目标即刻释放”的提交路径：

1. `StageOneApp.SelectSkill` 在 `SkillDisabledReason` 通过后检查 `Battle.Targets(index).Any()`；为空时直接 `Submit(new ActionRequest(ActionKind.Skill, null, index))`，不再进入 `PendingSkill` 等待点击。
2. 需要目标的技能仍走原来的“选技能 → 点高亮目标”流程；`SelectActor` 的提交逻辑未改动。
3. 该判断复用引擎公开语义：`Targeting = All` 的技能 `Targets()` 必然为空，而 `Choose` 技能若没有合法目标会先被 `SkillDisabledReason` 拦截，因此不会误提交。
4. 独立控制台验证“群体技能无目标可提交”：`allSkill: targets=empty submit=True`，覆盖「壁垒」「群体药雾」等技能共用的引擎路径。主场景点击链路仍需 Unity 内人工验收。

### 交互调整（2026-10-19）

“点击后立即释放”的体验被改为二次确认，并让不可用技能也能点开查看说明：

1. `StageOneApp` 新增 `InspectedSkill`，专门记录说明区正在展示的技能；它和等待目标的 `PendingSkill` 分离，不可用技能只写前者。
2. 群体/自身技能第一次点击进入 `PendingSkill` 并高亮其目标侧的全部存活角色；再点击任意一个高亮目标才调用 `Submit` 释放，点击其他技能、换位或 Esc 会取消。
3. `BattleEngine.Targets` 对 `Targeting = All` 的技能返回该技能目标侧的全部存活角色。群体技能的 `TargetRanks` 资产值为 0，因此这类技能不应用站位过滤；`ExecuteSkill` 仍忽略提交的目标 id，效果照常作用于整侧。
4. 不可用技能不再被 `Button.interactable = false` 拦住：槽位保持可点，按钮变暗、去掉了悬停高亮，说明区显示技能详情与 `SkillDisabledReason`；同时清空之前待释放的选择，避免读完说明后误点目标释放旧技能。
5. `GothicBattleHudController` 的选中标记改跟 `InspectedSkill`，因此不可用技能也有明确的“正在查看”反馈。
6. 新增 PlayMode 回归测试 `GroupSkillsReleaseFromAnyTargetAndBlockedSkillsStayReadable`，覆盖第一次点击不释放、点击任意目标释放、不可用技能只显示说明且不改变战斗。该测试尚待在 Unity Test Runner 中执行。

---

## BUG-003 拖拽攻击伤害有效但拖拽无效

### 现象

枪手使用「拖拽射击」（`g_grapple`）命中后，目标正常掉血，但没有被拖到 1 号位，也没有任何“拖拽失败”的提示。换上不同的敌人组合后，有时能拖动、有时完全不动，看起来像随机失效。

### 复现步骤

1. 进入 `Assets/Scenes/mainScene.unity`，队伍包含枪手，枪手站在 3 或 4 号位（`g_grapple` 的 `ActorRanks = 12`）。
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

### 修复记录（2026-10-18）

确定规则为“守护挡刀只转移伤害，拖拽始终作用于玩家选择的主目标”，并把判定从 `ApplyOnHit` 中拆出：

1. `ResolveAttack` 在 `ApplyOnHit` 之后单独调用 `TryPull(actor, target, skill)`，条件是 `target == main && skill.PullToFront && !target.Dead`；`victim`（守护者）不再参与拖拽。
2. 新增 `TryPull`：概率未触发时写日志“拖拽未生效（30% 概率未触发）”；`PullToFront` 对已在 1 号位、`Immobile` 与正常移动分别给出日志。
3. 新增 EditMode 回归测试 `PullMovesTheChosenEnemyEvenWhenAGuardTakesTheDamage`：断言伤害落在守护者、被守护者满血、被选中目标到 1 号位、守护者让到 2 号位。
4. 独立控制台验证通过：`guardPull: tankHp=190/200 wardHp=50 wardRank=0 tankRank=1`。
5. 拖拽概率最终定为 `0.4`（40%）；`g_grapple.asset`、`StageOneBuild.cs` 与游戏内描述已同步，概率未触发时的日志逻辑保留。

---

## BUG-004 复制首领死亡单位未清理，隐形怪物持续攻击且战斗不结束

### 现象

复制首领（Boss01-复制）战斗中，场上可见的怪物全部显示“已死亡”后，战斗仍然没有结束；下一回合开始，敌人继续行动并对队伍造成伤害，但画面上找不到对应的存活单位。有时还能看到新怪物被召唤到已经有尸体的站位上，卡片却仍然显示“已死亡”。组合首领（Boss03-组合）里操作怪被击败后也会出现类似情况：玩家看不到存活的操作怪，蒸汽机器却仍被持续操作。

### 复现步骤

**复制首领：**

1. 进入复制首领战（调试台“跳到首领层”后选择 `Boss01-复制`）。
2. 等到第 2 回合复制体开始出现。
3. 只击杀 1～3 号位中的一只复制体，保留首领和其他单位，观察该站位。
4. 下一回合开始时，确认该站位是否又生成了复制体，以及卡片显示的是新单位还是旧尸体。
5. 击杀首领与场上所有可见单位，观察战斗是否结算、下一回合是否仍受到攻击。

**组合首领：**

1. 进入 `Boss03-组合`，先不要击杀蒸汽机器。
2. 击杀 4 号位的操作怪，观察尸体是否留在 4 号位。
3. 下一回合确认 4 号位是否出现补召的操作怪，画面是否能看见它。
4. 观察蒸汽机器是否仍被操作并继续发动攻击。

### 影响范围

- 首领：`Boss01-复制`（`copy_boss`，`EnemyBehavior.CopyBoss`）必现；`Boss03-组合` 的操作怪补召存在同类问题。
- 机制：所有依赖“空站位”判断的召唤/补位逻辑，包括复制体 `SummonCopies` 与操作怪 `RespawnOperator`。
- 表现层：Gothic 战斗页的敌方 4 张卡片按站位一对一映射，尸体可遮住同站位的存活单位，出现卡片重叠。
- 结算：引擎的胜利条件本身没有写错，但会被“看不见的存活单位”阻塞，最终表现为 BUG-005。

### 根因分析

1. **死亡只改标记，不释放站位**
   - `Kill` 只执行 `actor.Dead = true`，没有把单位从 `Enemies` 中移除，也没有清空 `Rank`（`Assets/StageOne/Core/BattleEngine.cs:1587-1591`）。
   - 因此尸体仍然留在 `Enemies` 列表里，并继续占用它原来的 `Rank`。

2. **召唤与补召只把“存活单位”视为占位，于是和尸体生成在同一格**
   - `SummonCopies` 的空位判断是 `Enemies.Any(e => !e.Dead && e.OccupiesRank(rank))`（`BattleEngine.cs:1307-1312`）。尸体不算占位，所以 1～3 号位即使已经有尸体，也会继续在该位置 `SpawnCopy`。
   - `RespawnOperator` 从 3 号位向前找空位时同样只检查 `!e.Dead`（`BattleEngine.cs:1355-1357`），而死亡的操作怪尸体就在 4 号位（`Rank = 3`），补召单位因此直接叠在尸体上。
   - 结果：同一个 `Rank` 同时存在一个死亡单位和一个存活单位。

3. **HUD 每个站位只取第一个匹配单位，尸体把存活单位挡住了**
   - Gothic 战斗页按站位取敌人：`battle.Enemies.FirstOrDefault(e => e.Rank == rank)`（`Assets/GothicHUD/Runtime/GothicBattleHudController.cs:284`），**没有过滤 `Dead`**。
   - 尸体在 `Enemies` 列表中创建得更早，`FirstOrDefault` 先返回尸体；后补召的存活单位排在列表末尾，因此永远不会被渲染到卡片上。
   - 旧的内置页面虽然遍历所有敌人，但同一站位的单位会被画在完全相同的坐标上，视觉上仍然只是一张卡片；该页面现已删除。

4. **胜利判定会正确看到隐藏的存活单位**
   - `CheckEnd` 使用 `Enemies.All(a => a.Dead)`（`BattleEngine.cs:1632-1637`）。隐藏的复制体/操作怪确实还活着，所以 `Outcome` 保持 `Active`，战斗不会结算。
   - 隐藏单位仍会进入回合队列并正常行动，于是玩家看到的就是“场上全死了，但每回合还在挨打”。

5. **复制体是持续补召的，玩家无法靠等待结束**
   - 复制首领存活时每回合开始都会把存活复制体补到 3 只（`BattleEngine.cs:1302-1314`）。即使首领死亡后停止补召，已经存在的隐藏复制体仍会继续行动，直到被打死；但玩家在界面上找不到它们。

### 修复建议

1. **先修“同站位一死一活”的不变量（优先）**
   - 死亡单位要么从 `Enemies` 中移出并单独保存尸体列表，要么在死亡时明确释放站位；总之保证任意时刻同一 `Rank` 最多只有一个存活单位，且尸体不参与占位判断。
   - 召唤/补召改为只在“没有存活单位且没有尸体”的站位生成，或先把尸体压缩掉再找空位。

2. **界面按存活单位映射卡片**
   - 至少把 Gothic 页的取数改为 `FirstOrDefault(e => !e.Dead && e.Rank == rank)`，避免尸体遮住存活单位。
   - 若需要保留尸体表现，应把尸体作为不可交互的背景层单独绘制，不能占用敌方卡片槽位。

3. **采纳“怪物死亡后自动向前移动一格”的建议**
   - 在 `Kill` 之后增加一次敌方阵型压缩：同一阵营中，死亡单位后方的存活单位依次向 1 号位方向补位，保持相对顺序不变。
   - 多格单位（`RankWidth > 1`，如巨型首领、蒸汽机器）需要整体移动，并检查目标区间是否与存活单位冲突；`Immobile` 单位是否参与补位需要先定规则。
   - 补位同时会改变玩家技能的合法目标位置，属于规则层改动，建议先在 Battle Lab 中确认手感，再同步更新技能描述文档。

4. **补一条不变量测试**
   - 构造“1 号位复制体死亡、下一回合补召”的场景，断言：不存在两个敌人占用同一站位；存活敌人能被界面枚举到；击杀所有存活敌人后 `Outcome == Victory`。

### 验收标准

- 复制首领战中，每个敌方站位最多显示一个存活单位；补召的复制体不会藏在尸体下面，也不会出现在画面外。
- 击杀全部存活敌人后立即进入战斗结算，下一回合不再出现敌方行动或伤害。
- 组合首领中操作怪死亡后，补召单位可见且不与尸体重叠；若蒸汽机器已死则不再补召。
- 尸体不会响应点击、不会成为技能合法目标，也不会阻塞站位或胜利判定。
- 增加 EditMode/PlayMode 回归测试覆盖“死亡 → 补召 → 击杀 → 胜利”全流程。

### 备注

- 本条与 BUG-005 是同一组现象的两个角度：本条描述“为什么会有看不见的怪”，BUG-005 描述“为什么战斗卡住不结算”。
- 用户建议的“死亡后自动往前移动一格”能同时缓解尸体挡位与阵型空洞，但它本身不能替代“尸体不参与界面映射/占位”的修复；两者建议一起做。
- **修复记录（2026-10-18）**：`BattleEngine` 新增死亡站位登记与 `ResolveEnemyRanksAfterDeaths()`，存活敌人会在当前行动结束后前移一格；死亡站位按从后往前的顺序释放，同一次范围攻击击杀多具尸体时存活单位会连续补位，而不是只补一格。Gothic 战斗页只给存活敌人分配卡片，并在站位冲突/越界时回退到第一个空卡片；旧的内置页面已随 `StageOneView` 一并删除。新增 EditMode 测试覆盖单次死亡与同回合双死亡补位，独立控制台复现“死亡前移、双死亡连续补位、复制体各占 1/2/3 号位”，待 Unity 内验收。

---

## BUG-005 Boss 战击败首领后无法结算，卡在战斗

### 现象

击败 Boss 后战斗没有结束，界面一直停留在战斗页：没有出现“战斗胜利 / 通关结算”面板，也没有可点击的“继续前进 / 通关结算”按钮。部分情况下队伍在 Boss 死亡后仍会每回合受到攻击。

### 复现步骤

1. 通过调试台跳到首领层，分别进入三个 Boss 战。
2. 按正常流程击杀 Boss 及场上所有可见敌人。
3. 观察是否弹出战斗结算面板。
4. 若未结算，继续等待若干回合，确认是否仍有敌方行动或伤害。
5. 对 `Boss01-复制` 与 `Boss03-组合`，特别检查是否有不可见的复制体/操作怪仍然存活（见 BUG-004）。

### 影响范围

- `Boss01-复制`、`Boss03-组合`：高概率由 BUG-004 的隐藏存活单位导致。
- `Boss02-巨型`：该战只有一只多格首领，理论上首领死亡后应直接满足胜利条件；若仍无法结算，需要单独排查结算界面与 `ContinueBattle` 流程。
- 影响本局通关、通关文案与后续新开演练；由于 Boss 是路线终点，玩家会被完全卡住。

### 根因分析

1. **胜利条件要求“所有敌方单位死亡”，而不是“首领死亡”**
   - `CheckEnd` 的唯一胜利条件是 `Enemies.All(a => a.Dead)`（`Assets/StageOne/Core/BattleEngine.cs:1632-1637`）。
   - 只要还有一只存活单位，战斗就保持 `Active`。这与界面文案“击败第 X 层首领，本局通关”（`Assets/MainGame/Runtime/StageOneApp.cs`）并不一致：玩家理解为首领死亡即通关，引擎却要求清场。

2. **Boss01：首领死亡后仍有最多 3 只存活复制体**
   - 首领死亡后 `RunRoundStartHooks` 不再补召（`BattleEngine.cs:1269-1275`），但此前召唤出的复制体仍然存活。
   - 这些复制体若被尸体挡住（BUG-004），玩家无法选中或看见，却仍参与 `CheckEnd` 与回合行动，战斗永远无法结束。

3. **Boss03：操作怪会在蒸汽机器存活时持续补召**
   - `Kill` 会给死亡的操作怪写入 `NextSpawnRound`（`BattleEngine.cs:1614-1615`），`RespawnOperator` 在蒸汽机器仍存活时每回合把它补回 4 号位（`BattleEngine.cs:1346-1369`）。
   - 补召单位与尸体同站位后，Gothic HUD 只显示尸体，玩家会认为“已经打完了”，但蒸汽机器仍被操作并继续蓄能攻击，战斗保持 `Active`。
   - 只有先击杀蒸汽机器，操作怪才不会继续补召；这一优先级在游戏内没有提示。

4. **结算界面本身依赖 `Battle.Outcome != Active`**
   - `RoomPages.Settlement` 只在 `Battle.Outcome != BattleOutcome.Active` 时生成结算页，由 `IndustrialEventPage` 显示。
   - 因此只要还有一只隐藏存活单位，界面就不会出现任何“结束”入口，表现为“打完一直卡在战斗”。
   - 所以问题不在 HUD 把结算盖住，而在战斗结果根本没有变成 `Victory`。

5. **Boss02 需要单独确认**
   - `Boss02-巨型` 的编队只有 `giant_boss`，没有召唤物；它死亡后 `Enemies.All(a => a.Dead)` 应为真。
   - 如果实测仍然卡住，应重点检查：多格 Boss 的死亡是否真的写入了 `Dead`、`ContinueEnemies` 协程是否退出、以及结算弹窗的 Canvas 排序/按钮是否被遮挡。目前代码路径上未发现必然阻塞点。

### 修复建议

1. **先修复 BUG-004**，保证不存在“看不见但存活”的敌人。这是 Boss01/Boss03 卡死的直接原因。
2. **明确 Boss 战的胜利规则**，二选一：
   - 推荐：**首领死亡即胜利**。在 Boss 战结束时清除残余召唤物（或把它们标记为死亡），与“击败首领，本局通关”的文案一致。
   - 或者：仍要求清场，但必须保证所有存活敌人都可见、可选中，并在界面上提示剩余敌人数量与位置。
3. **给 Boss03 增加明确的目标优先级提示**：蒸汽机器存活时操作怪会补召；建议在战斗提示或敌人描述中写明“先摧毁蒸汽机器”。
4. **给调试台增加存活敌人总览**：显示所有 `!Dead` 敌人的名称、站位、生命，包含没有卡片映射的隐藏单位，便于快速确认是否还有存活目标。
5. **补 Boss 战回归测试**：
   - 三个 Boss 分别模拟真实击杀流程，而不是只调用“直接打赢”。
   - 断言 Boss 死亡后（按最终规则）`Outcome == Victory`、`Settlement != null`、`Current == null`。
   - 断言结算后调用 `ContinueBattle` 能进入通关结果页，不会停留在战斗页。

### 验收标准

- `Boss01-复制`、`Boss02-巨型`、`Boss03-组合` 在满足胜利规则后都能正常弹出结算面板。
- 点击“通关结算”后进入本局结果页，显示“击败第 X 层首领，本局通关”，不会卡在战斗页。
- 结算后不再产生任何敌方行动、伤害或召唤事件。
- 若保留“必须清场”的规则，界面上必须能看见并选中每一个存活敌人。
- 新增 Boss 战 PlayMode 回归测试，覆盖至少 Boss01 与 Boss03 的召唤/补召场景。

### 备注

- 本条与 BUG-004 共享根因，建议按“先修尸体与站位映射，再确认 Boss 胜利规则”的顺序处理。
- 若只修改结算界面而不修隐藏存活单位，战斗仍会保持 `Active`，结算面板不会出现，属于治标不治本。
- **修复记录（2026-10-18）**：随 BUG-004 一并修复。存活复制体/补召操作怪现在都会占用可见卡片并参与站位前移，玩家可以正常选中并击杀；所有敌方单位死亡后 `CheckEnd` 会正常返回 `Victory`。Boss 胜利规则仍为“清空所有存活敌人”，未改为“首领死亡即胜利”。待 Unity 内分别验收 Boss01 / Boss02 / Boss03。

---

## BUG-006 事件战斗胜利后结算页「继续」无法结束事件

### 现象

EVT-006「安全角落休整」打赢事件战斗后，战斗结算页只有一个「继续」按钮；点击后结算页不消失，事件也不结束，队伍卡在事件房间。EVT-001 等其它以战斗收尾的事件理论上同样受影响。

### 复现步骤

1. 进入任意以战斗收尾的事件（EVT-001 挖满、EVT-006 守夜后开战）。
2. 打赢战斗，进入结算页。
3. 点击结算页的「继续」按钮。
4. 观察：没有回到事件页，也没有前进到路线，页面保持原样。

### 根因

`IndustrialEventPage.PrepareActions` 会把「事件房间里只有一个『继续／继续前进／离开』选项」折叠成底部的 Continue 按钮。事件战斗期间 `StageOneApp.Room` 仍是 `RoomView.Event`，结算页唯一的「继续前进」被误判为事件收尾选项：

- Continue 按钮的 `Advance()` 走 `Source.ChooseEventOption(0)`，而不是 `current.Choose(0)` → `ContinueBattle()`。
- 此时事件脚本已到 `DoneStep`，`EventScripts.Choose` 直接返回 `false`，只提示「这个选项现在不可用。」
- `Battle` 仍是已结算状态，所以页面继续显示结算视图，形成死循环。

### 修复

- `IndustrialEventPage.PrepareActions` 的折叠条件加入 `Source.Battle == null`：结算页的选项必须作为普通选项卡展示，点击后由 `RoomPages.Settlement` 的 `Choose(0)` 调用 `StageOneApp.ContinueBattle()`。
- 修复日期：2026-10-20。`Assembly-CSharp` 编译通过，待 Unity 内用事件战斗结算页点击验收。

---

## 附录 A：验证方式

- 编译：`dotnet build Ruin.Core.csproj`、`Ruin.Runtime.csproj`、`Ruin.Editor.csproj`、`Ruin.EditModeTests.csproj`、`Ruin.PlayModeTests.csproj`。
- 规则行为：在仓库外建立临时控制台工程，引用 `Ruin.Core.dll`，构造 `RunState` + `BattleEngine`，检查事件序列与 `Current` / `Round` / `Imbalance`。
- 主场景表现：当前环境无法启动 Unity，BUG-001 / BUG-002 / BUG-003 的修复均由调用链、EditMode 测试或独立控制台验证；HUD 卡片、点击交互与动画仍需在 Unity 内人工复核。
- BUG-001 / BUG-002 / BUG-003：修复后五个工程编译通过；独立控制台验证 `imbalance`（Intent 展示、施加日志、免疫日志）、`allSkill`（空目标可提交）与 `guardPull`（守护挡刀仍拖拽主目标）全部通过。
- BUG-004 / BUG-005：已由 `Kill`、`SummonCopies`、`RespawnOperator`、`CheckEnd` 与 Gothic HUD 的站位映射调用链确认存在“尸体与存活单位同站位、界面只显示尸体”的路径；修复后已通过 `Ruin.Core` / `Ruin.Runtime` / `Ruin.Editor` / `Ruin.EditModeTests` / `Ruin.PlayModeTests` 编译，并用独立控制台工程验证“死亡后存活单位前移一格、同回合双死亡连续补位、复制体分别占据 1/2/3 号位、清空全部存活敌人后 `Outcome == Victory` 且生成结算”。仍需在 Unity 内按 Boss01 / Boss02 / Boss03 分别验收结算入口与卡片表现。

## 附录 B：相关代码位置

| 位置 | 说明 |
|---|---|
| `Assets/StageOne/Core/BattleEngine.cs:190-195` | 行动者被选中时的失衡跳过 |
| `Assets/StageOne/Core/BattleEngine.cs:231` | `NeedsTarget` 只认 `SkillTargeting.Choose` |
| `Assets/StageOne/Core/BattleEngine.cs:531-562` | `ApplyOnHit` 施加失衡、免疫反馈与流血/打断 |
| `Assets/StageOne/Core/BattleEngine.cs:563-574` | `TryPull` 的拖拽概率反馈与主目标限定 |
| `Assets/StageOne/Core/BattleEngine.cs:390` | 守护挡刀后仍对主目标执行拖拽 |
| `Assets/StageOne/Core/BattleEngine.cs:709-729` | `PullToFront` 的移动、1 号位与 `Immobile` 反馈 |
| `Assets/StageOne/Core/Models.cs:206` | `ImbalanceImmuneRounds` 默认值 |
| `Assets/StageOne/Resources/StageOne/Skills/g_grapple.asset` | 拖拽射击数值（75% 伤害 / 40% 拖拽） |
| `Assets/MainGame/Runtime/StageOneApp.cs` | `SelectSkill` 第一次点击只记录 `InspectedSkill`；空目标技能再点同一槽位才提交，不可用技能只展示说明；`SelectActor` 提交有目标技能 |
| `Assets/GothicHUD/Runtime/GothicBattleCard.cs:80-102` | 主场景卡片状态行；失衡经由 `Battle.Intent` 显示 |
| `Assets/StageOne/Editor/StageOneBuild.cs:92-166` | 5 个群体技能的定义位置 |
| `Assets/StageOne/Core/BattleEngine.cs:1654` | `Kill` 标记死亡并登记站位释放 |
| `Assets/StageOne/Core/BattleEngine.cs:1453` | `ResolveEnemyRanksAfterDeaths` 让存活敌人前移一格 |
| `Assets/StageOne/Core/BattleEngine.cs:1341` | `SummonCopies` 只按存活单位判断空位 |
| `Assets/StageOne/Core/BattleEngine.cs:1385` | `RespawnOperator` 只按存活单位判断空位 |
| `Assets/StageOne/Core/BattleEngine.cs:1346-1369` | `RespawnOperator` 在操作怪尸体站位补召 |
| `Assets/StageOne/Core/BattleEngine.cs:1632-1637` | `CheckEnd` 以“所有敌方单位死亡”为胜利条件 |
| `Assets/StageOne/Core/BattleEngine.cs:1269-1275` | 每回合开始的 Boss 召唤/补召钩子 |
| `Assets/GothicHUD/Runtime/GothicBattleHudController.cs:284` | 战斗页按 `Rank` 取敌人，未过滤 `Dead` |
| `Assets/MainGame/Runtime/RoomPages.cs` | `Settlement` 仅在 `Outcome != Active` 时生成结算页 |
| `Assets/MainGame/Runtime/StageOneApp.cs` | `ContinueBattle` 与 Boss 通关结算入口 |
