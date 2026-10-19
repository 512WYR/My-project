# 哥特蒸汽朋克 HUD · 独立素材版

打开 Assets/GothicHUD/Scenes/GothicBattleHUD.unity 编辑。
完整预制体：Prefabs/GothicBattleHUD.prefab；单独回合表：Prefabs/MechanicalRoundCounter.prefab。

本版使用 2026-10-11 提供的 25 张独立透明素材，保留原始 PNG，存于 Art/Modular。数字翻牌继续使用既有 0–9 图集。每个框、按钮、文本、血条填充和图标都是独立对象。

## 场景分区

- Round Counter：机械回合表与独立动态数字。
- Party：四人队伍头像与选择按钮。
- Bottom HUD / Portrait Panel：当前角色的大头像。
- Bottom HUD / Vitals：角色名称、生命和理智。
- Bottom HUD / Limbs：头部、躯干、手臂、腿部，各有正常/破损图标及血条。
- Bottom HUD / Skills：五个技能槽、右侧位移按钮、四个快捷物品槽。
- Bottom HUD / Description：可编辑的技能标题与说明。
- Ally Positions：我方 4 个位置卡，压在我方站位横条正上方。
- Enemy Positions：敌方 4 个位置卡，压在敌方站位横条正上方。
- Debug Button：右上角「调试」按钮，打开调试页。
- Debug Page：默认隐藏的调试页，收纳目前没有落位的信息与功能。它在任何页面都可见。
- ~~Node Pages / Route Page / Room Page / Run End Page~~：已整体删除，预制体不再包含节点页。

Canvas 基准为 1920×1080，顶部居中、队伍靠左上、底栏贴底，Expand 适配宽屏。透明 PNG 直接使用默认 UI 材质。头像与肢体血条在 Sprite Editor 中配置有效裁切区域，原始图片未修改。

## 编辑

在 Hierarchy 展开 GothicBattleHUD，按 T 调整 RectTransform；Image 的 Source Image 替换图片，Text 编辑文字，Fill 的 Fill Amount 控制条长度。数字牌来自“图层 1–5”，可分别替换；使用 Custom Hotkey 文字时请先隐藏对应数字牌图片。

根对象 ModularHudController / Characters 有四个角色数据，展开后可编辑姓名、职业、头像、生命和理智。头像请填入此处的 Portrait，控制器会同步队伍头像和当前角色头像。技能和物品图标默认留空，放入图片后将对应 Image 的 Color Alpha 设为 1。

编辑后 Ctrl+S 保存场景。希望同步预制体时，在根对象 Overrides 中应用需要保留的修改。

## 肢体损坏与恢复

| 顺序 | 部位 | 正常图 | 损坏图 |
|---|---|---|---|
| 0 | Head 头部 | 肢体1 | 肢体破损1 |
| 1 | Torso 躯干 | 肢体 2 | 肢体破损2 |
| 2 | Arm 手臂 | 肢体 3 | 肢体破损3 |
| 3 | Leg 腿部 | 肢体 4 | 肢体破损 4 |

在 Inspector 展开 Characters → Element 0 → Limbs，勾选某个部位的 Broken，或把 Current 设为 0，即可预览破损图。恢复时取消 Broken 并将 Current 设为大于 0。组件右键菜单提供 Break selected character arm、Restore selected character limbs 预览操作。

运行时接入：

```csharp
using GameMake.GothicHUD;

// characterIndex 是 Characters 下标：0–3。
hud.SetLimbHealth(characterIndex, HudLimb.Arm, remaining, maximum);
hud.SetLimbBroken(characterIndex, HudLimb.Leg, true);
hud.SetLimbBroken(characterIndex, HudLimb.Leg, false);
hud.SetVitals(characterIndex, health, maxHealth, sanity);
```

SetLimbHealth 在数值为 0 时换为破损图，大于 0 时恢复正常图。SetLimbBroken(false) 将耗尽的部位恢复至最大值。切换角色会保留各自损伤状态，不会互相影响。

StageOne 战斗模型现在会在敌方攻击命中后结算部位伤害；本 UI 仍只读取 `LimbSystem` 的部位状态并绑定显示，不自行计算伤害。战斗刷新时同步四个部位的血量与断肢标记。

## 回合数字和位移

MechanicalRoundCounter 保留 SetRound(int)、自动读取 StageOneApp.Battle.Round、数字进位和翻动效果。独立 UI 场景没有战斗管理器，不会自行模拟回合；可以修改 Round Number 或通过右键 Next Round 预览。

位移按钮紧邻第五技能槽。点击触发根对象 OnDisplacementRequested；若启用 Follow Stage One Battle 且同场景有 StageOneApp，则调用其现有换位行动，再点击合法相邻队友完成换位。没有战斗管理器时，按钮只发送事件。

GothicHudPanel.OnSkillSelected 提供技能选择事件，技能效果由游戏逻辑绑定。四个快捷物品槽原先通往已删除的道具面板，现在由控制器在运行时隐藏整组 `Items - 快捷物品`；饰品与药剂改在角色面板（点击头像）中处理。

角色名称、生命和理智可自动跟随 StageOneApp。本地预览或接入其他系统时，请关闭 Follow Stage One Battle。

## 接管全部页面：mainScene

GameMake / Gothic HUD / Build mainScene 会生成 Assets/Scenes/mainScene.unity，这是唯一的正式入口。场景中没有代码生成的 UI：战斗页来自 HUD（Canvas 10），路线页来自 `MapExploration`（20），全部非战斗页与战斗结算来自 `IndustrialEventPage`（25），角色面板来自 `CharacterPanel`（45）。验收命令与后台信息由独立的 `IndustrialDebugConsole.prefab` 提供（Canvas 80），入口是屏幕上的「调试台」按钮；不再使用 F1 / F3 / I，也不再有帮助页。

> 生成场景前必须先跑一次 **GameMake / Gothic HUD / Rebuild Modular HUD**：新组件与它们的引用都写在预制体和场景实例上，只重建其中一个不够。
>
> 旧版本生成的预制体若仍带 `Node Pages - 节点页`，运行一次 **GameMake / Gothic HUD / Remove Node Pages - 节点页**，它会删除整棵子树并清掉根节点上失效的 `GothicNodeHudController`。生成器已不再创建这些对象。

数据来源都是 StageOneApp 的 Changed 事件，各控制器只写自己的对象：

| 归属 | 组件 | 说明 |
|---|---|---|
| 生命 / 理智 / 肢体 / 队伍头像 | ModularHudController | Follow Stage One Battle 关闭，由战斗控制器写 Characters 后调用 RefreshView |
| 回合数 | MechanicalRoundCounter | Follow Stage One Battle 关闭，由战斗控制器调用 SetRound |
| 技能槽 / 位移 / 说明 / 位置卡 | GothicBattleHudController | 唯一写入者，只更新已存在对象，不重建层级 |
| 路线页 / 房间选择 | ExplorationMapController | 场景内独立 Canvas，接管 `RoomView.Map` |
| 事件房间 | IndustrialEventPage | 场景内独立 Canvas，接管 `RoomView.Event` |
| 调试台 | IndustrialDebugConsole | 独立 prefab 与 Canvas（排序 80）；屏幕按钮或 HUD 的「调试台」按钮打开，不占用功能键 |

战斗页本身只承担位置、血量、技能三件事，其余信息与命令都收进调试台：

| 区域 | 来源 | 内容 |
|---|---|---|
| 位置卡 ×8 | GothicBattleCard | 剪影、姓名、生命条、生命条下方的理智条、状态行（我方）或敌人意图（敌方）、行动/可选标记、阵亡叉 |
| 血量 | ModularHudController / Vitals | 当前被查看角色的生命与理智 |
| 技能 | ModularHudController / Skills | 五个技能槽、位移键、技能说明 |
| 调试台 | IndustrialDebugConsole | 九个页签：概览、队伍、战斗、地图与房间、事件、部位与义肢、物品与饰品、规则与目录、日志；包含新开演练、玩家/队员理智拖动条与输入框、常态/技术/原始形态按钮、生命、强制房间与事件、战斗结算、义肢与饰品管理 |

位置卡的生命条下方各有一条理智条（`GothicBattleCard.SanityFill / SanityValue`），与 Vitals 区的数值同步；敌方卡同样显示，但敌方理智通常不参与战斗。最左列的 Party 队伍头像栏本次不驱动，保持预制体原样。战斗记录没有放入 UI。

底部右侧的六行属性预留（攻击、防御、速度、闪避、命中、暴击）由 `GothicHudPanel.AttributeValues` 提供，当前由 `IndustrialBattleDetails` 写入选中队员的有效值：攻击、防御、速度取 `BattleEngine.Effective*`（含肢体重伤惩罚与临时增益），命中计入头部重伤与饰品下限，暴击计入必暴饰品；数值为空时显示 `—`。

### 节点页（已移除）

旧版的 `Node Pages - 节点页`（路线页、六个房间页、本局结束页）以及驱动它们的 `GothicNodeHudController`、`GothicNodeOption`、`GothicRouteNode` 已整体删除，`Rebuild Modular HUD` 也不再创建它们。

- 路线与房间选择由场景内的 `ExplorationMapController` 负责。
- 事件房间由场景内的 `IndustrialEventPage` 负责。
- 增益房、补给站、电梯井、宝箱房和本局结束页由 `IndustrialEventPage` 统一按事件页结构显示，内容来自 `Ruin.Runtime.RoomPages`。

mainScene 中的 HUD 实例把 Follow Stage One Battle 关掉，由 `GothicBattleHudController` 单独写入角色、卡面和技能，避免两个控制器争同一个控件。

战场卡点击会选中该角色，选中技能或位移后可作为目标。受击与治疗的闪色由 HUD 自己维护卡片图字典并实现 `StageOneApp.BattleEventAnimation`，不再依赖代码生成的页面。

## 验收与备份

本版已在 Unity 2022.3.62f3c1 中生成场景、预制体和实际渲染图，并通过 Play Mode 验证：

- 回合 01、09、10、99、100 的数字显示和翻动动画。
- 快速连续更新、关闭/重新启用计数器。
- 四类正常/损坏图标切换与恢复。
- 各角色独立肢体状态、血条比例与数值边界。
- 角色选择、技能选择、位移按钮事件。

测试覆盖本 UI 的状态、显示和按钮事件；自动部位伤害由 `BattleEngine.WearLimb` 在规则层结算，UI 只负责刷新。

当前渲染图和检查报告：D:/GameMake/交付/哥特HUD/新版/
旧版完整备份：D:/GameMake/交付/哥特HUD/备份-20261011/GothicHUD-before-modular.zip

GameMake / Gothic HUD / Rebuild Modular HUD 会重建默认场景和预制体，普通编辑不需要运行。重建前请备份自行修改的版本。旧版生成脚本仍保留，也不要运行，否则会恢复旧布局。

## 清晰度预览
已将默认最小文字提高到 14，并开启 Canvas Pixel Perfect。检查细节时，在 Game 选择固定 1920×1080，关闭 Low Resolution Aspect Ratios，并使用 1x 比例。窗口太小时可最大化 Game 视图。菜单 GameMake / Gothic HUD / Clear Preview - 1080p 可设置这些预览选项。聊天中的整图缩略显示也会压缩细节，应打开原尺寸预览判断。

