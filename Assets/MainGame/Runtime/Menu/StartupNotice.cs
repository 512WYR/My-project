using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // The black advisory shown once per launch, before the title screen. It is deliberately
    // unskippable: the AI disclosure should reach every player, so the page ignores input and
    // plays a fixed realtime fade-in, hold and fade-out. Built at runtime like the menus, above
    // both of their canvases, and destroyed as soon as the sequence ends.
    public sealed class StartupNotice : MonoBehaviour
    {
        // Above the title screen (95) and the pause box (96) while it is up.
        private const int SortingOrder = 97;
        // Three seconds in total: content fades in, holds, then the whole black page fades out to
        // reveal the title screen. The holds are realtime because the title screen freezes time.
        private const float TotalSeconds = 3f;
        private const float FadeSeconds = .35f;
        private const float HoldSeconds = TotalSeconds - FadeSeconds * 2f;

        private Canvas canvas;
        private CanvasGroup pageGroup;
        private CanvasGroup contentGroup;

        // Called only from the real entry path; MenuOverlay.ShouldShowStartMenu has already ruled
        // out tests, batch runs and preview scenes before the notice is created.
        public static void Show(MonoBehaviour host)
        {
            var notice = host.gameObject.AddComponent<StartupNotice>();
            notice.Build();
        }

        private void Build()
        {
            canvas = MenuUi.MakeCanvas("StartupNoticeCanvas", SortingOrder);
            // The page starts fully opaque so the title screen never flashes before the notice.
            // Only the content fades in; the final fade runs on this group to uncover the title.
            pageGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            pageGroup.alpha = 1f;
            pageGroup.blocksRaycasts = true;

            var black = MenuUi.Stretch(canvas.transform, "Black").gameObject.AddComponent<Image>();
            black.color = new Color32(0, 0, 0, 255);
            // The opaque plate also swallows clicks, so the title screen underneath cannot be
            // operated while the notice is still counting down.
            black.raycastTarget = true;

            var content = MenuUi.Stretch(canvas.transform, "Content");
            contentGroup = content.gameObject.AddComponent<CanvasGroup>();
            contentGroup.alpha = 0f;
            contentGroup.blocksRaycasts = false;

            MenuUi.CenterLabel(content, "Title", "健康游戏忠告", 0, 250, 900, 60, 44, MenuUi.Gold);
            MenuUi.CenterSolid(content, "TitleRule", 0, 205, 380, 1, MenuUi.Line);

            var advice = new[]
            {
                "抵制不良游戏，拒绝盗版游戏。",
                "注意自我保护，谨防受骗上当。",
                "适度游戏益脑，沉迷游戏伤身。",
                "合理安排时间，享受健康生活。"
            };
            for (int i = 0; i < advice.Length; i++)
                MenuUi.CenterLabel(content, "Advice" + i, advice[i], 0, 150 - i * 50, 1000, 42, 28, MenuUi.Ink);

            MenuUi.CenterSolid(content, "AiRule", 0, -80, 620, 1, MenuUi.Line);
            MenuUi.CenterLabel(content, "AiTitle", "AI 内容说明", 0, -125, 600, 36, 26, MenuUi.Gold);
            MenuUi.CenterLabel(content, "AiLine1",
                "本游戏的角色立绘与场景背景等美术素材，", 0, -165, 1100, 36, 25, MenuUi.Muted);
            MenuUi.CenterLabel(content, "AiLine2",
                "包含由人工智能（AI）生成或辅助制作的内容。", 0, -198, 1100, 36, 25, MenuUi.Muted);

            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            // Realtime throughout: the title screen behind this page has already set the time
            // scale to zero, so scaled waits or deltas would never advance.
            yield return Fade(contentGroup, 0f, 1f, FadeSeconds);
            yield return new WaitForSecondsRealtime(HoldSeconds);
            yield return Fade(pageGroup, 1f, 0f, FadeSeconds);
            Dismiss();
        }

        private static IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                group.alpha = Mathf.SmoothStep(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            group.alpha = to;
        }

        private void Dismiss()
        {
            if (canvas)
            {
                // Deactivate before the deferred Destroy so the black plate disappears this frame.
                canvas.gameObject.SetActive(false);
                Destroy(canvas.gameObject);
                canvas = null;
            }
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (canvas) Destroy(canvas.gameObject);
        }
    }
}
