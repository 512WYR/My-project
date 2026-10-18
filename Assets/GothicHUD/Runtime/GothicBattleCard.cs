using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    /// <summary>
    /// One position on the battle stage. The controller writes into it; it never rebuilds its own hierarchy,
    /// because a battle refreshes many times a second and the authored objects have to survive that.
    /// </summary>
    public sealed class GothicBattleCard : MonoBehaviour
    {
        [Header("Wiring / 引用")]
        public Button Hit;
        public Image Background;
        public Outline Highlight;
        public SilhouetteGraphic Art;
        public Text Name;
        public Image HealthFill;
        public Text HealthValue;
        public Image SanityFill;
        public Text SanityValue;
        public Text Intent;
        public Text Status;
        public GameObject ActiveBadge;
        public GameObject TargetBadge;
        public GameObject DeadCross;

        [Header("Colors / 配色")]
        public Color AliveBody = new Color32(8, 14, 19, 255);
        public Color DeadBody = new Color32(65, 72, 75, 255);
        public Color AllyAccent = new Color32(129, 193, 200, 255);
        public Color EnemyAccent = new Color32(206, 136, 117, 255);
        public Color DeadAccent = new Color32(90, 94, 93, 255);
        public Color PossibleTint = new Color32(52, 76, 82, 255);
        public Color ActiveTint = new Color32(45, 54, 58, 255);

        // Filled by the controller so a click can be routed back without the card knowing about the battle.
        [HideInInspector] public string ActorId;

        public void Render(ActorState actor, bool active, bool possible, bool inspected, string intent)
        {
            if (actor == null) { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            ActorId = actor.Id;

            Color accent = actor.Player ? AllyAccent : EnemyAccent;
            if (actor.Dead) accent = DeadAccent;

            if (Background)
                Background.color = actor.Dead ? DeadBody : possible ? PossibleTint : active ? ActiveTint : AliveBody;

            if (Highlight)
            {
                Highlight.enabled = possible || active || inspected;
                Highlight.effectColor = possible || active ? new Color32(219, 187, 131, 255) : accent;
            }

            if (Art)
            {
                Art.Silhouette = actor.Definition.Silhouette;
                Art.Enemy = actor.Definition.Pattern;
                Art.IsEnemy = !actor.Player;
                Art.Accent = accent;
                Art.color = actor.Dead ? DeadBody : AliveBody;
                Art.raycastTarget = false;
                // The silhouette fields are plain fields on a mesh that is only rebuilt when the graphic is
                // marked dirty, and the colour alone may be unchanged when a swap puts a different hero on
                // this rank. Without this the previous occupant's silhouette would linger.
                Art.SetVerticesDirty();
            }

            if (Name) Name.text = actor.Name;

            if (HealthFill) HealthFill.fillAmount = Mathf.Clamp01((float)actor.Hp / Mathf.Max(1, actor.Definition.MaxHp));
            if (HealthValue)
                HealthValue.text = actor.Dead ? "已死亡"
                    : actor.DeathDoor ? "濒死 · 需要治疗"
                    : actor.Hp + " / " + actor.Definition.MaxHp;

            // Sanity sits directly under the health bar so a hero's other vital is visible on the stage,
            // not only on the selected-character panel.
            if (SanityFill) SanityFill.fillAmount = Mathf.Clamp01((float)actor.Sanity / 100f);
            if (SanityValue)
                SanityValue.text = Mathf.Clamp((float)actor.Sanity, 0f, 100f).ToString("0") + " / 100";

            // A card carries one status line: the intent for enemies, the limb/state summary for allies.
            // They share a slot because a position card only has room for one.
            if (Status) Status.text = actor.Player ? StatusLine(actor) : "";
            if (Intent) Intent.text = intent ?? "";

            if (ActiveBadge) ActiveBadge.SetActive(active);
            if (TargetBadge) TargetBadge.SetActive(possible);
            if (DeadCross) DeadCross.SetActive(actor.Dead);
        }

        // Only allies reach this; an enemy card shows its intent in the same slot instead. The wording
        // mirrors the built-in battle cards, so a hero reads the same on either page.
        private static string StatusLine(ActorState actor)
        {
            string limb = LimbSystem.Summary(actor);
            string state = actor.BleedTurns > 0 ? "流血 " + actor.BleedDamage + "×" + actor.BleedTurns
                : actor.Form == SkillForm.Technical ? "高理智技能"
                : actor.Form == SkillForm.Primal ? "低理智变化" : "";
            if (limb.Length == 0 && state.Length == 0)
                return AccessorySystem.ShortSummary(actor);
            if (limb.Length == 0) return state;
            return state.Length == 0 ? limb : limb + " · " + state;
        }
    }
}
