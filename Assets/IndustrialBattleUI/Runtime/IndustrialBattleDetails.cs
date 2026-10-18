using System;
using System.Collections;
using GameMake.GothicHUD;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.IndustrialBattleUI
{
    public sealed class IndustrialBattleDetails : MonoBehaviour
    {
        public GothicBattleHudController BattleHud;
        public Text[] SkillNames, PartyNames;
        public SilhouetteGraphic[] PartyFigures;
        public SilhouetteGraphic MainFigure;
        public RectTransform Wheel;
        public Text TurnLabel;
        StageOneApp bound;
        int lastRound;
        BattleEngine lastBattle;
        float wheelAngle, targetWheelAngle;
        Coroutine spin;
        void Start() { Bind(); Sync(); }
        void Update() { if (BattleHud && BattleHud.BattleSource != bound) { Bind(); Sync(); } }
        void OnEnable() { Bind(); Sync(); }
        void OnDisable()
        {
            if(bound) bound.Changed-=Sync; bound=null;
            if(spin!=null) StopCoroutine(spin); spin=null;
            wheelAngle=targetWheelAngle;
            if(Wheel) Wheel.localEulerAngles=new Vector3(0,0,wheelAngle);
        }
        void Bind()
        {
            if(bound) bound.Changed-=Sync;
            bound=BattleHud?BattleHud.BattleSource:null;
            if(bound) bound.Changed+=Sync;
        }
        public void Sync()
        {
            if(!bound || bound.Run==null || bound.Battle==null) return;
            var battle=bound.Battle;
            var skills=battle.Current!=null && battle.Current.Player ? battle.SkillsFor(battle.Current) : Array.Empty<SkillSpec>();
            for(int i=0;i<SkillNames.Length;i++) SkillNames[i].text=i<skills.Length?skills[i].Name:"—";
            for(int i=0;i<PartyNames.Length && i<bound.Run.Heroes.Count;i++)
            {
                var hero=bound.Run.Heroes[i]; PartyNames[i].text=hero.Name;
                Figure(PartyFigures[i],hero);
                PartyFigures[i].enabled=!BattleHud.Panel.PartyPortraits[i].sprite;
            }
            int selected=Mathf.Clamp(BattleHud.Presentation.SelectedCharacter,0,bound.Run.Heroes.Count-1);
            Figure(MainFigure,bound.Run.Heroes[selected]);
            MainFigure.enabled=!BattleHud.Panel.MainPortrait.sprite;
            // Card geometry is authored separately from its body, so keep the silhouettes legible.
            foreach(var card in BattleHud.Allies) TintCard(card);
            foreach(var card in BattleHud.Enemies) TintCard(card);
            TurnLabel.text=battle.Current==null?"战斗结束":battle.Current.Name+"  ·  "+(battle.Current.Player?"我方行动":"敌方行动");
            if(lastBattle!=battle || battle.Round<lastRound)
            {
                if(spin!=null) StopCoroutine(spin); spin=null;
                wheelAngle=targetWheelAngle=0;
                if(Wheel) Wheel.localEulerAngles=Vector3.zero;
                lastBattle=battle;
            }
            else if(battle.Round>lastRound && Wheel)
            {
                targetWheelAngle-=30f*(battle.Round-lastRound);
                if(spin!=null) StopCoroutine(spin);
                if(isActiveAndEnabled) spin=StartCoroutine(Spin());
                else { wheelAngle=targetWheelAngle; Wheel.localEulerAngles=new Vector3(0,0,wheelAngle); }
            }
            lastRound=battle.Round;
        }
        static void Figure(SilhouetteGraphic figure,ActorState hero)
        {
            if(!figure) return;
            figure.Silhouette=hero.Definition.Silhouette; figure.color=hero.Dead?new Color32(78,78,72,255):new Color32(210,190,154,255);
            figure.SetVerticesDirty();
        }
        static void TintCard(GothicBattleCard card)
        {
            if(!card || !card.Art) return;
            card.Art.color=card.DeadCross.activeSelf?new Color32(70,70,66,255):new Color32(206,190,163,255);
        }
        IEnumerator Spin()
        {
            float begin=Time.unscaledTime, angle=wheelAngle, end=targetWheelAngle;
            while(Time.unscaledTime-begin<.45f)
            { float t=Mathf.Clamp01((Time.unscaledTime-begin)/.45f); wheelAngle=Mathf.Lerp(angle,end,1-Mathf.Pow(1-t,3)); Wheel.localEulerAngles=new Vector3(0,0,wheelAngle); yield return null; }
            wheelAngle=end; Wheel.localEulerAngles=new Vector3(0,0,end); spin=null;
        }
    }
}
