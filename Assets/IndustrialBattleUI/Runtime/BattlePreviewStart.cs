using System.Collections;
using Ruin.Core;
using Ruin.Runtime;
using UnityEngine;

namespace GameMake.IndustrialBattleUI
{
    // Only the dedicated battle preview scene uses this. Normal exploration scenes keep their run flow.
    public sealed class BattlePreviewStart : MonoBehaviour
    {
        public StageOneApp Source;
        IEnumerator Start()
        {
            while(Source && Source.Run==null) yield return null;
            if(!Source || Source.Battle!=null) yield break;
            var step=Source.Map.Current;
            step.Candidates.Clear(); step.Candidates.Add(new NodeState{Floor=step.Floor,Index=step.Index,Type=NodeType.Battle});
            Source.SelectNode(0);
        }
    }
}
