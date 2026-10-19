using UnityEngine;

namespace Ruin.Runtime
{
    public sealed partial class StageOneApp
    {
        private BattlePresentation presentation;

        // Built once per app, after the catalog exists and before the first run. Batch playmode tests and
        // the editor verification skip the canvas entirely; the animation hook also short-circuits fast.
        private void PreparePresentation()
        {
            if (Application.isBatchMode || AutomationMode) return;
            presentation = BattlePresentation.Ensure(this);
        }

        private void HidePresentation()
        {
            BattleStatusTooltip.HideAll();
            if (presentation) presentation.Hide();
        }
    }
}
