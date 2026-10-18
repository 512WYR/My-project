using System.Collections;
using System.Globalization;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.GothicHUD
{
    public sealed class MechanicalRoundCounter : MonoBehaviour
    {
        [Header("Round / 回合数")]
        [SerializeField, Min(1)] private int roundNumber = 1;
        public bool FollowStageOneBattle = true;
        public StageOneApp BattleSource;
        [Header("Digit sprites in 0–9 order / 数字素材")]
        public Sprite[] DigitSprites = new Sprite[10];
        [Tooltip("Ten authored slots support the full positive int range. Only necessary slots are shown.")]
        public Image[] DigitSlots = new Image[10];
        public RectTransform DigitContainer;
        [Tooltip("Size the digit tiles to the authored inner opening.")]
        public bool FitDigitsToContainer;
        public bool AnimateChanges = true;
        [Range(.05f, .5f)] public float FlipDuration = .18f;
        private Coroutine flipRoutine;
        private float nextSourceSearch;
        public int RoundNumber => roundNumber;
        public static string FormatRound(int round) => Mathf.Max(1, round).ToString("D2", CultureInfo.InvariantCulture);

        private void OnEnable() { Draw(roundNumber); }
        private void OnDisable()
        {
            if (flipRoutine != null) StopCoroutine(flipRoutine);
            flipRoutine = null;
            if (DigitContainer) DigitContainer.localScale = Vector3.one;
        }
        private void OnValidate()
        {
            roundNumber = Mathf.Max(1, roundNumber);
            Draw(roundNumber);
        }
        private void Update()
        {
            if (!FollowStageOneBattle) return;
            if (!BattleSource && Time.unscaledTime >= nextSourceSearch)
            {
                BattleSource = FindObjectOfType<StageOneApp>();
                nextSourceSearch = Time.unscaledTime + 1f;
            }
            if (BattleSource && BattleSource.Battle != null) SetRound(BattleSource.Battle.Round);
        }

        /// <summary>Call after the combat system advances a complete round, not after each unit's action.</summary>
        public void SetRound(int value)
        {
            value = Mathf.Max(1, value);
            if (value == roundNumber) return;
            roundNumber = value;
            if (flipRoutine != null) StopCoroutine(flipRoutine);
            if (DigitContainer) DigitContainer.localScale = Vector3.one;
            if (Application.isPlaying && isActiveAndEnabled && AnimateChanges && DigitContainer)
                flipRoutine = StartCoroutine(Flip(value));
            else Draw(value);
        }
        [ContextMenu("Preview / Next Round")]
        public void AdvanceRound() { SetRound(roundNumber == int.MaxValue ? int.MaxValue : roundNumber + 1); }
        [ContextMenu("Preview / Reset to 01")]
        public void ResetRound() { SetRound(1); }
        public void RefreshDisplay() { Draw(roundNumber); }

        private IEnumerator Flip(int value)
        {
            float half = Mathf.Max(.025f, FlipDuration / 2);
            for (float t = 0; t < half; t += Time.unscaledDeltaTime)
            {
                DigitContainer.localScale = new Vector3(1, Mathf.Lerp(1, .04f, t / half), 1);
                yield return null;
            }
            Draw(value);
            for (float t = 0; t < half; t += Time.unscaledDeltaTime)
            {
                DigitContainer.localScale = new Vector3(1, Mathf.Lerp(.04f, 1, t / half), 1);
                yield return null;
            }
            DigitContainer.localScale = Vector3.one; flipRoutine = null;
        }
        private void Draw(int value)
        {
            if (!DigitContainer || DigitSprites == null || DigitSprites.Length != 10 || DigitSlots == null) return;
            string digits = FormatRound(value);
            float totalWidth = FitDigitsToContainer ? DigitContainer.rect.width : Mathf.Min(144, Mathf.Max(74, digits.Length * 35));
            float height = FitDigitsToContainer ? DigitContainer.rect.height : 59;
            float cellWidth = totalWidth / digits.Length;
            for (int i = 0; i < DigitSlots.Length; i++)
            {
                var slot = DigitSlots[i]; if (!slot) continue;
                bool visible = i < digits.Length; slot.gameObject.SetActive(visible); if (!visible) continue;
                slot.sprite = DigitSprites[digits[i] - '0'];
                var rect = slot.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(cellWidth, height);
                rect.anchoredPosition = new Vector2(-totalWidth / 2 + cellWidth * (i + .5f), 0);
            }
        }
    }
}
