using System.Collections;
using System.Linq;
using NUnit.Framework;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Ruin.Tests
{
    public class StageOneFlowTests
    {
        private GameObject root;
        private StageOneApp app;
        [UnitySetUp] public IEnumerator Create()
        {
            root = new GameObject("TestApp"); app = root.AddComponent<StageOneApp>(); app.AutomationMode = true;
            yield return null; yield return null;
        }
        [UnityTearDown] public IEnumerator Destroy()
        {
            Object.Destroy(root);
            foreach (var e in Object.FindObjectsOfType<UnityEngine.EventSystems.EventSystem>()) Object.Destroy(e.gameObject);
            yield return null;
        }

        [UnityTest] public IEnumerator SceneBuildsChineseUIAndActionTargets()
        {
            Assert.NotNull(app.Battle); Assert.AreEqual(4, app.Run.Heroes.Count);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.text == "界限之间"));
            app.SelectAction(ActionKind.Primary); yield return null;
            Assert.AreEqual(ActionKind.Primary, app.Pending);
            string target = app.Battle.Targets(ActionKind.Primary).First().Id;
            app.SelectActor(target);
            int frames = 0; while (app.Busy && frames++ < 400) yield return null;
            Assert.IsFalse(app.Busy); Assert.IsTrue(app.Battle.Events.Any(e => e.Kind == "action" || e.Kind == "miss"));
        }

        [UnityTest] public IEnumerator DebugPresetAndFeedbackContinueThroughUI()
        {
            app.NewRun(85); yield return null;
            Assert.IsTrue(app.Run.Heroes.All(h => h.Sanity == 85));
            app.DebugMental(MentalState.Composed); app.DebugSetSanity(50);
            var selected = app.Battle.Find(app.InspectedId);
            app.DebugSettle(); yield return null;
            Assert.IsTrue(Object.FindObjectsOfType<Button>().Any(b => b.name == "ContinueBattle"));
            app.ContinueBattle(); yield return null;
            Assert.AreEqual(50, selected.Sanity); Assert.AreEqual(MentalState.Composed, selected.Mental);
            app.DebugFeedbackExample(); yield return null;
            Assert.AreEqual(47.5, app.Run.PlayerSanity);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "PlayerResult" && t.text.Contains("47.5")));
        }
    }
}
