# 调试验收台（Industrial Debug Console）

`Assets/IndustrialDebugUI` 是 mainScene 使用的调试验收 UI。它是一个**在编辑器里生成并保存的 prefab**，运行时只写文本、切换按钮状态、把点击转发给 `StageOneApp`，不会在游戏里创建任何界面对象。

- 入口：屏幕顶部中间的「调试台 · DEBUG」按钮；Gothic HUD 右上角的「调试台」按钮也会打开同一个控制台。
- 关闭：右上角「关闭」或 `Esc`。
- **不再使用 F1 / F3 / I 等特殊按键。**
- Canvas：Screen Space - Overlay，排序 80，参考分辨率 1920 × 1080。

## 文件

| 路径 | 作用 |
|---|---|
| `Runtime/IndustrialDebugConsole.cs` | 运行时控制器；绑定 `StageOneApp`，写文本并接命令。Release 构建下自动关闭。 |
| `Editor/IndustrialDebugConsoleBuilder.cs` | 编辑器内生成 prefab 的全部布局。手工修改 prefab 会在重建时被覆盖，永久改动请改这里。 |
| `Editor/IndustrialDebugConsoleInstaller.cs` | 自动/手动把 prefab 放进 `Assets/Scenes/mainScene.unity`，并修复 HUD 根缩放。 |
| `Prefabs/IndustrialDebugConsole.prefab` | 生成产物，由 mainScene 引用。 |

## 页签与功能

| 页签 | 内容 |
|---|---|
| 概览 Overview | 当前页面、种子、玩家理智、层/步/已通过、补给与战斗场次、初始增益、房间记录；全队生命/理智/精神/部位；义肢、饰品、道具库存；当前提示与房间/路线/本局记录；内置操作说明。 |
| 队伍 Party | 选择队员；完整属性、形态、部位、义肢、饰品、战斗状态、当前形态技能与剩余次数；队员理智 15/50/85、凝神/躁动/清除状态、恢复满血/设为濒死；**玩家理智 15/50/85/100**；**新开演练（默认种子 / 理智 15 / 50 / 85）**。 |
| 战斗 Battle | 当前战斗/回合/行动者、行动顺序、双方生命与状态、敌方意图、部位修正后的攻防速命、最近事件、结算明细、战斗日志；直接胜利、撤退结算、整备生命、濒死演练、反馈样例；选中队员的理智与精神状态。 |
| 地图与房间 Map | 当前层/步、本层采样、候选与选择、完整路线；跳到第 1/2/3 层、跳到本层末端、跳到首领层；强制进入补给站、事件房、祭坛房、宝箱房、电梯井、普通/精英/首领战、初始增益房。 |
| 事件 Events | 当前事件的脚本、步骤、次数、标志、正文、提示与选项；强制进入 `EVT-001`～`EVT-008`、`EVT-H001`（高理智）、`EVT-L001`（低理智）；事件战斗直接打赢或撤退。 |
| 部位与义肢 Limbs | 选中队员四个部位的状态与生命、义肢库存、四项部位系数；击伤头/躯干/手臂/腿，断肢手臂/腿，修复重伤，安装仓库中的义肢。 |
| 物品与饰品 Items | 三类库存明细；选中队员已装备的饰品；装备库存前 8 件、卸下前 6 件、对选中队员使用前 6 件道具；一键发放全部义肢/饰品/道具。 |
| 规则与目录 Rules | 地图规则、当前理智下的房间概率、补给与精英/首领参数、战斗规则、精神形态倍率、部位规则、角色目录、事件目录、饰品/道具/义肢目录。 |
| 日志 Log | 最近的提示历史（最多 80 条）与最近 60 条战斗事件，按「新 → 旧」排列；可清空。 |

## 重建与安装

菜单：

- `GameMake / Debug Console / 重建调试台 Prefab` — 重新生成 prefab。
- `GameMake / Debug Console / 重建并安装到 mainScene` — 重新生成并安装。
- `GameMake / Debug Console / 安装到 mainScene` — 只安装。
- `GameMake / Debug Console / 修复 HUD 根缩放` — 把 `GothicBattleHUD.prefab` 根节点的零缩放恢复为 1。

安装是幂等的。编辑器每次域重载后会自动检查：

1. prefab 不存在，或 `IndustrialDebugConsoleBuilder.Version` 比 `EditorPrefs` 中记录的版本新 → 重建 prefab。
2. mainScene 中没有该 prefab 实例 → 加为场景根对象。
3. 场景原本是干净的 → 立即保存场景；场景已有未保存修改 → 只标记 dirty，并在 Console 提示按 `Ctrl+S`，不会覆盖用户正在编辑的内容。

修改布局或序列化字段后，请把 `IndustrialDebugConsoleBuilder.Version` 加一，这样其他机器打开工程时也会自动重建。

## 运行时约定

- `Source` 为空时会在场景里查找 `StageOneApp`；两者必须在同一场景。
- 所有命令通过 `StageOneApp.Debug*` 方法执行，最终都会 `Refresh()`，因此调试台、Gothic HUD 和旧 UI 读到的是同一份状态。
- 控制台打开时会把 `StageOneApp.DebugOverlayOpen` 设为 true，并消费当帧的 `Esc`，避免同时清掉战斗中的待选目标。
- 只在 `UNITY_EDITOR || DEVELOPMENT_BUILD` 下编译完整实现；正式包中的占位实现会在 `Awake` 关闭对象，prefab 可以继续留在场景里。
