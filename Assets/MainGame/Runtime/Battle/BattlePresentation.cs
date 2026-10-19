using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Ruin.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // The full-screen battle cut-in. It owns one canvas built at runtime, reuses four target slots and
    // plays one short charge / clash / result beat per BattleShowGroup. It never reads or writes the
    // battle state; the app feeds it a read-only snapshot through BattleShowPlan.
    public sealed class BattlePresentation : MonoBehaviour
    {
        private const int SortingOrder = 60;
        private const int MaxTargets = 4;
        private const float TargetGap = 24f;
        private const float TargetCentreX = 480f;
        private const float ActorX = -560f;
        private const float SlotY = 20f;

        private static readonly Color DamageColor = new Color32(0xE6, 0x6B, 0x5B, 0xFF);
        private static readonly Color CritColor = new Color32(0xF2, 0xC2, 0x66, 0xFF);
        private static readonly Color HealColor = new Color32(0x72, 0xD1, 0x8A, 0xFF);
        private static readonly Color MissColor = new Color32(0xB4, 0xB8, 0xB8, 0xFF);
        private static readonly Color DodgeColor = new Color32(0x84, 0xD8, 0xE2, 0xFF);
        private static readonly Color GuardColor = new Color32(0x9F, 0xB8, 0xD8, 0xFF);
        private static readonly Color SanityDownColor = new Color32(0xA7, 0x95, 0xDE, 0xFF);
        private static readonly Color SanityUpColor = new Color32(0x8F, 0xC9, 0xD9, 0xFF);
        private static readonly Color LimbColor = new Color32(0xD6, 0x92, 0x5B, 0xFF);
        private static readonly Color SeveredColor = new Color32(0xE0, 0x6A, 0x4F, 0xFF);
        private static readonly Color BuffColor = new Color32(0xE2, 0xC8, 0x89, 0xFF);
        private static readonly Color DamageFlash = new Color32(0xFF, 0xD0, 0xC4, 0xFF);
        private static readonly Color HealFlash = new Color32(0xC8, 0xFF, 0xD8, 0xFF);
        private static readonly Color DodgeFlash = new Color32(0xD0, 0xFA, 0xFF, 0xFF);
        private static readonly Color LimbFlash = new Color32(0xFF, 0xE0, 0xB0, 0xFF);
        private static readonly Color SanityFlash = new Color32(0xDB, 0xCC, 0xFF, 0xFF);
        private static readonly Color BuffFlash = new Color32(0xFF, 0xF0, 0xC8, 0xFF);

        private StageOneApp app;
        private Canvas canvas;
        private GameObject stage;
        private CanvasGroup stageGroup;
        private RectTransform actorSlot;
        private SilhouetteGraphic actorArt;
        private Text actorResult;
        private readonly RectTransform[] targetSlots = new RectTransform[MaxTargets];
        private readonly SilhouetteGraphic[] targetArts = new SilhouetteGraphic[MaxTargets];
        private readonly Text[] targetResults = new Text[MaxTargets];
        private readonly Color[] targetBaseColors = new Color[MaxTargets];
        private readonly Color[] targetFlashColors = new Color[MaxTargets];
        private readonly Vector2[] targetBasePositions = new Vector2[MaxTargets];
        private readonly float[] targetRecoils = new float[MaxTargets];
        private readonly RectTransform[] targetStatusRoots = new RectTransform[MaxTargets];
        private readonly Text[] targetStatusTexts = new Text[MaxTargets];
        private readonly BattleStatusHover[] targetStatusHovers = new BattleStatusHover[MaxTargets];
        private readonly string[] targetStatusContents = new string[MaxTargets];
        private readonly List<BattleShowResult> shown = new List<BattleShowResult>(MaxTargets);
        private RectTransform actorStatusRoot;
        private Text actorStatusText;
        private BattleStatusHover actorStatusHover;
        private string actorStatusContent;

        private bool built;
        private int activeTargets;
        private int generation;

        private Vector2 actorBase;
        private Vector2 actorHit;

        public static BattlePresentation Ensure(StageOneApp owner)
        {
            if (!owner) return null;
            var presentation = owner.GetComponent<BattlePresentation>();
            if (!presentation) presentation = owner.gameObject.AddComponent<BattlePresentation>();
            presentation.app = owner;
            try { presentation.BuildOnce(); }
            catch (Exception e) { Debug.LogException(e); }
            return presentation;
        }

        public void Hide()
        {
            generation++;
            HideStage();
        }

        public IEnumerator Play(IList<BattleShowGroup> groups)
        {
            if (groups == null || groups.Count == 0) yield break;
            try { if (!built) BuildOnce(); }
            catch (Exception e) { Debug.LogException(e); yield break; }
            if (!stage) yield break;
            BattleStatusTooltip.HideAll();

            int token = ++generation;
            int played = 0;
            for (int i = 0; i < groups.Count && played < 4; i++)
            {
                if (token != generation) yield break;
                var group = groups[i];
                if (group == null || group.Actor == null) continue;
                played++;
                yield return RunGuarded(PlayGroup(group, token), token);
                if (token == generation) HideStage();
            }
            if (token == generation) Hide();
        }

        private void BuildOnce()
        {
            if (built) return;
            built = true;

            canvas = MenuUi.MakeCanvas("BattlePresentationCanvas", SortingOrder);
            stage = MenuUi.Stretch(canvas.transform, "Stage").gameObject;
            stageGroup = stage.AddComponent<CanvasGroup>();
            stageGroup.alpha = 0f;

            // The scrim lives inside the stage so the CanvasGroup fade covers it too, and it is the only
            // raycast target: clicks never reach the battle page while a cut-in is playing.
            var scrim = MenuUi.Stretch(stage.transform, "Scrim").gameObject.AddComponent<Image>();
            scrim.color = new Color32(5, 6, 8, 235);
            scrim.raycastTarget = true;

            var content = MenuUi.Stretch(stage.transform, "Content");
            actorSlot = MenuUi.Center(content, "Actor", ActorX, SlotY, 300f, 480f);
            actorArt = AddArt(actorSlot);
            actorResult = MakeResult(actorSlot, "Result", 76);
            actorResult.rectTransform.anchoredPosition = new Vector2(0f, 340f);
            actorResult.rectTransform.sizeDelta = new Vector2(580f, 180f);
            actorStatusText = MakeStatusStrip(actorSlot, "Status", -260f, 340f, 38f, 16,
                out actorStatusRoot, out actorStatusHover);

            for (int i = 0; i < MaxTargets; i++)
            {
                targetSlots[i] = MenuUi.Center(content, "Target " + (i + 1), TargetCentreX, SlotY, 220f, 352f);
                targetArts[i] = AddArt(targetSlots[i]);
                targetResults[i] = MakeResult(targetSlots[i], "Result", 64);
                targetStatusTexts[i] = MakeStatusStrip(targetSlots[i], "Status", -220f, 270f, 38f, 15,
                    out targetStatusRoots[i], out targetStatusHovers[i]);
                targetSlots[i].gameObject.SetActive(false);
            }

            stage.SetActive(false);
        }

        // Graphic's RequireComponent lives on concrete subclasses in Unity 2022, so ensure the renderer
        // explicitly. Without it the mesh is built but never submitted and the slot stays blank.
        private static SilhouetteGraphic AddArt(RectTransform slot)
        {
            if (!slot.GetComponent<CanvasRenderer>()) slot.gameObject.AddComponent<CanvasRenderer>();
            var art = slot.gameObject.AddComponent<SilhouetteGraphic>();
            art.raycastTarget = false;
            return art;
        }

        private static Text MakeResult(Transform parent, string name, int size)
        {
            var text = MenuUi.CenterLabel(parent, name, "", 0f, 0f, 220f, 160f, size, DamageColor);
            // Buff lists can be long; wrap inside the slot instead of pushing the line off-screen.
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = true;
            // Status riders sit directly under the number; the default line height leaves a wide gap
            // between differently sized lines.
            text.lineSpacing = 0.8f;
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(3f, -3f);
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.useGraphicAlpha = true;
            text.gameObject.SetActive(false);
            return text;
        }

        // A small plate under the feet, not a second result over the head. The plate is the raycast
        // target so hovering it can open the shared tooltip; the text itself never blocks a click.
        private static Text MakeStatusStrip(RectTransform slot, string name, float y, float width, float height,
            int fontSize, out RectTransform root, out BattleStatusHover hover)
        {
            root = MenuUi.Center(slot, name, 0f, y, width, height);
            var plate = root.gameObject.AddComponent<Image>();
            plate.color = new Color32(8, 10, 12, 190);
            plate.raycastTarget = true;

            var text = MenuUi.CenterLabel(root, "Text", "", 0f, 0f, width - 14f, height - 2f, fontSize, MenuUi.Ink);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            hover = root.gameObject.AddComponent<BattleStatusHover>();
            root.gameObject.SetActive(false);
            return text;
        }

        private IEnumerator PlayGroup(BattleShowGroup group, int token)
        {
            if (!Configure(group)) yield break;

            stage.SetActive(true);
            stageGroup.alpha = 0f;
            actorSlot.localScale = Vector3.one;
            yield return Tween(0.16f, t => stageGroup.alpha = t, token);
            if (token != generation) yield break;

            bool support = !HasOffence(group);
            bool sanityAttack = !support && IsSanityAttack(group);
            Vector2 fadeFrom = actorBase;
            if (support)
            {
                // Heal / protect / buff: no dash. The actor pulses in place and every target glows in
                // its result colour at the same time.
                yield return Tween(0.20f, t =>
                {
                    float pulse = 1f + 0.05f * Mathf.Sin(t * Mathf.PI);
                    actorSlot.localScale = new Vector3(pulse, pulse, 1f);
                    for (int i = 0; i < activeTargets; i++)
                        targetArts[i].color = Color.Lerp(targetFlashColors[i], targetBaseColors[i], t);
                }, token);
                ShowResults();
                PlaySupportCue(group);
                yield return RevealResults(token);
            }
            else
            {
                // The attack is split around the dash: the windup plays while the actor lines up, the
                // hit lands with the numbers. The sanity attack is one continuous take, so it only
                // fires on impact.
                if (!sanityAttack) SfxDirector.Play(SfxCue.AttackWindup);
                Vector2 wind = actorBase + new Vector2(-36f, 0f);
                yield return Tween(0.18f, t => actorSlot.anchoredPosition = Vector2.Lerp(actorBase, wind, EaseOut(t)), token);
                yield return Tween(0.16f, t => actorSlot.anchoredPosition = Vector2.Lerp(wind, actorHit, EaseIn(t)), token);

                ShowResults();
                PlayOffenceCue(group, sanityAttack);
                yield return Tween(0.18f, t =>
                {
                    float punch = EaseOut(t);
                    for (int i = 0; i < activeTargets; i++)
                    {
                        targetSlots[i].anchoredPosition = targetBasePositions[i] + new Vector2(targetRecoils[i] * punch, 0f);
                        targetArts[i].color = Color.Lerp(targetFlashColors[i], targetBaseColors[i], t);
                    }
                    float pop = Pop(t);
                    for (int i = 0; i < activeTargets; i++)
                        targetResults[i].rectTransform.localScale = new Vector3(pop, pop, 1f);
                    if (actorResult.gameObject.activeSelf)
                        actorResult.rectTransform.localScale = new Vector3(pop, pop, 1f);
                }, token);
                fadeFrom = actorHit;
            }

            float hold = 0.80f + (group.Critical ? 0.20f : 0f) + (HasDeath(group) ? 0.18f : 0f);
            yield return WaitRealtime(hold, token);

            yield return Tween(0.22f, t =>
            {
                stageGroup.alpha = 1f - t;
                actorSlot.anchoredPosition = Vector2.Lerp(fadeFrom, actorBase, t);
                actorSlot.localScale = Vector3.one;
                for (int i = 0; i < activeTargets; i++)
                    targetSlots[i].anchoredPosition = Vector2.Lerp(targetBasePositions[i] + new Vector2(targetRecoils[i], 0f), targetBasePositions[i], t);
            }, token);

            HideStage();
        }

        private bool Configure(BattleShowGroup group)
        {
            if (!actorArt || !actorSlot || group == null || group.Actor == null) return false;

            actorBase = new Vector2(ActorX, SlotY);
            actorSlot.anchoredPosition = actorBase;
            actorSlot.localScale = Vector3.one;
            ApplyArt(actorArt, group.Actor, false);
            actorResult.text = "";
            actorResult.gameObject.SetActive(false);
            actorStatusContent = null;
            if (actorStatusRoot) actorStatusRoot.gameObject.SetActive(false);
            if (actorStatusHover) actorStatusHover.Bind(null);

            shown.Clear();
            BattleShowResult self = null;
            for (int i = 0; i < group.Results.Count; i++)
            {
                var r = group.Results[i];
                if (r == null || r.Target == null) continue;
                if (r.Target.Id == group.Actor.Id) { self = r; continue; }
                shown.Add(r);
                if (shown.Count >= MaxTargets) break;
            }

            if (self != null)
            {
                Color color;
                string text = Format(self, group.Critical, 76, out color);
                if (!string.IsNullOrEmpty(text))
                {
                    actorResult.text = text;
                    actorResult.color = color;
                    actorResult.fontSize = 76;
                }
                if (!self.Died && (self.Buffed || self.LimbWorn || self.SanityLocked))
                {
                    actorStatusContent = StatusContent(self);
                    actorStatusText.text = actorStatusContent;
                    if (actorStatusHover) actorStatusHover.Bind(group.Actor);
                }
            }

            activeTargets = shown.Count;
            float width = TargetWidth(activeTargets);
            float height = width * 1.6f;
            float total = activeTargets * width + Mathf.Max(0, activeTargets - 1) * TargetGap;
            float start = TargetCentreX - total * 0.5f + width * 0.5f;

            for (int i = 0; i < MaxTargets; i++)
            {
                targetStatusContents[i] = null;
                if (targetStatusRoots[i]) targetStatusRoots[i].gameObject.SetActive(false);
                if (targetStatusHovers[i]) targetStatusHovers[i].Bind(null);

                bool use = i < activeTargets;
                targetSlots[i].gameObject.SetActive(use);
                targetResults[i].gameObject.SetActive(false);
                if (!use) continue;

                var r = shown[i];
                float x = start + i * (width + TargetGap);
                targetSlots[i].sizeDelta = new Vector2(width, height);
                targetSlots[i].anchoredPosition = new Vector2(x, SlotY);
                targetSlots[i].localScale = Vector3.one;
                targetBasePositions[i] = targetSlots[i].anchoredPosition;

                ApplyArt(targetArts[i], r.Target, r.Died);
                targetBaseColors[i] = targetArts[i].color;
                targetFlashColors[i] = FlashFor(r);

                int size = Mathf.Clamp(Mathf.RoundToInt(width * 0.42f), 36, 84);
                Color color;
                string text = Format(r, group.Critical, size, out color);
                var label = targetResults[i];
                label.rectTransform.anchoredPosition = new Vector2(0f, height * 0.5f + 78f);
                label.rectTransform.sizeDelta = new Vector2(Mathf.Max(240f, width + 160f), 170f);
                label.rectTransform.localScale = Vector3.one;
                label.fontSize = size;
                label.text = text ?? "";
                label.color = color;
                targetRecoils[i] = RecoilFor(r);

                if (!r.Died && (r.Buffed || r.LimbWorn || r.SanityLocked))
                {
                    string content = StatusContent(r);
                    if (!string.IsNullOrEmpty(content))
                    {
                        targetStatusContents[i] = content;
                        targetStatusTexts[i].text = content;
                        targetStatusRoots[i].anchoredPosition = new Vector2(0f, -height * 0.5f - 46f);
                        targetStatusRoots[i].sizeDelta = new Vector2(width + 50f, 40f);
                        targetStatusTexts[i].rectTransform.sizeDelta = new Vector2(width + 34f, 38f);
                        targetStatusHovers[i].Bind(r.Target);
                    }
                }
            }

            actorHit = new Vector2(TargetCentreX - total * 0.5f - 160f, SlotY);
            if (activeTargets == 0) actorHit = actorBase + new Vector2(120f, 0f);
            if (HasMiss(group)) actorHit = Vector2.Lerp(actorBase, actorHit, 0.55f);

            return activeTargets > 0 || !string.IsNullOrEmpty(actorResult.text);
        }

        private void ShowResults()
        {
            for (int i = 0; i < activeTargets; i++)
            {
                var label = targetResults[i];
                if (string.IsNullOrEmpty(label.text))
                {
                    label.gameObject.SetActive(false);
                }
                else
                {
                    label.gameObject.SetActive(true);
                    label.rectTransform.localScale = new Vector3(0.68f, 0.68f, 1f);
                }
                if (targetStatusRoots[i])
                    targetStatusRoots[i].gameObject.SetActive(!string.IsNullOrEmpty(targetStatusContents[i]));
            }
            if (!string.IsNullOrEmpty(actorStatusContent) && actorStatusRoot)
                actorStatusRoot.gameObject.SetActive(true);
            if (string.IsNullOrEmpty(actorResult.text)) return;
            actorResult.gameObject.SetActive(true);
            actorResult.rectTransform.localScale = new Vector3(0.68f, 0.68f, 1f);
        }

        private IEnumerator RevealResults(int token)
        {
            yield return Tween(0.18f, t =>
            {
                float pop = Pop(t);
                for (int i = 0; i < activeTargets; i++)
                    targetResults[i].rectTransform.localScale = new Vector3(pop, pop, 1f);
                if (actorResult.gameObject.activeSelf)
                    actorResult.rectTransform.localScale = new Vector3(pop, pop, 1f);
            }, token);
        }

        private static void ApplyArt(SilhouetteGraphic art, ActorState actor, bool dead)
        {
            if (!art || actor == null || actor.Definition == null) return;
            var spec = actor.Definition;
            art.Silhouette = spec.Silhouette;
            art.Enemy = spec.Pattern;
            art.IsEnemy = !actor.Player;
            art.Accent = actor.Player ? MenuUi.Gold : new Color32(0xB4, 0x6B, 0x5B, 0xFF);
            var portrait = ActorArt.Portrait(spec);
            art.SetPortrait(portrait);
            // The cut-in background is nearly black, so the vector silhouettes use a lighter body than
            // they do on the battle card; a portrait keeps its own colours and only greys out on death.
            art.color = portrait != null
                ? dead ? new Color(0.47f, 0.47f, 0.47f, 1f) : Color.white
                : dead ? new Color32(0x3A, 0x41, 0x44, 0xFF) : new Color32(0x7A, 0x84, 0x86, 0xFF);
            art.SetVerticesDirty();
        }

        private static float TargetWidth(int count)
        {
            if (count <= 0) return 220f;
            return Mathf.Clamp((760f - TargetGap * (count - 1)) / count, 150f, 220f);
        }

        private static bool HasOffence(BattleShowGroup group)
        {
            for (int i = 0; i < group.Results.Count; i++)
            {
                var r = group.Results[i];
                if (r == null) continue;
                // Damage, sanity drain and limb damage look like attacks, but only when the victim is
                // somebody other than the actor. A bleed tick hurts its own carrier and stays in place.
                bool other = group.Actor == null || r.Target == null || r.Target.Id != group.Actor.Id;
                if (!other) continue;
                if (r.Damage > 0 || r.Missed || r.Dodged) return true;
                if (r.SanityDelta < -0.0001 || r.LimbWorn) return true;
            }
            return false;
        }

        // Heal wins over guard; either one replaces the generic click the beat would otherwise have.
        // A fully blocked attack has no damage, so it also lands in the support branch: the show plan
        // labels the blocker "格挡", while a protect skill labels its target "守护".
        private static void PlaySupportCue(BattleShowGroup group)
        {
            bool heal = false, block = false, guard = false;
            for (int i = 0; i < group.Results.Count; i++)
            {
                var r = group.Results[i];
                if (r == null) continue;
                if (r.Heal > 0) heal = true;
                if (r.Guarded)
                {
                    if (r.Label == "格挡") block = true;
                    else guard = true;
                }
                if (r.Buffed && Contains(r.BuffText, "防御")) guard = true;
            }
            if (heal) SfxDirector.Play(SfxCue.Heal);
            else if (block) SfxDirector.Play(SfxCue.Block);
            else if (guard) SfxDirector.Play(SfxCue.DefendUp);
        }

        // A miss has no impact sound. An interception puts damage on the guardian, while a plain block
        // leaves the guarded target at zero damage, so the same result flags distinguish the two.
        private static void PlayOffenceCue(BattleShowGroup group, bool sanityAttack)
        {
            bool missed = false, guarded = false, intercepted = false, hit = false;
            for (int i = 0; i < group.Results.Count; i++)
            {
                var r = group.Results[i];
                if (r == null) continue;
                if (r.Missed || r.Dodged) missed = true;
                if (r.Damage > 0) hit = true;
                if (!r.Guarded) continue;
                guarded = true;
                if (r.Damage > 0) intercepted = true;
            }

            if (missed) SfxDirector.Play(SfxCue.AttackMiss);
            else if (intercepted) SfxDirector.Play(SfxCue.GuardHit);
            // A fully blocked beat has no number anywhere; if any target still took damage the normal
            // impact sound is the honest one.
            else if (guarded && !hit) SfxDirector.Play(SfxCue.Block);
            else SfxDirector.Play(sanityAttack ? SfxCue.SanityAttack : SfxCue.AttackHit);
        }

        // The whisperer is the sanity enemy; any enemy hit that actually drains sanity gets its take
        // even if a future variant is added under another pattern.
        private static bool IsSanityAttack(BattleShowGroup group)
        {
            if (group.Actor == null || group.Actor.Player) return false;
            var definition = group.Actor.Definition;
            if (definition != null && definition.Pattern == EnemyPattern.Whisperer) return true;
            for (int i = 0; i < group.Results.Count; i++)
                if (group.Results[i] != null && group.Results[i].SanityDelta < -0.0001) return true;
            return false;
        }

        private static bool Contains(string text, string part)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(part, StringComparison.Ordinal) >= 0;
        }

        private static bool HasMiss(BattleShowGroup group)
        {
            for (int i = 0; i < group.Results.Count; i++)
                if (group.Results[i] != null && group.Results[i].Missed) return true;
            return false;
        }

        private static bool HasDeath(BattleShowGroup group)
        {
            for (int i = 0; i < group.Results.Count; i++)
                if (group.Results[i] != null && group.Results[i].Died) return true;
            return false;
        }

        private static Color FlashFor(BattleShowResult r)
        {
            if (r.Missed) return MissColor;
            if (r.Dodged) return DodgeFlash;
            if (r.Damage > 0) return DamageFlash;
            if (r.LimbWorn) return LimbFlash;
            if (r.SanityDelta < -0.0001) return SanityFlash;
            if (r.Heal > 0) return HealFlash;
            if (r.Buffed) return BuffFlash;
            return GuardColor;
        }

        private static float RecoilFor(BattleShowResult r)
        {
            if (r.Missed) return 0f;
            if (r.Dodged) return 20f;
            if (r.Damage > 0) return 70f;
            if (r.LimbWorn) return 40f;
            if (r.SanityDelta < -0.0001) return 24f;
            if (r.Heal > 0) return 10f;
            if (r.Buffed) return 8f;
            if (Math.Abs(r.SanityDelta) > 0.0001 || r.SanityLocked) return 8f;
            return 70f;
        }

        private static string StatusContent(BattleShowResult r)
        {
            if (r == null || r.Target == null) return null;
            string content = BattleStatus.Compact(r.Target, 5);
            if (string.IsNullOrEmpty(content) && r.Buffed) content = r.BuffText;
            return content;
        }

        // Damage wins over heal, heal wins over a status label. Sanity, limb and concrete buff names are
        // secondary lines under the number; when a beat has none of the primary results, the first
        // status becomes the main line so it still reads at arm's length.
        private static string Format(BattleShowResult r, bool critical, int baseSize, out Color color)
        {
            color = DamageColor;
            if (r == null) return null;
            if (r.Missed) { color = MissColor; return "未命中"; }
            if (r.Dodged) { color = DodgeColor; return "闪避"; }

            int small = Mathf.Max(14, Mathf.RoundToInt(baseSize * 0.34f));
            string buffLine = BuffLine(r);
            string sanityLine = SanityLine(r);
            string limbLine = LimbLine(r);

            string main = null;
            bool numeric = false;
            bool promotedBuff = false, promotedSanity = false, promotedLimb = false;
            if (r.Damage > 0)
            {
                color = critical ? CritColor : DamageColor;
                int big = Mathf.RoundToInt(baseSize * (critical ? 1.25f : 1f));
                string number = "<size=" + big + ">-" + r.Damage + "</size>";
                main = critical
                    ? "<size=" + Mathf.RoundToInt(baseSize * 0.55f) + ">暴击！</size>\n" + number
                    : number;
                numeric = true;
            }
            else if (r.Heal > 0)
            {
                color = HealColor;
                main = "<size=" + baseSize + ">+" + r.Heal + "</size>";
                numeric = true;
            }
            else if (!string.IsNullOrEmpty(r.Label))
            {
                color = r.Label == "净化" ? HealColor : GuardColor;
                main = r.Label;
            }
            else if (buffLine != null)
            {
                color = BuffColor;
                main = Sized(buffLine, small);
                promotedBuff = true;
            }
            else if (sanityLine != null)
            {
                color = SanityColorFor(r);
                main = Sized(sanityLine, small);
                promotedSanity = true;
            }
            else if (limbLine != null)
            {
                color = r.LimbSevered ? SeveredColor : LimbColor;
                main = Sized(limbLine, small);
                promotedLimb = true;
            }

            var extras = new List<string>(4);
            if (numeric && !string.IsNullOrEmpty(r.Label)) extras.Add(Sized(r.Label, small));
            if (!promotedSanity && sanityLine != null) extras.Add(Sized(sanityLine, small));
            if (!promotedLimb && limbLine != null) extras.Add(Sized(limbLine, small));
            if (!promotedBuff && buffLine != null) extras.Add(Sized(buffLine, small));
            for (int i = 0; i < extras.Count; i++)
                main = string.IsNullOrEmpty(main) ? extras[i] : main + "\n" + extras[i];

            if (r.Died)
            {
                string caption = r.Target != null && r.Target.Player ? "倒下" : "击破";
                if (main == null) { color = MissColor; main = caption; }
                else main += "\n<size=" + Mathf.RoundToInt(baseSize * 0.45f) + ">" + caption + "</size>";
            }

            return main;
        }

        private static Color SanityColorFor(BattleShowResult r)
        {
            if (r.SanityLocked) return GuardColor;
            return r.SanityDelta >= 0 ? SanityUpColor : SanityDownColor;
        }

        private static string SanityLine(BattleShowResult r)
        {
            if (r.SanityLocked) return "<color=#9FB8D8>理智锁定</color>";
            if (Math.Abs(r.SanityDelta) < 0.0001) return null;
            bool up = r.SanityDelta > 0;
            string amount = Math.Round(Math.Abs(r.SanityDelta), 1, MidpointRounding.AwayFromZero)
                .ToString("0.#", CultureInfo.InvariantCulture);
            return "<color=" + (up ? "#8FC9D9" : "#A795DE") + ">理智 " + (up ? "+" : "-") + amount + "</color>";
        }

        private static string LimbLine(BattleShowResult r)
        {
            if (!r.LimbWorn) return null;
            string part = string.IsNullOrEmpty(r.LimbPart) ? "肢体" : r.LimbPart;
            if (r.LimbSevered) return "<color=#E06A4F>" + part + " 断肢！</color>";
            if (r.LimbDamage > 0) return "<color=#D6925B>" + part + " 受伤 -" + r.LimbDamage + "</color>";
            return "<color=#D6925B>" + part + " 受伤</color>";
        }

        private static string BuffLine(BattleShowResult r)
        {
            if (!r.Buffed) return null;
            string text = string.IsNullOrEmpty(r.BuffText) ? "增益" : r.BuffText;
            return "<color=#E2C889>" + text + "</color>";
        }

        private static string Sized(string richText, int size)
        {
            return "<size=" + size + ">" + richText + "</size>";
        }

        private static float EaseOut(float t) { return 1f - (1f - t) * (1f - t); }
        private static float EaseIn(float t) { return t * t; }
        private static float Pop(float t) { return 0.68f + 0.32f * t + 0.15f * Mathf.Sin(t * Mathf.PI); }

        private IEnumerator Tween(float duration, Action<float> apply, int token)
        {
            if (duration <= 0f)
            {
                try { apply(1f); } catch (Exception e) { Debug.LogException(e); }
                yield break;
            }
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (token != generation) yield break;
                elapsed += Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                float t = Mathf.Clamp01(elapsed / duration);
                try { apply(t); } catch (Exception e) { Debug.LogException(e); yield break; }
                yield return null;
            }
            if (token != generation) yield break;
            try { apply(1f); } catch (Exception e) { Debug.LogException(e); }
        }

        private IEnumerator WaitRealtime(float seconds, int token)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (token != generation) yield break;
                elapsed += Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                yield return null;
            }
        }

        // Coroutines cannot yield inside a try/catch, so the inner animation is stepped by hand. Any
        // exception is logged and ends the beat instead of escaping into ContinueEnemies.
        private IEnumerator RunGuarded(IEnumerator routine, int token)
        {
            while (routine != null)
            {
                if (token != generation) yield break;
                bool moved;
                object current = null;
                try
                {
                    moved = routine.MoveNext();
                    if (moved) current = routine.Current;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    yield break;
                }
                if (!moved) yield break;
                yield return current;
            }
        }

        private void HideStage()
        {
            BattleStatusTooltip.HideAll();
            if (stageGroup) stageGroup.alpha = 0f;
            if (stage) stage.SetActive(false);
        }

        private void Update()
        {
            // StopAllCoroutines (return to menu, new run, battle rebuild) can strand the canvas without
            // running the tail of PlayGroup. Busy is the app's own marker for "an action is in flight".
            if (stage && stage.activeSelf && (!app || !app.Busy)) Hide();
        }

        private void OnDestroy()
        {
            generation++;
            BattleStatusTooltip.HideAll();
            if (canvas) Destroy(canvas.gameObject);
            canvas = null;
            stage = null;
            actorSlot = null;
            actorArt = null;
            actorResult = null;
            actorStatusRoot = null;
            actorStatusText = null;
            actorStatusHover = null;
            for (int i = 0; i < MaxTargets; i++)
            {
                targetSlots[i] = null;
                targetArts[i] = null;
                targetResults[i] = null;
                targetStatusRoots[i] = null;
                targetStatusTexts[i] = null;
                targetStatusHovers[i] = null;
                targetStatusContents[i] = null;
            }
        }
    }
}
