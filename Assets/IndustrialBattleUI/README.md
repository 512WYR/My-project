# 工业风战斗界面

本次以新素材重建原战斗 prefab：`Assets/GothicHUD/Prefabs/GothicBattleHUD.prefab`。原 `mainScene` 和地图探索场景通过该 prefab 自动使用新版战斗页，地图路线和房间页不改。

打开 `Assets/GothicHUD/Scenes/GothicBattleHUD.unity`，Play 后直接进入一场真实战斗。这个场景的 `Battle Preview Entry` 仅用于直接预览战斗，正常主场景仍从探索流程开始。

## 可编辑模块

- 顶部：新机械计数框、ROUND 牌、0～9 数字贴图与轮盘。回合数字随实际战斗变化。
- 左侧：四连角色框，点击查看不同队员。
- 中部：四个我方站位与敌方站位，头像框、生命条、行动／目标标记、敌人意图。
- 左下：新角色血条面板，包含头像、生命、理智和四个独立肢体图标／血条；正常、受伤和断肢状态使用现有规则同步。
- 生命、理智填充条缩短为 292px，肢体图标间距收紧至 6px；右侧预留攻击、防御、速度、闪避、命中、暴击六行属性，数值暂为“—”，尚未绑定计算。面板边框和状态符号由原图切分成独立 Sprite，源 PNG 不变。
- 底部操作区高 360px，内容上下留约 44px、左右留约 46px，避免覆盖外框花纹；内部模块缩放至 97%。理智条为淡灰色（#CDCDCD），数值加深色描边。顶部计数表保持 78% 缩放；两侧横杠各延长约 125 屏幕像素，中间数字框、齿轮、连接件和箭头保持原比例，保留回合数字和齿轮动画。
- 中下：技能放置栏、五个技能按钮与新数字牌、独立位移按钮、已有物品占位槽。
- 右下：绿色技能介绍屏，文字为单独的 Text 对象。

人物暂用项目已有剪影；素材未提供新角色立绘和技能图标。可通过原 `ModularHudController.Characters[].Portrait` 和 `GothicHudPanel.SkillIcons` 接入图片。技能名称已与当前行动角色的技能表绑定。

## 清晰度

原始 PNG 保存在本目录 `Art/`，不覆盖旧素材。采用无压缩、无 mipmap 的 Sprite 导入，Canvas 像素对齐，参考分辨率 1920×1080。重要正文为 18px，卡牌状态为 15px；请在固定 1080p、1× Game 预览中检查清晰度。

预览和验证记录见 `D:/GameMake/交付/战斗UI重建/`。同目录的 `GothicHUD-before-rebuild-*.zip` 保留了修改前的完整 GothicHUD 文件夹。

重建命令：`GameMake > Industrial Battle > Rebuild Battle Page`。它更新原 battle prefab 并生成直接战斗预览场景；手工调整布局后请先备份。原 `Rebuild Modular HUD` 菜单生成旧风格，不用于这套新界面。
