using UnityEngine;
using UnityEngine.UI;

namespace GameMake.EventUI
{
    public sealed class EventChoiceView : MonoBehaviour
    {
        public Button Hit;
        public Text Number, Title, Detail;
        public CanvasGroup Fade;
        public int ChoiceIndex;
        public bool Available;
    }
}
