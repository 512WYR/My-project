using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GameMake.IndustrialDebugUI.EditorTools
{
    /// <summary>
    /// Temporary master switch for every debug-only surface: the acceptance console, both "调试台"
    /// launchers, the forced event in the event-preview scene and the battle hint that mentions the
    /// console. It works by adding <see cref="Define"/> to the currently selected build target; the
    /// source stays untouched, so restoring the feature is one menu click. The define is stored in
    /// ProjectSettings, which means it survives editor restarts until it is removed here.
    /// </summary>
    public static class DebugFeatureToggle
    {
        public const string Define = "RUIN_DEBUG_OFF";
        private const string DisablePath = "GameMake/Debug Console/暂时关闭调试功能";
        private const string RestorePath = "GameMake/Debug Console/恢复调试功能";
        private const string StatusPath = "GameMake/Debug Console/查看调试功能状态";

        [MenuItem(DisablePath, false, 0)]
        private static void Disable() { Apply(true); }

        [MenuItem(DisablePath, true)]
        private static bool DisableValidate() { return !IsDisabled; }

        [MenuItem(RestorePath, false, 1)]
        private static void Restore() { Apply(false); }

        [MenuItem(RestorePath, true)]
        private static bool RestoreValidate() { return IsDisabled; }

        [MenuItem(StatusPath, false, 20)]
        private static void LogStatus()
        {
            Debug.Log(IsDisabled
                ? "调试功能状态：已关闭（" + Define + " 位于 " + Target + "）。"
                    + "运行游戏时调试台与 HUD「调试台」按钮都不会出现。"
                : "调试功能状态：开启（" + Target + " 中没有 " + Define + "）。");
        }

        public static bool IsDisabled
        {
            get
            {
                foreach (var symbol in PlayerSettings.GetScriptingDefineSymbols(Target).Split(';'))
                    if (symbol.Trim() == Define) return true;
                return false;
            }
        }

        private static NamedBuildTarget Target =>
            NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);

        private static void Apply(bool disabled)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("请先退出播放模式，再切换调试功能；切换会触发脚本重编译。");
                return;
            }
            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning("脚本正在编译，请稍后再切换调试功能。");
                return;
            }

            var target = Target;
            var kept = new List<string>();
            foreach (var symbol in PlayerSettings.GetScriptingDefineSymbols(target).Split(';'))
            {
                string trimmed = symbol.Trim();
                if (trimmed.Length > 0 && trimmed != Define) kept.Add(trimmed);
            }
            if (disabled) kept.Add(Define);

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", kept));
            AssetDatabase.SaveAssets();

            Debug.Log(disabled
                ? "已关闭调试功能（" + Define + " → " + target + "）：调试台、HUD「调试台」按钮、"
                    + "事件预览强制事件和战斗调试提示都会停用。菜单 GameMake/Debug Console/恢复调试功能 可还原。"
                : "已恢复调试功能（" + Define + " 已从 " + target + " 移除）。");
        }
    }
}
