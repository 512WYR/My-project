using UnityEngine;

namespace Ruin.Runtime
{
    // Title screen and pause box glue. Kept out of StageOneApp.cs because everything here is a menu
    // concern: the app only needs to say when a run starts, when it returns to the title, and quit.
    public sealed partial class StageOneApp
    {
        private MenuOverlay menu;

        // Only the real entry scene shows the title screen. A PlayMode test sets AutomationMode before
        // Start and a page preview scene has a different path, so both keep the old direct-to-route
        // start with a running clock.
        private void PrepareStartMenu()
        {
            if (!MenuOverlay.ShouldShowStartMenu(this)) return;
            menu = MenuOverlay.Ensure(this);
            menu.ShowStartMenu();
        }

        public void StartRunFromMenu()
        {
            // NewRun hides the menus itself, which restores the clock before the first route render.
            NewRun();
        }

        public void OpenPauseMenu()
        {
            if (Run == null) return;
            if (!menu) menu = MenuOverlay.Ensure(this);
            menu.OpenPause();
        }

        public void ClosePauseMenu()
        {
            if (menu) menu.ClosePause();
        }

        // Leaving the fight without resetting the run: stop every enemy coroutine, drop the battle and
        // room state, and park on the route. Run stays non-null because the views expect it, and
        // AwaitingNode=true lets MusicDirector fade back to the exploration stem on its own.
        public void ReturnToMainMenu()
        {
            if (Run == null) return;

            StopAllCoroutines();
            Busy = false;
            ClearPending();
            SupplyShopOpen = false; SupplyShopNote = null;
            CharacterPanelOpen = false; CharacterHeroId = null;
            Battle = null;
            EventBattle = null;
            ActiveNode = null;
            nodeGainsTracked = false;
            battleRewardsSettled = false;
            battleRewardNote = null;
            AwaitingNode = true;
            Room = RoomView.Map;
            RoomNote = null;
            MapNote = null;
            Refresh();

            if (!menu) menu = MenuOverlay.Ensure(this);
            menu.ShowStartMenu();
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            // Application.Quit does nothing in the editor; stopping Play is the equivalent.
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
