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
- Node Pages / Route Page：默认隐藏的路线页，候选节点竖直排列。
- Node Pages / Room Page：默认隐藏的房间页，通用边框 + 配图块 + 文字块 + 选项块 + 右下角继续按钮。
- Node Pages / Run End Page：默认隐藏的本局结束页。

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

现有 StageOne 战斗模型没有肢体伤害字段，因此本次实现独立的肢体状态层及 UI 绑定。在后续肢体伤害结算处调用上述接口即可；普通生命伤害不会自行导致某一部位损坏。

## 回合数字和位移

MechanicalRoundCounter 保留 SetRound(int)、自动读取 StageOneApp.Battle.Round、数字进位和翻动效果。独立 UI 场景没有战斗管理器，不会自行模拟回合；可以修改 Round Number 或通过右键 Next Round 预览。

位移按钮紧邻第五技能槽。点击触发根对象 OnDisplacementRequested；若启用 Follow Stage One Battle 且同场景有 StageOneApp，则调用其现有换位行动，再点击合法相邻队友完成换位。没有战斗管理器时，按钮只发送事件。

GothicHudPanel.OnSkillSelected 提供技能选择事件，技能效果由游戏逻辑绑定。四个快捷物品槽是可编辑按钮，物品使用逻辑可通过 Button.OnClick 接入。

角色名称、生命和理智可自动跟随 StageOneApp。本地预览或接入其他系统时，请关闭 Follow Stage One Battle。

## 接管全部页面：mainScene

GameMake / Gothic HUD / Build mainScene 会生成 Assets/Scenes/mainScene.unity。该场景把 StageOneApp 的 External Battle Ui 打开，于是 StageOneView 不再绘制任何页面——战斗页与节点页全部让出，只保留结算弹窗、F1 帮助和 F3 验收工具；它的 Canvas 排序为 50，所以弹窗仍盖在 HUD（排序 10）之上。

> 生成场景前必须先跑一次 **GameMake / Gothic HUD / Rebuild Modular HUD**：新组件与它们的引用都写在预制体和场景实例上，只重建其中一个不够。

数据来源都是 StageOneApp 的 Changed 事件，两个控制器各管一半，互不写对方的对象：

| 归属 | 组件 | 说明 |
|---|---|---|
| 生命 / 理智 / 肢体 / 队伍头像 | ModularHudController | Follow Stage One Battle 关闭，由战斗控制器写 Characters 后调用 RefreshView |
| 回合数 | MechanicalRoundCounter | Follow Stage One Battle 关闭，由战斗控制器调用 SetRound |
| 技能槽 / 位移 / 说明 / 位置卡 | GothicBattleHudController | 唯一写入者，只更新已存在对象，不重建层级 |
| 路线页 / 六个房间页 / 本局结束页 | GothicNodeHudController | 唯一写入者，同样只更新已存在对象 |
| 调试页 | GothicBattleHudController | 唯一写入者；「调试」按钮在所有页面都可见，战斗外的行显示为「—」 |

战斗页本身只承担位置、血量、技能三件事，其余都收进调试页：

| 区域 | 来源 | 内容 |
|---|---|---|
| 位置卡 ×8 | GothicBattleCard | 剪影、姓名、生命条、状态行（我方）或敌人意图（敌方）、行动/可选标记、阵亡叉 |
| 血量 | ModularHudController / Vitals | 当前被查看角色的生命与理智 |
| 技能 | ModularHudController / Skills | 五个技能槽、位移键、技能说明 |
| 调试页 | GothicBattleHudController | 玩家理智、层数/场次/回合、当前行动者、提示、敌人意图、帮助 F1、验收 F3、新开演练 |

位置卡不显示理智，理智看 Vitals 区。最左列的 Party 队伍头像栏本次不驱动，保持预制体原样。战斗记录没有放入 UI。

### 节点页

节点页与战斗页由 `AwaitingNode` 天然互斥：一边显示时另一边整组隐藏。每个节点页共用同一套骨架，边框一律是「通用边框」（GeneralFrame）：

```
┌ 通用边框 ────────────────────────────────────────────┐
│  房间名 / 标题            [帮助 F1] [验收 F3]        │
│ ┌ 配图块 ┐  ┌ 文字块：标题 / 正文 / 结果 ────────┐   │
│ └────────┘  └────────────────────────────────────┘   │
│ ┌ 选项块：选项卡（按数量居中） ────────────────────┐ │
│ └──────────────────────────────────────────────────┘ │
│  提示文字                                [ 继续 ] ←──┘
└──────────────────────────────────────────────────────┘
```

- **配图块**：EventSpec 目前没有图片字段，所以每页都是带标注的占位框，边框用节点类型色描边。将来给 EventSpec 加 Sprite 时，这里是唯一的填充点。
- **继续按钮**：固定放在窗口右下角同一个位置，承担「节点做完 → 回到路线选下一个节点」。事件房/增益房/补给站/宝箱房是「离开」，电梯井是「下楼」，本局结束页是「新开一局」。将来若有窗口要关闭按钮，放同一处即可。按钮左侧那行提示文字说明它为什么暂时不可点。
- **路线页**：内容与内置页一致，只有候选节点由横向一排改成**竖直排列**（右侧一列）。左侧是概率行与已经过的位置。点候选卡本身就是前进，所以路线页没有继续按钮。
- **补给站**：治疗全队与静养进了选项块；「按角色 × 按部位」的修复与安装义肢塞不进这套骨架，先用文字占位——**这几项在 mainScene 里暂时不可达**（StageOne.unity 的内置补给页不受影响）。义肢仍可用 F3 验收工具发放。
- **电梯井 / 宝箱房**：没有选项，选项块显示说明文字，继续位分别是「下楼」与「离开」。

保留 Follow Stage One Battle 为默认值的原因为：StageOne.unity 与该场景的自动化测试（StageOneSmoke、StageOneFlowTests）都走内置页面，External Battle Ui 默认关闭，行为不变（该开关现在覆盖整页，但取值和默认值没变）。

战场卡点击会选中该角色，选中技能或位移后可作为目标；`StageOneView.RegisterCardImage` 让打击闪红在 HUD 上照常工作。

## 验收与备份

本版已在 Unity 2022.3.62f3c1 中生成场景、预制体和实际渲染图，并通过 Play Mode 验证：

- 回合 01、09、10、99、100 的数字显示和翻动动画。
- 快速连续更新、关闭/重新启用计数器。
- 四类正常/损坏图标切换与恢复。
- 各角色独立肢体状态、血条比例与数值边界。
- 角色选择、技能选择、位移按钮事件。

测试覆盖本 UI 的状态、显示和按钮事件；未模拟完整 StageOne 战斗中自动结算的肢体伤害，因为现有战斗系统尚无该机制。

当前渲染图和检查报告：D:/GameMake/交付/哥特HUD/新版/
旧版完整备份：D:/GameMake/交付/哥特HUD/备份-20261011/GothicHUD-before-modular.zip

GameMake / Gothic HUD / Rebuild Modular HUD 会重建默认场景和预制体，普通编辑不需要运行。重建前请备份自行修改的版本。旧版生成脚本仍保留，也不要运行，否则会恢复旧布局。

## 清晰度预览
已将默认最小文字提高到 14，并开启 Canvas Pixel Perfect。检查细节时，在 Game 选择固定 1920×1080，关闭 Low Resolution Aspect Ratios，并使用 1x 比例。窗口太小时可最大化 Game 视图。菜单 GameMake / Gothic HUD / Clear Preview - 1080p 可设置这些预览选项。聊天中的整图缩略显示也会压缩细节，应打开原尺寸预览判断。

