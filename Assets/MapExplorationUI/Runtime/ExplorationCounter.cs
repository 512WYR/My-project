using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.ExplorationUI
{
    public sealed class ExplorationCounter : MonoBehaviour
    {
        public Sprite[] Digits = new Sprite[10];
        public Image[] FloorSlots;
        public Image[] RoomSlots;
        public RectTransform FloorWindow, RoomWindow;
        public int Floor = 1, Room = 1;
        public bool Animate = true;
        Coroutine flip;

        void OnEnable() { Paint(); }
        void OnDisable()
        {
            if (flip != null) StopCoroutine(flip);
            flip = null;
            if (FloorWindow) FloorWindow.localScale = Vector3.one;
            if (RoomWindow) RoomWindow.localScale = Vector3.one;
        }
        public void SetLocation(int floor, int room)
        {
            floor = Mathf.Max(0, floor); room = Mathf.Max(0, room);
            if (Floor == floor && Room == room) { Paint(); return; }
            Floor = floor; Room = room;
            if (flip != null) StopCoroutine(flip);
            if (Animate && Application.isPlaying && isActiveAndEnabled) flip = StartCoroutine(Flip());
            else Paint();
        }
        IEnumerator Flip()
        {
            for (float t = 0; t < .09f; t += Time.unscaledDeltaTime)
            { Scale(1 - t / .09f); yield return null; }
            Paint();
            for (float t = 0; t < .12f; t += Time.unscaledDeltaTime)
            { Scale(t / .12f); yield return null; }
            Scale(1); flip = null;
        }
        void Scale(float y)
        {
            if (FloorWindow) FloorWindow.localScale = new Vector3(1, Mathf.Max(.02f, y), 1);
            if (RoomWindow) RoomWindow.localScale = new Vector3(1, Mathf.Max(.02f, y), 1);
        }
        public void Paint() { PaintNumber(FloorSlots, Floor); PaintNumber(RoomSlots, Room); }
        void PaintNumber(Image[] slots, int value)
        {
            if (slots == null || Digits == null || Digits.Length != 10) return;
            string number = Mathf.Max(0, value).ToString("D2");
            float step = 104f / number.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                var image = slots[i]; if (!image) continue;
                image.gameObject.SetActive(i < number.Length);
                if (i >= number.Length) continue;
                image.sprite = Digits[number[i] - '0'];
                image.preserveAspect = true;
                float width = Mathf.Min(29, step - 2);
                image.rectTransform.anchoredPosition = new Vector2(step * (i + .5f), -23);
                image.rectTransform.sizeDelta = new Vector2(width, Mathf.Min(46, width * 1.57f));
            }
        }
    }
}
