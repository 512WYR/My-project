using UnityEngine;
using UnityEngine.UI;

namespace GameMake.ExplorationUI
{
    // Straight UI geometry stays sharp at any canvas scale; no baked map screenshot.
    [ExecuteAlways, RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExplorationRouteGraphic : MaskableGraphic
    {
        public Vector2[] Starts = new Vector2[0], Ends = new Vector2[0];
        public Color[] Tints = new Color[0];
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i < Starts.Length && i < Ends.Length; i++)
            {
                Vector2 a = Starts[i], b = Ends[i];
                a.y = -a.y; b.y = -b.y;
                Vector2 n = new Vector2(-(b-a).y, (b-a).x).normalized * 1.2f;
                int offset = vh.currentVertCount;
                Color tint = i < Tints.Length ? Tints[i] : color;
                vh.AddVert(a-n, tint, Vector2.zero); vh.AddVert(a+n, tint, Vector2.zero);
                vh.AddVert(b+n, tint, Vector2.zero); vh.AddVert(b-n, tint, Vector2.zero);
                vh.AddTriangle(offset, offset+1, offset+2); vh.AddTriangle(offset, offset+2, offset+3);
            }
        }
    }
}
