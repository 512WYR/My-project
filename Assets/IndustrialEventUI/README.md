# 工业风事件页面

已接入主场景、StageOne 场景、地图场景及战斗预览场景，使用各场景原有的 StageOneApp 状态。进入任何非战斗房间时自动接替旧页面：事件房、增益房、补给站、电梯井、宝箱房，以及本局结束页，都按事件页的「标题 + 正文 + 选项 + 继续」结构显示。离开后返回地图。

页面内容由 `Ruin.Runtime.RoomPages` 统一生成：`RoomView.Event` 仍读 `EventScripts.Page`，其余房间由 `RoomPages` 把 `StageOneApp` 的既有命令（`ChooseBuff`、`SupplyHeal`、`SupplyCalm`、`RepairLimb`、`InstallProsthetic`、`AdvanceFloor`、`LeaveRoom`、`NewRun`）包装成同一套选项。补给站的「部位与义肢」是页面内的二级菜单（主菜单 → 队员 → 部位），只存在于事件页的视图状态里，不写入游戏存档。

直接运行 `Assets/IndustrialEventUI/Scenes/IndustrialEvent.unity` 可预览“铁门后的敲击声”。正常游戏入口仍为 `Assets/Scenes/mainScene.unity`。

## 布局与交互

- 左上：地图素材改制的横向绿色显示屏，显示事件标题、阶段和位置，保留稀疏的细扫描干扰。
- 左下：逐字显示事件正文、结果和提示，长文本可用滚轮或拖动右侧滚动条阅读。
- 右侧：通用边框制成的长条选项。当前阶段文本全部显示后，选项依次从屏幕右侧滑入，再允许点击。
- 右下：只有继续步骤时显示“继续”，结束叙述播放完后显示该页面的前进按钮（“离开”、“下到第 N 层”、“新开一局”等）。两者均使用缩小的通用边框并从右侧弹出。
- 分支、角色选择、奖励、理智条件和事件战斗继续沿用现有事件规则。
- 房间页在有选项的同时也会显示前进按钮：增益房选完增益后才能离开，补给站可以边处理边离开，其余房间直接离开。

文字默认每秒显示 38 个字，可在 IndustrialEventPage 的 CharactersPerSecond 调整。滑入耗时 0.32 秒，选项错开 0.07 秒。

显示屏当前呈现事件信息；尚未配置事件插图。预制体中保留 EventIllustration 图片位置，可供后续添加插图。

## 文件与维护

预制体：`Assets/IndustrialEventUI/Prefabs/IndustrialEventPage.prefab`。

菜单 `GameMake / Event UI / Build And Apply Event Page` 按布局代码重新生成预制体并应用到各入口场景，重建会覆盖预制体的手动布局调整。原始 PNG 复制到本目录 Art 下，外部素材文件未修改。

运行验证覆盖文本显示与提前点击保护、多阶段事件、条件选项、角色选择、战斗返回事件、继续/离开、长文本滚动及 1920×1080 / 2560×1080 画面。报告与截图位于工程上一级 `交付/事件页面`。
