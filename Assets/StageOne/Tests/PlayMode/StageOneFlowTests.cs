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

        // Clicks the real Button instead of calling the app method, so a broken onClick wiring
        // (a closure over a shared loop variable, for example) cannot pass the test.
        private void Click(string name)
        {
            var button = Object.FindObjectsOfType<Button>().First(b => b.name == name);
            Assert.IsTrue(button.interactable, "按钮 " + name + " 应当可以点击");
            button.onClick.Invoke();
        }

        [UnityTest] public IEnumerator SceneBuildsChineseUIAndActionTargets()
        {
            Assert.IsTrue(app.AwaitingNode); Assert.IsNull(app.Battle);
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.text == "界限之间"));
            app.SelectNode(0); yield return null;
            Assert.NotNull(app.Battle); Assert.AreEqual(4, app.Run.Heroes.Count);
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
            app.SelectNode(0); yield return null;
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

        [UnityTest] public IEnumerator MapOffersThreeRoomsAndAdvancesAFixedFloor()
        {
            var opening = app.Map.Current;
            Assert.AreEqual(1, opening.Floor); Assert.AreEqual(3, opening.Nodes.Count);
            Assert.AreEqual(NodeType.Battle, opening.Nodes[0].Type, "每层保留一个可玩的普通战斗分支");
            Assert.IsTrue(Object.FindObjectsOfType<Button>().Any(b => b.name == "Node_0"));
            Assert.IsTrue(Object.FindObjectsOfType<Text>().Any(t => t.name == "MapOddsHeading"));

            // 已展示的层不重抽：重新渲染既不改房间也不消耗随机序列。
            var types = opening.Nodes.Select(n => n.Type).ToArray();
            uint state = app.Run.Random.State;
            app.Refresh(); yield return null;
            Assert.That(app.Map.Current.Nodes.Select(n => n.Type), Is.EqualTo(types));
            Assert.AreEqual(state, app.Run.Random.State);

            Click("Node_0"); yield return null;
            int frames = 0; while (app.Busy && frames++ < 400) yield return null;
            Assert.IsFalse(app.AwaitingNode); Assert.NotNull(app.Battle);
            app.DebugSettle(); yield return null;
            Click("ContinueBattle"); yield return null;
            Assert.IsTrue(app.AwaitingNode); Assert.IsNull(app.Battle);
            Assert.AreEqual(2, app.Map.Floor); Assert.IsTrue(opening.Resolved);
        }

        [UnityTest] public IEnumerator PlaceholderRoomSkipsAndBossFloorEndsTheRun()
        {
            app.DebugEnterFloor(app.Catalog.MapRules().Floors);
            Assert.AreEqual(1, app.Map.Current.Nodes.Count);
            Assert.AreEqual(NodeType.Boss, app.Map.Current.Nodes[0].Type);
            Click("Node_0"); yield return null;
            Assert.IsTrue(app.Map.Finished); Assert.IsNotNull(app.RunEndNote);
            Click("MapNewRun"); yield return null;
            Assert.IsFalse(app.Map.Finished); Assert.AreEqual(1, app.Map.Floor);
            Assert.AreEqual(3, app.Map.Current.Nodes.Count);
        }
    }
}
