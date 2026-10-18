# 地图探索页面

正常游戏打开 `Assets/Scenes/mainScene.unity`，点击 Play；它也是 Build Settings 的首个启用场景。独立预览仍可打开 `Assets/MapExplorationUI/Scenes/MapExploration.unity`。

- 左上：楼层与**层内房间序号**，从 01 开始。进入下一层后，房间序号回到 01；数码使用提供的 0～9 图片，并带短暂翻牌动画。
- 左侧：提供的四连角色框。显示实际队员、生命与理智；点击可切换队员选择。人物暂用项目已有剪影，可在每个 `Portrait - Drop Sprite Here` 的 Image 中替换头像。
- 右侧：地图铺满剩余区域。边框用九宫格，网格从原素材切片平铺；路径线和节点为独立 UI 元素。
- 地图左下：按 `Map.Current.Candidates` 显示 1～3 个房间选项，使用提供的通用框。每次进入新位置时淡入上移；点击进入对应的实际房间或战斗。
- 路线只读取已生成的数据，不会提前抽取未知房间，也不会在刷新界面时重新生成候选。

新场景从真实开局开始，所以第一屏只有“初始增益”一个选项。完成增益房后，后续位置按地图规则出现 1～3 个候选。

## 资产与编辑

- `Prefabs/MapExploration.prefab`：地图页面，计数器、角色框、路线、房间选项均可单独编辑。
- `Art/`：本次提供的 14 张原始 PNG 副本，不覆盖 GothicHUD 中的旧素材。
- `ExplorationMapController.Source`：真实的 `StageOneApp`。监听 `Changed` 同步状态，选择房间调用 `SelectNode`。
- `ExplorationCounter.SetLocation(floor, room)`：独立更新数字。至少两位，超过 99 时增加数位，不会截断或回绕。
- `ExplorationMapController.OnCharacterSelected`：角色选中事件。
- 现有规则最多九个层内位置，完整显示；若以后增加房间数，路线显示以当前位置为中心的九格窗口。

新版地图已接入 `mainScene`、旧 `StageOne` 入口、独立地图场景和战斗预览场景。每个场景共用一个 StageOneApp：新版地图负责探索选择，GothicHUD 负责房间、战斗和结算；旧路线页由 `ExternalRouteUi` 关闭。战斗预览仍直接进入战斗，结束后返回新版地图。地图保持横向路线和“未探索”占位。

`GameMake > Map Exploration > Apply To Game Entry Scenes` 可重新接入这些入口；它保留场景其他对象，补齐地图实例与引用，并将 `mainScene` 设为默认启动场景。地图／战斗场景重建器也会保留新版地图接入。

## 清晰度与预览

以 1920×1080 排版，Canvas 开启像素对齐。素材无压缩、不生成 mipmap；中文、数值和说明使用独立动态字体。1080p 下正文 18px、房间标题 24px，角色状态 16px。

在 Unity Game 窗口选择固定 1920×1080，关闭 Low Resolution Aspect Ratios，按 1× 检查实际清晰度。小窗口的缩略显示与放大后的低分辨率预览不能代表原生输出。

预览图和验证记录位于项目上级的 `交付/地图探索UI/`。`MapExploration-3-Choices.png` 是验证时临时设置的三候选状态，保存的场景仍从真实开局开始。

如需重新生成，可用菜单 `GameMake > Map Exploration > Build New Scene`。此操作会重建本目录的场景与 prefab，手动调整后请先另存副本。
