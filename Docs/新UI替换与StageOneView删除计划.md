# 新 UI 替换与 StageOneView 删除计划

> 状态：规划文档，尚未实施。  
> 目标入口场景：`Assets/Scenes/mainScene.unity`。  
> 目标 UI：`Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab` + 现有 Gothic HUD 控制器。

## 1. 背景

当前 `StageOneApp.Start()` 会无条件创建 `StageOneView`：

```csharp
View = gameObject.AddComponent<StageOneView>();
View.Initialize(this);
```

`StageOneView.cs`（967 行）用代码生成全部 UGUI 页面，包括路线、增益房、补给站、事件、电梯、宝箱、战斗页，以及四个模态：结算、帮助、验收工具、饰品管理。

`mainScene.unity` 中已经有一套场景手摆的新 UI：`GothicBattleHUD` 预制体实例，由 `GothicBattleHudController` 和 `GothicNodeHudController` 订阅 `StageOneApp.Changed` 后驱动。目前它接管了路线页、房间页和战斗页，但仍依赖 StageOneView 绘制模态；自身也缺少若干页面内容，导致 mainScene 在一场战斗结束后无法继续。

本计划分两个阶段：

1. **先补齐新 UI**，让 mainScene 在不依赖 StageOneView 页面的情况下完整可玩；
2. **再删除 StageOneView.cs**，让 `StageOneApp` 只通过 `Changed` 事件和动画委托与 UI 通信。

## 2. 约束与前提

- 继续使用 uGUI，不迁移 UI Toolkit，也不引入 TextMeshPro。
- 用户当前是手工编辑 prefab/场景，因此**不要重跑** `GameMake/Gothic HUD/Rebuild Modular HUD`，否则会覆盖手工改动。若需要可复现，应把改动同步进 `ModularHudBuilder`。
- `Ruin.Runtime` 不能引用 `Assembly-CSharp` 中的 GothicHUD 类型。`StageOneApp` 与 UI 之间只能通过委托、事件或基础 Unity 类型解耦。
- 测试与冒烟暂不纳入本次目标，但 Runtime 代码必须保持可编译。
- 正式运行入口改为 `Assets/Scenes/mainScene.unity`。

## 3. 阶段 A：补齐新 UI（保留 StageOneView 可运行）

### A0. 先修阻塞项：HUD 不可见

`Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab` 根 RectTransform 的 `m_LocalScale` 当前为 `{0,0,0}`，而 `mainScene.unity` 中的预制体实例没有 scale override，会导致整个 HUD 缩放为 0、不可见也不可点击。

处理：

- 把预制体根节点 scale 改为 `(1,1,1)`。
- 在 `ModularSceneBuilder.Build()` 与 `ModularHudBuilder` 的保存路径上保留或补上断言，避免再次产出零缩放 prefab。
- 验收：Play mainScene，Route Page 可见，候选按钮可点击。

### A1. 补齐节点/房间页：`GothicNodeHudController.cs`

#### A1.1 补给站：部位与义肢子页

当前补给站只有“治疗全队”和“静养”，修复部位/安装义肢仍是占位文字。

复用接口：

- `LimbSystem.Get(actor, part)`
- `LimbSystem.ConditionOf(limb)`
- `LimbSystem.PartName(part)` / `LimbSystem.ConditionName(condition)`
- `LimbSystem.CanRepair(actor, part)` / `LimbSystem.CanInstall(actor, item)`
- `LimbSystem.Summary(actor)`
- `app.Run.Prosthetics`
- `app.RepairLimb(heroId, part)`
- `app.InstallProsthetic(heroId, itemId)`

建议的预制体结构（放在 `Room Page - 房间页` 下，默认 inactive）：

```text
Supply Limb Page - 部位与义肢
  Limb Scrim
  Limb Panel
    Limb Title
    Limb Note
    Limb Stock
    Limb Back
    Limb Hero 1..4
      Hero Name
      Hero State
      Hero Note
      Head Cell / Torso Cell / Arm Cell / Leg Cell
        Part Name
        Part State
        Repair Button
        Install Button
        Missing Note
```

行为：

- `SyncSupplyRoom` 增加第 3 个选项“部位与义肢 / 修复重伤、安装义肢”。
- `SelectOption` 的 `RoomView.Supply` 分支在索引 3 时打开子页。
- 子页打开时隐藏选项区；返回时关闭子页并恢复房间页。
- 更新补给站正文，删除“本页暂未接入”的旧说明。
- 部位处于重伤时显示“修复”；断肢时显示库存中对应部位的义肢安装按钮；没有义肢时显示“缺少该部位的义肢”。

#### A1.2 宝箱房：饰品入口

- `SyncTreasureRoom` 使用 `Options[0]` 显示“管理饰品 · 持有 N 件”，点击调用 `app.OpenAccessoryEditor()`。
- 正文改为说明宝箱发放饰品，并显示 `app.RoomNote`、义肢/饰品库存与玩家理智。
- 离开仍走 `app.LeaveRoom()`。

#### A1.3 路线页：饰品入口

- 在 `Route Page - 路线页` 增加 `Route Accessory Button`，显示“饰品 N”。
- 点击调用 `app.OpenAccessoryEditor()`。
- `SyncRoute` 中刷新数量。

### A2. 补齐战斗页：`GothicBattleHudController.cs`

#### A2.1 战斗日志

- 新增 `JournalRoot` 与 3 条 `Journal Entry` 文本，并加入 `ChromeRoots`，非战斗时自动隐藏。
- 数据取 `app.Battle.Events` 最近 3 条 `Message`。
- 按 `BattleEvent.Kind` 上色：
  - `damage` / `death` → Rust
  - `heal` / `sanity` → Cyan
  - `limb` → Gold
  - 其余 → Muted

#### A2.2 结算页（阻塞项）

战斗结束后 `Outcome != Active`，现有 `Sync()` 的 `live` 判断会隐藏整个 HUD，导致 mainScene 卡住。必须新增结算页。

建议结构：

```text
Settlement Page - 结算页
  Settlement Scrim
  Settlement Panel
    ResultHeading
    ResultSubtitle
    ResultColumns
    ResultRule
    Result Row 1..4
      ResultName
      ResultStart
      ResultEnd
      ResultDelta
    Formula
    PlayerResult
    Continuity
    ContinueBattle
    ResultNewRun
```

数据来源：

- `app.Battle.Settlement`
- `BattleSettlement.Outcome / Entries / Before / RawDelta / After / AppliedDelta`
- `FeedbackEntry.Name / Start / End / Dead / Delta`
- `app.ContinueBattle()` / `app.NewRun()`

关键点：

- **`Sync()` 中必须先同步结算页，再执行 `if (!live) return;`**，否则结算页永远不会出现。
- 结算页不要放进 `ChromeRoots`。
- 文案从 `StageOneView.Settlement()` 逐字移植。
- `ContinueBattle` 按钮在队伍全灭或首领战失败时显示“结束本局”，首领战胜利时显示“通关结算”，否则显示“继续前进”。

### A3. 新建模态控制器：`Assets/GothicHUD/Runtime/GothicModalHudController.cs`

新组件挂到 HUD 根，订阅 `app.Changed`，只做 `SetActive` 与文本写入，不回调 `Refresh()`，避免循环。

在预制体根新增最后一个子节点：

```text
Modal Layer - 模态层
```

默认 inactive，承载以下四个模态。

#### A3.1 Help

- 显示条件：`app.HelpOpen`
- 正文从 `StageOneView.Help()` 移植。
- 关闭调用 `app.ToggleHelp()`。

#### A3.2 Laboratory

- 显示条件：`app.LabOpen && !app.LabEvents`
- 按钮与命令逐项对应现有 `StageOneView.Laboratory()`：

| 按钮 | 命令 |
| --- | --- |
| `Preset15` / `Preset50` / `Preset85` | `app.NewRun(15 / 50 / 85)` |
| `Set15` / `Set50` / `Set85` | `app.DebugSetSanity(...)` |
| `SetComposed` / `SetAgitated` / `ClearMental` | `app.DebugMental(MentalState.Composed / Agitated / None)` |
| `FeedbackExample` | `app.DebugFeedbackExample()` |
| `DeathDoorExample` | `app.DebugDeathDoor()` |
| `EndBattleDebug` | `app.DebugSettle()` |
| `RestParty` | `app.DebugRestParty()` |
| `SkipFloor` | `app.DebugSkipToFloorEnd()` |
| `JumpBoss` | `app.DebugEnterFloor(app.Catalog.MapRules().Floors)` |
| `ForceSupply` / `ForceEvent` / `ForceAltar` / `ForceTreasure` / `ForceElevator` | `app.DebugForceRoom(NodeType.*)` |
| `LabEvents` | `app.ToggleLabEvents()` |
| `InjureHead` / `InjureTorso` / `InjureArm` / `InjureLeg` | `app.DebugInjureLimb(LimbPart.*)` |
| `SeverArm` / `SeverLeg` | `app.DebugSeverLimb(LimbPart.*)` |
| `GrantAllProsthetics` | `app.DebugGrantAllProsthetics()` |
| `GrantAllAccessories` | `app.DebugGrantAllAccessories()` |
| `LabAccessories` | `app.OpenAccessoryEditor()` |
| `CloseLab` | `app.ToggleLab()` |

#### A3.3 Event Lab

- 显示条件：`app.LabOpen && app.LabEvents`
- 事件按钮：
  - `Force001..Force008` → `app.DebugForceEvent("EVT-001".."EVT-008")`
  - `ForceH001` → `app.DebugForceEvent("EVT-H001")`
  - `ForceL001` → `app.DebugForceEvent("EVT-L001")`
- `LabWinBattle` → `app.DebugWinBattle()`
- `LabRetreatBattle` → `app.DebugSettle()`
- `CloseEventLab` → `app.ToggleLabEvents()`

#### A3.4 Accessory Editor

- 显示条件：`app.AccessoryEditorOpen`
- 4 个角色页签、已装备 6 行、仓库 6 行。
- 复用：
  - `AccessorySystem.Summary`
  - `AccessorySystem.ShortSummary`
  - `AccessorySystem.RestrictionText`
  - `AccessorySystem.EquipBlockedReason`
  - `app.AccessoryHeroId`
  - `app.SelectAccessoryHero(heroId)`
  - `app.EquipAccessory(heroId, stockIndex)`
  - `app.UnequipAccessory(heroId, itemId)`
  - `app.CloseAccessoryEditor()`
- 超出 6 行时显示“另有 N 件未显示”。

同时给 `GothicBattleHudController.Sync()` 和 `GothicNodeHudController.Sync()` 的显示判断加入 `!app.AccessoryEditorOpen`，与 Help/Lab 的行为保持一致。

### A4. 检查点：关闭 mainScene 中的旧模态

在 `StageOneView.Render()` 的 `if (app.ExternalBattleUi)` 分支中，移除 `Settlement`、`Help`、`Laboratory`、`AccessoryEditor` 四个调用，直接 `return;`。

此时：

- `mainScene` 的全部页面与模态都应由新 UI 提供；
- `StageOne.unity` 的 `ExternalBattleUi == false`，旧 UI 仍完整可玩。

手工跑通一整局后，再进入阶段 B。

## 4. 阶段 B：删除 StageOneView 并解耦 StageOneApp

### B1. `Assets/StageOne/Runtime/StageOneApp.cs`

删除：

- `public StageOneView View { get; private set; }`
- `Start()` 中的 `AddComponent<StageOneView>()` 与 `View.Initialize(this)`
- `public bool ExternalBattleUi;` 及其注释
- `Refresh()` 中的 `View.Render()`，改为：

```csharp
public void Refresh() { Changed?.Invoke(); }
```

新增动画委托（不引用任何 Gothic 类型）：

```csharp
public delegate IEnumerator BattleEventAnimator(BattleEvent[] events, bool fast);

public void SetBattleAnimator(BattleEventAnimator animator);
public void ClearBattleAnimator(BattleEventAnimator animator);
private IEnumerator AnimateBattleEvents(BattleEvent[] events);
```

行为：

- 有注册动画器时：`yield return animator(events, AutomationMode)`。
- 没有动画器时：按旧 `StageOneView.AnimateEvents` 的节奏回退：
  - 有 `damage` / `heal` 事件时，每条等待 `AutomationMode ? 0.01f : 0.18f`；
  - 没有命中事件时，等待 `AutomationMode ? 0.01f : 0.12f`。
- `ContinueEnemies()` 中两处 `View.AnimateEvents(...)` 改为 `yield return AnimateBattleEvents(...)`。

### B2. `Assets/GothicHUD/Runtime/GothicBattleHudController.cs`

- 新增私有 `Dictionary<string, Image> cardImages`。
- `SyncStage()` 中直接写入己方/敌方卡背景，不再调用 `app.View.RegisterCardImage`。
- 实现：

```csharp
public IEnumerator AnimateEvents(BattleEvent[] events, bool fast)
```

只处理 `damage` / `heal`：按 `TargetId` 找图改色，等待后恢复；无命中时等待 0.01/0.12 秒。

- `Bind()` 时 `bound.SetBattleAnimator(AnimateEvents)`。
- `OnDisable()` 时 `bound.ClearBattleAnimator(AnimateEvents)`。

### B3. 最小可编译处理

- `Assets/StageOne/Runtime/StageOneSmoke.cs`：
  - 两处 `app.View.InterfaceFont` 改为从场景内已启用 `Text.font` 取字体；
  - 取不到时使用 `Font.CreateDynamicFontFromOSFont(...)` 兜底；
  - 保留旧按钮名逻辑，本次不修行为。
- `Assets/GothicHUD/Editor/ModularSceneBuilder.cs`：删除 `app.ExternalBattleUi = true;` 及相关注释。
- `Assets/StageOne/Tests/PlayMode/StageOneFlowTests.cs`：不引用 View 类型，可以编译，不改；运行会失败，记录为后续重写。
- 不再传入 `--stage1-smoke`。

### B4. 删除文件

- 删除 `Assets/StageOne/Runtime/StageOneView.cs`。
- 删除 `Assets/StageOne/Runtime/StageOneView.cs.meta`。
- **保留** `Assets/StageOne/Runtime/SilhouetteGraphic.cs`，`GothicBattleCard` 仍在使用。
- 全仓搜索确认无以下残留：
  - `app.View`
  - `View.AnimateEvents`
  - `RegisterCardImage`
  - `ExternalBattleUi`

## 5. 阶段 C：场景、构建、文档收尾

### C1. 入口场景

`Assets/StageOne/Editor/StageOneBuild.cs`：

- 新增 `MainScenePath = "Assets/Scenes/mainScene.unity"`。
- `Prepare()` 不再把 `EditorBuildSettings.scenes` 覆盖成只有 StageOne.unity；改为 mainScene 优先，不存在时回退 StageOne.unity。
- `BuildWindows()` 的 `BuildPlayerOptions.scenes` 使用 mainScene。

### C2. ModularSceneBuilder

- 保留“先 `StageOneBuild.Prepare()`，再创建 mainScene，最后把 mainScene 去重并放到构建列表首位”的顺序。
- 增加 `GothicModalHudController.Source = app` 的接线。
- 保留 `hud.transform.localScale = Vector3.one`。

### C3. StageOne.unity

- 标记为 legacy，不再作为入口或构建场景。
- 删除 StageOneView 后，该场景没有 UI，也没有 EventSystem，不再可玩。

### C4. 文档

- `Assets/GothicHUD/README.md`：删除 StageOneView、ExternalBattleUi、旧模态相关描述。
- `Docs/代码结构与功能分析.md`：更新 UI 架构说明，改为“StageOneApp 只发 `Changed`，Gothic HUD 自持所有页面与模态”。

## 6. 验证清单

1. mainScene Play：Route Page 可见；候选、概率、已通过位置正常。
2. 初始增益房 → 选择增益 → 离开 → 路线。
3. 战斗：技能、换位、目标高亮正常；受伤/治疗时卡片闪色；日志显示 3 条；敌方回合自动推进。
4. F3 → `EndBattleDebug` → 结算页出现；`ContinueBattle` 回到路线；`ResultNewRun` 重开。
5. F1 打开/关闭 Help。
6. F3 打开 Lab、进入 Event Lab、`Force001`、`LabWinBattle`、返回；从 Lab 打开饰品管理。
7. F3 → `ForceSupply`：治疗、静养、管理饰品；先 `GrantAllProsthetics` + `SeverArm`，再验证修复/安装；离开。
8. F3 → `ForceTreasure`：显示 `RoomNote` 与库存，管理饰品可打开；离开。
9. 电梯井 → 下一层；`JumpBoss` → 首领战 → 通关/结束页 → 新开演练。
10. 1280×720 与 1920×1080 各检查一次文本换行与遮挡。
11. `GameMake/Build Stage One Windows` 构建后，exe 首场景为 mainScene，完整走一遍。
12. Unity 控制台无错误；无 View / ExternalBattleUi 引用残留。

## 7. 风险清单

1. **预制体被 builder 覆盖**：重跑 `Rebuild Modular HUD` 会丢失手工改动。  
   处理：本阶段不重跑；若要重跑，先把改动同步进 `ModularHudBuilder`。
2. **结算页被 `Sync()` 早退隐藏**：必须把结算同步放在 `live` 判断之前。
3. **零 scale 复发**：builder 重跑或场景重建可能再次产生不可见 HUD；加断言。
4. **跨程序集引用**：`StageOneApp` 不得出现 `GameMake.GothicHUD` 类型；动画只能通过委托。
5. **StageOneView 删除后旧场景无 UI**：StageOne.unity 必须退役，或补 HUD 与 EventSystem。
6. **UI 层级**：`Modal Layer` 必须是最后一个根子节点；结算页放在 Debug Page 之后、Modal Layer 之前，否则 Help/Lab 会被压住。
7. **文本溢出与字体缺字**：新模态移植旧文案后，在 1280×720 与 1920×1080 下逐页检查。
8. **重复绑定**：新按钮通过控制器运行时绑定，不要在 prefab 中同时加 persistent onClick，避免双触发。
9. **`ExternalBattleUi` 残留序列化**：字段删除后场景中的值会被 Unity 忽略，重新保存场景即可清理。

## 8. 关键文件

- `Assets/StageOne/Runtime/StageOneApp.cs`
- `Assets/GothicHUD/Runtime/GothicBattleHudController.cs`
- `Assets/GothicHUD/Runtime/GothicNodeHudController.cs`
- `Assets/GothicHUD/Runtime/GothicModalHudController.cs`（新建）
- `Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab`
- `Assets/Scenes/mainScene.unity`
- `Assets/StageOne/Runtime/StageOneSmoke.cs`
- `Assets/StageOne/Editor/StageOneBuild.cs`
- `Assets/GothicHUD/Editor/ModularSceneBuilder.cs`
