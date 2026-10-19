using System.Collections;
using Ruin.Runtime;
using UnityEngine;

namespace GameMake.EventUI
{
    public sealed class EventPreviewStart : MonoBehaviour
    {
        public StageOneApp Source;
        IEnumerator Start()
        {
            while(Source && Source.Run==null) yield return null;
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !RUIN_DEBUG_OFF
            if(Source) Source.DebugForceEvent("EVT-003");
#endif
        }
    }
}
