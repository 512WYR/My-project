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
        static readonly Color SkillReady=new Color32(224,207,176,255);
        static readonly Color SkillBlocked=new Color32(118,114,102,255);
        StageOneApp bound;
        int lastRound;
        BattleEngine lastBattle;
        float wheelAngle, targetWheelAngle;
        Coroutine spin;
        void Start() { Bind(); Sync(); }
        void Update() { if (BattleHud && BattleHud.BattleSource != bound) { Bind(); Sync(); } }
        void LateUpdate()
        {
            // The presentation component and this page both listen to the same Changed event, so the
            // order they run in is not guaranteed. Enforce the invariant after both have synced: as
            // soon as the portrait slot carries art, the procedural/full-body main figure stays off.
            if (MainFigure && BattleHud && BattleHud.Panel && BattleHud.Panel.MainPortrait)
                MainFigure.enabled = !BattleHud.Panel.MainPortrait.sprite;
        }
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
            for(int i=0;i<SkillNames.Length;i++)
            {
                bool usable=i<skills.Length && battle.SkillDisabledReason(i)==null && !bound.Busy;
                SkillNames[i].text=i<skills.Length?skills[i].Name:"—";
                // The frame dims through the button's ColorTint, but the name sits outside that target,
                // so it has to follow the same state or an unavailable skill still reads as available.
                SkillNames[i].color=usable?SkillReady:SkillBlocked;
            }
            for(int i=0;i<PartyNames.Length && i<bound.Run.Heroes.Count;i++)
            {
                var hero=bound.Run.Heroes[i]; PartyNames[i].text=hero.Name;
                Figure(PartyFigures[i],hero);
                PartyFigures[i].enabled=!BattleHud.Panel.PartyPortraits[i].sprite;
            }
            int selected=Mathf.Clamp(BattleHud.Presentation.SelectedCharacter,0,bound.Run.Heroes.Count-1);
            Figure(MainFigure,bound.Run.Heroes[selected]);
            SyncAttributes(battle,bound.Run.Heroes[selected]);
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
        // The bottom panel describes the selected hero, so the six reserved rows read the same effective
        // values the engine uses: limb damage, temporary defence/accuracy buffs and accessories included.
        void SyncAttributes(BattleEngine battle,ActorState hero)
        {
            var values=BattleHud&&BattleHud.Panel?BattleHud.Panel.AttributeValues:null;
            if(values==null || hero==null) return;
            var limbs=battle.Rules.Limbs;
            SetAttribute(0,Mathf.RoundToInt((float)battle.EffectiveAttack(hero)).ToString());
            SetAttribute(1,Percent(battle.EffectiveDefense(hero)));
            string speed=hero.Definition.MaxSpeed>hero.Definition.MinSpeed
                ? hero.Definition.MinSpeed+"–"+hero.Definition.MaxSpeed
                : battle.EffectiveSpeed(hero).ToString();
            SetAttribute(2,speed);
            SetAttribute(3,Percent(hero.Definition.Dodge));
            SetAttribute(4,Percent(Math.Max(hero.Definition.Accuracy*LimbSystem.AccuracyFactor(hero,limbs)+hero.AccuracyBuff,AccessorySystem.AccuracyFloor(hero))));
            SetAttribute(5,Percent(AccessorySystem.AlwaysCrit(hero)?1:hero.Definition.CritChance));
        }
        void SetAttribute(int index,string text)
        {
            var values=BattleHud&&BattleHud.Panel?BattleHud.Panel.AttributeValues:null;
            if(values!=null && index<values.Length && values[index]) values[index].text=text;
        }
        static string Percent(double value)
        {
            return Mathf.RoundToInt((float)Math.Max(0,Math.Min(1,value))*100)+"%";
        }
        static void Figure(SilhouetteGraphic figure,ActorState hero)
        {
            if(!figure) return;
            figure.Silhouette=hero.Definition.Silhouette;
            // Same lookup as the battle cards: a portrait under Resources/StageOne/HeroArt/<Id> replaces
            // the vector figure, and anything without art keeps it.
            var portrait=ActorArt.Portrait(hero.Definition);
            figure.SetPortrait(portrait);
            figure.color=portrait?hero.Dead?new Color(0.47f,0.47f,0.47f,1f):Color.white
                :hero.Dead?new Color32(78,78,72,255):new Color32(210,190,154,255);
            figure.SetVerticesDirty();
        }
        static void TintCard(GothicBattleCard card)
        {
            if(!card || !card.Art) return;
            bool dead=card.DeadCross!=null && card.DeadCross.activeSelf;
            bool copy=card.Actor!=null && card.Actor.IsCopy;
            // The bronze tint is for the vector figure. A portrait carries its own colours, so it only
            // greys out when the actor is dead — the same rule GothicBattleCard.Render applies. A boss
            // copy keeps its greyscale material here too, so the later tint pass cannot erase it.
            card.Art.material=copy?ActorArt.CopyMaterial:null;
            card.Art.color=copy?ActorArt.CopyColor(dead)
                :card.Art.Portrait
                    ?(dead?new Color32(120,120,120,255):Color.white)
                    :dead?new Color32(70,70,66,255):new Color32(206,190,163,255);
            card.Art.SetVerticesDirty();
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
