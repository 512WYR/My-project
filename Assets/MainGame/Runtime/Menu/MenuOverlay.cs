using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // The two global menus: the title screen shown on launch and the pause box opened with Esc.
    // Both are plain UGUI built at runtime, above every page canvas, so neither the scene nor the
    // Build Settings need a second entry. The component lives on StageOneApp's object, which keeps
    // its lifetime tied to the run and lets PlayMode tests skip it entirely through AutomationMode.
    public sealed class MenuOverlay : MonoBehaviour
    {
        private const int StartMenuOrder = 95;
        private const int PauseOrder = 96;
        private const string EntryScenePath = "Assets/Scenes/mainScene.unity";

        private StageOneApp app;
        private Canvas startCanvas;
        private Canvas pauseCanvas;
        private RectTransform startMain;
        private RectTransform startSettings;
        private Slider volumeSlider;
        private Text volumeValue;
        private Slider sfxSlider;
        private Text sfxValue;
        private bool built;

        // Gameplay pages hidden by the title screen. Their previous active flags are recorded so a
        // page that was already closed (the character panel, the debug console) stays closed.
        private readonly List<GameObject> hiddenPages = new List<GameObject>();
        private readonly List<bool> hiddenPageWasActive = new List<bool>();
        private bool pagesHidden;

        public bool StartMenuOpen { get { return startCanvas && startCanvas.gameObject.activeSelf; } }
        public bool PauseOpen { get { return pauseCanvas && pauseCanvas.gameObject.activeSelf; } }

        public static MenuOverlay Ensure(StageOneApp owner)
        {
            var overlay = owner.GetComponent<MenuOverlay>();
            if (!overlay) overlay = owner.gameObject.AddComponent<MenuOverlay>();
            overlay.Bind(owner);
            overlay.BuildOnce();
            return overlay;
        }

        // Shown only from the real entry point. Tests drive the app directly and preview scenes open
        // individual pages, so neither should be frozen behind a title screen and a zero time scale.
        public static bool ShouldShowStartMenu(StageOneApp owner)
        {
            if (!owner || owner.AutomationMode) return false;
            // The Play Mode verifications open mainScene and drive it with scaled-time coroutines;
            // a title screen with a zero time scale would freeze them. They run in batch mode.
            if (Application.isBatchMode) return false;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            return scene.path == EntryScenePath;
        }

        private void Bind(StageOneApp owner)
        {
            app = owner;
        }

        private void BuildOnce()
        {
            if (built) return;
            built = true;
            BuildStartMenu();
            BuildPauseMenu();
            startCanvas.gameObject.SetActive(false);
            pauseCanvas.gameObject.SetActive(false);
        }

        private void Update()
        {
            // Settings is a sub-page, not a modal of its own: Esc backs out to the title, which keeps
            // the in-game Esc rule (only close the pause box / open it) untouched.
            if (!StartMenuOpen || !startSettings || !startSettings.gameObject.activeSelf) return;
            if (Input.GetKeyDown(KeyCode.Escape)) ShowMain();
        }

        // ---- title screen ----

        private void BuildStartMenu()
        {
            startCanvas = MenuUi.MakeCanvas("StartMenuCanvas", StartMenuOrder);

            var scrim = MenuUi.Stretch(startCanvas.transform, "Scrim").gameObject.AddComponent<Image>();
            scrim.color = new Color32(5, 6, 8, 214);
            // Blocks clicks from reaching the route page behind the title screen.
            scrim.raycastTarget = true;

            startMain = MenuUi.Stretch(startCanvas.transform, "Main");
            var title = MenuUi.CenterLabel(startMain, "Title", "锈骨深渊", 0, 230, 1200, 120, 82, MenuUi.Gold);
            AddShadow(title, new Vector2(4, -4), new Color(0f, 0f, 0f, .72f));
            MenuUi.CenterLabel(startMain, "Demo", "demo", 0, 158, 400, 32, 22, MenuUi.Muted);
            MenuUi.CenterSolid(startMain, "TitleRule", 0, 120, 360, 2, MenuUi.Line);

            MenuUi.MenuButton(startMain, "Start", "开始游戏", 38, 360, 64, out _).onClick.AddListener(StartRun);
            MenuUi.MenuButton(startMain, "Settings", "设置", -42, 360, 64, out _).onClick.AddListener(ShowSettings);
            MenuUi.MenuButton(startMain, "Quit", "退出游戏", -122, 360, 64, out _).onClick.AddListener(Quit);

            BuildSettingsPage();
        }

        private void BuildSettingsPage()
        {
            startSettings = MenuUi.Stretch(startCanvas.transform, "Settings");
            MenuUi.CenterLabel(startSettings, "Title", "设置", 0, 230, 800, 90, 60, MenuUi.Gold);
            MenuUi.CenterSolid(startSettings, "TitleRule", 0, 172, 260, 2, MenuUi.Line);

            MenuUi.CenterLabel(startSettings, "VolumeLabel", "音乐音量", 0, 90, 500, 40, 28, MenuUi.Ink);
            volumeSlider = MenuUi.SliderBar(startSettings, "MusicVolume", 34, 420, 14);
            volumeValue = MenuUi.CenterLabel(startSettings, "VolumeValue", "", 0, -12, 200, 32, 22, MenuUi.Muted);

            MenuUi.CenterLabel(startSettings, "SfxLabel", "音效音量", 0, -78, 500, 40, 28, MenuUi.Ink);
            sfxSlider = MenuUi.SliderBar(startSettings, "SfxVolume", -134, 420, 14);
            sfxValue = MenuUi.CenterLabel(startSettings, "SfxValue", "", 0, -180, 200, 32, 22, MenuUi.Muted);

            MenuUi.MenuButton(startSettings, "Back", "返回", -270, 300, 60, out _).onClick.AddListener(ShowMain);

            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);
            RefreshVolume();
        }

        public void ShowStartMenu()
        {
            BuildOnce();
            startCanvas.gameObject.SetActive(true);
            pauseCanvas.gameObject.SetActive(false);
            HideGameplayPages();
            ShowMain();
            ClearSelection();
            ApplyTimeScale();
        }

        // The title screen is not just an opaque layer: every page below it is switched off, so the
        // HUD, the route and the room pages cannot peek through the scrim or steal input. The
        // backdrop canvas (order -1000) is deliberately left alone.
        private void HideGameplayPages()
        {
            if (pagesHidden) return;
            pagesHidden = true;
            hiddenPages.Clear();
            hiddenPageWasActive.Clear();

            var canvases = Object.FindObjectsOfType<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (!canvas || canvas == startCanvas || canvas == pauseCanvas) continue;
                if (canvas.sortingOrder < 0 || canvas.sortingOrder >= StartMenuOrder) continue;

                var page = canvas.gameObject;
                hiddenPages.Add(page);
                hiddenPageWasActive.Add(page.activeSelf);
                page.SetActive(false);
            }
        }

        private void RestoreGameplayPages()
        {
            if (!pagesHidden) return;
            pagesHidden = false;
            for (int i = 0; i < hiddenPages.Count; i++)
                if (hiddenPages[i]) hiddenPages[i].SetActive(hiddenPageWasActive[i]);
            hiddenPages.Clear();
            hiddenPageWasActive.Clear();
        }

        private void ShowMain()
        {
            if (startSettings) startSettings.gameObject.SetActive(false);
            if (startMain) startMain.gameObject.SetActive(true);
            SaveVolume();
        }

        private void ShowSettings()
        {
            if (startMain) startMain.gameObject.SetActive(false);
            if (startSettings) startSettings.gameObject.SetActive(true);
            RefreshVolume();
        }

        private void OnVolumeChanged(float value)
        {
            MusicDirector.SetMasterVolume(value);
            if (volumeValue) volumeValue.text = Percent(value);
        }

        private void OnSfxChanged(float value)
        {
            SfxDirector.SetMasterVolume(value);
            if (sfxValue) sfxValue.text = Percent(value);
        }

        private void RefreshVolume()
        {
            if (volumeSlider)
            {
                volumeSlider.SetValueWithoutNotify(MusicDirector.MasterVolume);
                if (volumeValue) volumeValue.text = Percent(MusicDirector.MasterVolume);
            }
            if (sfxSlider)
            {
                sfxSlider.SetValueWithoutNotify(SfxDirector.MasterVolume);
                if (sfxValue) sfxValue.text = Percent(SfxDirector.MasterVolume);
            }
        }

        private static string Percent(float value) { return Mathf.RoundToInt(value * 100f) + "%"; }

        // PlayerPrefs writes are cheap but flushing to disk on every drag frame is not; the title
        // screen persists when the settings page closes and when the menu is dismissed.
        private static void SaveVolume()
        {
            MusicDirector.SaveMasterVolume();
            SfxDirector.SaveMasterVolume();
        }

        // ---- pause box ----

        private void BuildPauseMenu()
        {
            pauseCanvas = MenuUi.MakeCanvas("PauseMenuCanvas", PauseOrder);

            var scrim = MenuUi.Stretch(pauseCanvas.transform, "Scrim").gameObject.AddComponent<Image>();
            scrim.color = new Color32(4, 5, 6, 160);
            scrim.raycastTarget = true;

            MenuUi.CenterSolid(pauseCanvas.transform, "Frame", 0, 0, 572, 372, MenuUi.Line);
            var panel = MenuUi.CenterSolid(pauseCanvas.transform, "Panel", 0, 0, 566, 366, MenuUi.Panel);
            panel.raycastTarget = true;

            MenuUi.CenterLabel(panel.transform, "Title", "暂停", 0, 118, 400, 64, 46, MenuUi.Gold);
            MenuUi.CenterSolid(panel.transform, "TitleRule", 0, 82, 440, 1, MenuUi.Line);

            MenuUi.MenuButton(panel.transform, "Return", "退回到主菜单", 10, 400, 58, out _).onClick.AddListener(ReturnToMainMenu);
            MenuUi.MenuButton(panel.transform, "Quit", "退出游戏", -70, 400, 58, out _).onClick.AddListener(Quit);
            MenuUi.CenterLabel(panel.transform, "Hint", "按 ESC 继续游戏", 0, -142, 460, 28, 18, MenuUi.Muted);
        }

        public void OpenPause()
        {
            BuildOnce();
            // The title screen already owns the screen; Esc there means "nothing to resume".
            if (StartMenuOpen) return;
            startCanvas.gameObject.SetActive(false);
            pauseCanvas.gameObject.SetActive(true);
            // The scrim stops mouse clicks, but a still-selected HUD button would also eat Space/Enter.
            ClearSelection();
            ApplyTimeScale();
        }

        public void ClosePause()
        {
            if (pauseCanvas) pauseCanvas.gameObject.SetActive(false);
            ClearSelection();
            ApplyTimeScale();
        }

        public void HideAllForGameplay()
        {
            if (startCanvas) startCanvas.gameObject.SetActive(false);
            if (pauseCanvas) pauseCanvas.gameObject.SetActive(false);
            RestoreGameplayPages();
            SaveVolume();
            ApplyTimeScale();
        }

        // A single place decides the time scale, so closing one menu while another is open cannot
        // accidentally unfreeze the game. Music and menu animations use unscaled time.
        private void ApplyTimeScale()
        {
            Time.timeScale = StartMenuOpen || PauseOpen ? 0f : 1f;
        }

        private static void ClearSelection()
        {
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        private void StartRun()
        {
            if (app) app.StartRunFromMenu();
        }

        private void ReturnToMainMenu()
        {
            if (app) app.ReturnToMainMenu();
        }

        private void Quit()
        {
            if (app) app.QuitGame();
        }

        private void OnDestroy()
        {
            if (startCanvas) Destroy(startCanvas.gameObject);
            if (pauseCanvas) Destroy(pauseCanvas.gameObject);
            if (Time.timeScale == 0f) Time.timeScale = 1f;
        }

        private static void AddShadow(Text text, Vector2 distance, Color color)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectDistance = distance;
            shadow.effectColor = color;
            shadow.useGraphicAlpha = true;
        }
    }
}
