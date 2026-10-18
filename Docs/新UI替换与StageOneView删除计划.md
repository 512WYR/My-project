# 代码生成 UI 移除记录

> 状态：已完成。  
> 正式入口：`Assets/Scenes/mainScene.unity`。  
> 结论：项目中不再有任何由代码生成的正式 UI。

## 1. 背景

`StageOneApp.Start()` 过去会无条件创建 `StageOneView`。`StageOneView.cs` 用代码生成整套 uGUI：路线、增益房、补给站、事件、电梯、宝箱、战斗页，以及结算、帮助、验收工具、饰品管理与道具面板。`mainScene` 中的预制体页面因此一直和这套生成 UI 并存。

本次工作把正式入口的页面全部交给预制体，`StageOneApp` 只保留状态机与数据绑定。

## 2. 最终分层（mainScene）

```text
Camera / EventSystem / StageOne(StageOneApp)
├─ GothicBattleHUD       Canvas 10   战斗页、角色卡、技能、回合数
├─ MapExploration        Canvas 20   路线页、候选人、队伍头像
├─ IndustrialEventPage   Canvas 25   增益/补给/事件/祭坛/电梯/宝箱/战斗结算/本局结束
├─ CharacterPanel        Canvas 45   角色详情、部位与义肢、饰品、药剂
└─ IndustrialDebugConsole Canvas 80 调试台（编辑器与 Development Build）
```

页面可见性只由状态决定：

- 路线页：`AwaitingNode && Room == Map && !Map.Finished`；
- 事件页：`RoomPages.Visible(app)`，其中战斗结算在 `Battle.Outcome != Active` 时也会显示；
- 战斗页：`Battle != null && !AwaitingNode && Battle.Outcome == Active`；
- 角色面板：路线页头像与战斗队伍头像打开。

## 3. 已完成内容

### 3.1 删除的旧 UI

- `StageOneView.cs`：整套代码生成页面与模态。
- `F1` 帮助：帮助文本、快捷键与 `HelpOpen` 全部移除，不再提供帮助页。
- `F3` Laboratory / Event Lab：验收命令统一由 `IndustrialDebugConsole` 提供，入口是屏幕上的「调试台」。
- `I` 道具面板与饰品管理弹窗：游戏内入口移除；饰品与药剂改由角色面板处理，调试台仍可直接操作。
- HUD 的四个快捷物品按钮：运行时隐藏整组 `Items - 快捷物品`，不再接到任何页面。

### 3.2 新增或迁移

- `RoomPages.Settlement`：战斗结算改由 `IndustrialEventPage` 绘制，正文包含每名队员的入战/结束理智、平均净变化 × 反馈系数、玩家理智变化与连续性说明；选项为「继续前进 / 通关结算 / 结束本局」与「新开演练」。结算页 `Instant = true`，跳过逐字显示。
- `CharacterPanelCommands.cs`：把原 `StageOneCharacterPanel` 的后端原样迁到 `StageOneApp` 的 partial 文件，类名暂未改。新的 `Assets/CharacterPanelUI` 未改动。
- 动画桥：`StageOneApp.BattleEventAnimation` 委托；有订阅时由 HUD 实现受击/治疗闪色，没有订阅时回退为等待。
- `Assets/MainGame/Runtime/`：原 `Assets/StageOne/Runtime/` 的运行时代码整体迁移到这里，程序集名仍是 `Ruin.Runtime`，其他 asmdef 引用不变。

### 3.3 入口与场景

- `Assets/StageOne/Scenes/StageOne.unity` 已删除。
- `ProjectSettings/EditorBuildSettings.asset` 与构建脚本只保留 `mainScene`。
- `GameMake / Gothic HUD / Build mainScene` 现在会一并挂接路线页、事件页、角色面板与调试台。
- 修复了 `mainScene` 中 HUD 与调试台实例的零缩放覆盖；调试台实例位于 `SceneRoots`。
- 场景文件中的 `ExternalBattleUi` 字段已随字段删除一并清理。

## 4. 当前约定

- 不再新增 `StageOne` 命名的文件或目录；新内容放在 `Assets/MainGame/` 等新目录。
- `StageOneApp` 的类名全局重命名留到后续单独处理。
- 验收与后台信息统一走「调试台」；不再使用特殊功能键。
- BattleLab 的 IMGUI 调试场景保持原样，不在正式 UI 范围内。

## 5. 关键文件

- `Assets/MainGame/Runtime/StageOneApp.cs`
- `Assets/MainGame/Runtime/RoomPages.cs`
- `Assets/MainGame/Runtime/CharacterPanelCommands.cs`
- `Assets/GothicHUD/Runtime/GothicBattleHudController.cs`
- `Assets/IndustrialEventUI/Runtime/IndustrialEventPage.cs`
- `Assets/MapExplorationUI/Runtime/ExplorationMapController.cs`
- `Assets/IndustrialDebugUI/Runtime/IndustrialDebugConsole.cs`
- `Assets/Scenes/mainScene.unity`
- `Assets/GothicHUD/Editor/ModularSceneBuilder.cs`
- `Assets/StageOne/Editor/StageOneBuild.cs`
