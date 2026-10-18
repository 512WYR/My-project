using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ruin.Runtime
{
    // Transient per-frame UI state. Kept out of BattleLabConfig because none of it should be exported.
    public sealed class BattleLabUiState
    {
        public int Tab;
        public int ConfigSubTab;
        public Vector2[] ConfigSubScroll = new Vector2[4];
        public Vector2 BattleScroll;
        public Vector2 LogScroll;
        public Vector2 LogListScroll;
        public int LogSeenCount;
        public Vector2 ToolsScroll;

        public string SelectedActorId;
        public int SelectedSkillIndex = -1;
        public string SelectedTargetId;
        public string SwapTargetId;
        public int SelectedConsumableIndex = -1;
        public string ConsumableTargetId;
        public int[] ProstheticSelection = new int[4];


        public string ConfigJson = "";
        public string ImportMessage = "";
        public bool ImportMessageIsError;

        public string LogKind = "";
        public string LogActor = "";
        public string LogTarget = "";
        public string LogSearch = "";
        public bool LogCurrentOnly;
        public bool LogAutoScroll = true;

        public string SnapshotName = "";
        public string RngStateInput = "";
        public string ForceValueInput = "0";
        public string ForceTimesInput = "1";
        public bool ForceConsumeState = true;

        public string ReplayReport = "";
        public string ReplayJson = "";
        public string FuzzReport = "";
        public BattleLabFuzzProgress FuzzProgress;
        public BattleLabFuzzSettings FuzzSettings = new BattleLabFuzzSettings();
        public int FuzzIndex;
        public Coroutine FuzzRoutine;

        public string BugNote = "";
        public string InvariantReport = "";
        public string PurityReport = "";
        public string ExportMessage = "";
        public bool ExportMessageIsError;

        public string ActiveSkillHint = "";

        // Foldout state for the categorized pages. A key is opened once with its default, then the user's
        // choice is remembered until the app is reloaded.
        private readonly HashSet<string> openGroups = new HashSet<string>();
        private readonly HashSet<string> initializedGroups = new HashSet<string>();

        public bool GroupOpen(string key, bool defaultOpen)
        {
            if (key == null) return defaultOpen;
            if (!initializedGroups.Contains(key))
            {
                initializedGroups.Add(key);
                if (defaultOpen) openGroups.Add(key);
            }
            return openGroups.Contains(key);
        }

        public void SetGroupOpen(string key, bool open)
        {
            if (key == null) return;
            initializedGroups.Add(key);
            if (open) openGroups.Add(key);
            else openGroups.Remove(key);
        }
    }
}
