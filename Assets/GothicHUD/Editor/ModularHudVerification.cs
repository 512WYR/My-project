using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameMake.GothicHUD;
using UnityEditor;
using UnityEngine;

namespace GameMake.GothicHUDEditor
{
    [InitializeOnLoad]
    public static class ModularHudVerification
    {
        const string Key = "GothicHUD.ModularVerification";
        static IEnumerator routine;
        static double resume;
        static ModularHudVerification()
        {
            EditorApplication.playModeStateChanged += OnMode;
            if (SessionState.GetBool(Key, false) && EditorApplication.isPlaying)
                EditorApplication.delayCall += StartTests;
        }
        public static void Run()
        {
            ModularHudBuilder.Build();
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }
        static void OnMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) StartTests();
        }
        static void StartTests()
        {
            if (routine != null || !SessionState.GetBool(Key, false)) return;
            Application.runInBackground = true;
            routine = Tests(); resume = EditorApplication.timeSinceStartup + .5;
            EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (routine == null || EditorApplication.timeSinceStartup < resume) return;
            try
            {
                if (!routine.MoveNext())
                {
                    EditorApplication.update -= Tick; routine = null; SessionState.SetBool(Key, false);
                    File.AppendAllText(Path.Combine(ModularHudBuilder.Output, "validation.txt"), "PASS: Unity Play Mode checks completed\n");
                    Debug.Log("MODULAR_HUD_PLAYMODE_PASS"); EditorApplication.Exit(0); return;
                }
                resume = EditorApplication.timeSinceStartup + (routine.Current is float seconds ? seconds : .1);
            }
            catch (Exception e)
            {
                SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
                Debug.LogException(e); File.AppendAllText(Path.Combine(ModularHudBuilder.Output, "validation.txt"), "FAIL Play Mode: " + e + "\n"); EditorApplication.Exit(1);
            }
        }
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static void AssertDigits(MechanicalRoundCounter counter, string expected)
        {
            Require(counter.DigitSlots.Count(i => i.gameObject.activeSelf) == expected.Length, "Incorrect digit count: " + expected);
            for (int i = 0; i < expected.Length; i++) Require(counter.DigitSlots[i].sprite == counter.DigitSprites[expected[i]-'0'], "Incorrect digit sprite: " + expected);
        }
        static IEnumerator Tests()
        {
            var hud = UnityEngine.Object.FindObjectOfType<ModularHudController>();
            var counter = UnityEngine.Object.FindObjectOfType<MechanicalRoundCounter>();
            Require(hud && counter, "Missing runtime HUD");
            hud.FollowStageOneBattle = false; counter.FollowStageOneBattle = false;
            AssertDigits(counter, "01");
            foreach (int round in new[] {9,10,99,100})
            {
                counter.SetRound(round); yield return .35f; AssertDigits(counter, round.ToString("D2"));
                Require(counter.DigitContainer.localScale == Vector3.one, "Animation did not restore digit scale");
            }
            counter.SetRound(21); yield return .05f; counter.SetRound(22); yield return .35f; AssertDigits(counter,"22");
            counter.SetRound(23); yield return .05f; counter.gameObject.SetActive(false); counter.SetRound(24); counter.gameObject.SetActive(true); yield return .2f; AssertDigits(counter,"24");
            for(int part=0;part<4;part++)
            {
                hud.SetLimbHealth(0,(HudLimb)part,0,100);
                Require(hud.Limbs[part].Icon.sprite==hud.Limbs[part].BrokenSprite,"Broken limb mapping failed");
                Require(hud.Limbs[part].Fill.fillAmount==0,"Broken limb bar not empty");
                hud.Panel.PartyButtons[1].onClick.Invoke();
                Require(hud.SelectedCharacter==1 && hud.Limbs[part].Icon.sprite==hud.Limbs[part].HealthySprite,"Party selection/state isolation failed");
                hud.Panel.PartyButtons[0].onClick.Invoke();
                Require(hud.Limbs[part].Icon.sprite==hud.Limbs[part].BrokenSprite,"Damage state not retained");
                hud.SetLimbHealth(0,(HudLimb)part,45,100);
                Require(hud.Limbs[part].Icon.sprite==hud.Limbs[part].HealthySprite && Mathf.Abs(hud.Limbs[part].Fill.fillAmount-.45f)<.001f,"Limb restore or fill failed");
                hud.SetLimbHealth(0,(HudLimb)part,100,100);
            }
            hud.SetVitals(0,37,100,60);
            Require(Mathf.Abs(hud.HealthFill.fillAmount-.37f)<.001f && Mathf.Abs(hud.SanityFill.fillAmount-.6f)<.001f,"Vitals did not update");
            hud.SetVitals(0,-10,100,150);
            Require(hud.HealthFill.fillAmount==0 && hud.SanityFill.fillAmount==1,"Vitals clamping failed");
            hud.SetVitals(0,100,100,75);
            int moves=0,skill=-1; hud.OnDisplacementRequested.AddListener(()=>moves++); hud.Panel.OnSkillSelected.AddListener(i=>skill=i);
            hud.DisplacementButton.onClick.Invoke(); Require(moves==1,"Move button not connected or double bound");
            hud.Panel.SkillButtons[4].onClick.Invoke(); Require(skill==4 && hud.Panel.SkillSelections[4].activeSelf,"Skill button not connected");
            foreach(var selected in hud.Panel.SkillSelections) selected.SetActive(false);
            hud.SetLimbBroken(0,HudLimb.Arm,true); counter.SetRound(12); yield return .35f;
            ModularHudBuilder.Render(hud.GetComponent<Canvas>(),Camera.main,"PlayMode-ArmBroken-Round12",1920,1080);
            File.AppendAllText(Path.Combine(ModularHudBuilder.Output,"validation.txt"),"PASS Play Mode: animated 01/09/10/99/100, rapid changes, disable/re-enable\nPASS Play Mode: all 4 broken/restored icons, per-character retention, vitals clamping\nPASS Play Mode: party/skill/movement button callbacks\n");
        }
    }
}
