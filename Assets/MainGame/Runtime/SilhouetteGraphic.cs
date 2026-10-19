using Ruin.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // Vector silhouettes keep the prototype self-contained and easily replaceable by sprites. When a
    // Portrait texture is assigned the vector body is skipped and the texture is drawn instead, fitted
    // inside the same rect, so every existing builder keeps working without a second Image node.
    //
    // In Unity 2022 Graphic's RequireComponent moved to its concrete subclasses, so a runtime
    // AddComponent<SilhouetteGraphic>() otherwise gets no CanvasRenderer: OnPopulateMesh still builds
    // the mesh, but there is no renderer to submit it. Prefab-authored instances hid this because the
    // editor saved the CanvasRenderer alongside the script.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SilhouetteGraphic : Graphic
    {
        public string Silhouette;
        public EnemyPattern Enemy;
        public bool IsEnemy;
        public Color Accent = new Color(0.7f, 0.7f, 0.7f);
        public Texture2D Portrait;
        private VertexHelper mesh;
        private Vector2 origin;
        private Vector2 scale;

        // The canvas material samples whatever mainTexture reports. Switching between the sprite and the
        // white texture therefore requires the material to be refreshed, not only the mesh.
        public override Texture mainTexture => Portrait != null ? Portrait : base.mainTexture;

        public void SetPortrait(Texture2D portrait)
        {
            if (Portrait == portrait) return;
            Portrait = portrait;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); mesh = vh;
            if (Portrait != null) { DrawPortrait(); return; }
            var r = rectTransform.rect; origin = r.min; scale = new Vector2(r.width / 100, r.height / 160);
            Color shadow = color;
            Poly(shadow, new Vector2(25, 4), new Vector2(41, 4), new Vector2(49, 50), new Vector2(35, 54));
            Poly(shadow, new Vector2(60, 4), new Vector2(76, 4), new Vector2(65, 53), new Vector2(50, 49));
            Poly(shadow, new Vector2(21, 33), new Vector2(77, 33), new Vector2(66, 108), new Vector2(34, 108));
            Circle(new Vector2(50, 130), 16, shadow);
            Poly(shadow, new Vector2(29, 99), new Vector2(16, 57), new Vector2(25, 52), new Vector2(41, 102));
            Poly(shadow, new Vector2(60, 104), new Vector2(82, 62), new Vector2(91, 67), new Vector2(71, 111));
            if (IsEnemy)
            {
                if (Enemy == EnemyPattern.Whisperer)
                {
                    Poly(shadow, new Vector2(7, 24), new Vector2(93, 24), new Vector2(65, 129), new Vector2(35, 129));
                    Poly(shadow, new Vector2(23, 121), new Vector2(49, 158), new Vector2(75, 121));
                    Line(new Vector2(38, 130), new Vector2(62, 130), 3, Accent);
                    Circle(new Vector2(79, 83), 7, Accent);
                }
                else if (Enemy == EnemyPattern.Hunter)
                {
                    Poly(shadow, new Vector2(23, 133), new Vector2(37, 155), new Vector2(70, 150), new Vector2(83, 123));
                    Line(new Vector2(64, 68), new Vector2(95, 33), 7, Accent);
                    Line(new Vector2(30, 70), new Vector2(5, 48), 5, Accent);
                }
                else
                {
                    Poly(shadow, new Vector2(22, 78), new Vector2(80, 78), new Vector2(77, 118), new Vector2(24, 118));
                    Line(new Vector2(88, 34), new Vector2(88, 148), 5, Accent);
                    Poly(Accent, new Vector2(72, 130), new Vector2(97, 125), new Vector2(97, 150), new Vector2(80, 156));
                }
            }
            else if (Silhouette == "shield")
            {
                Poly(Accent, new Vector2(9, 94), new Vector2(40, 99), new Vector2(42, 54), new Vector2(25, 38), new Vector2(8, 55));
                Line(new Vector2(25, 48), new Vector2(25, 92), 2, shadow);
                Line(new Vector2(86, 38), new Vector2(86, 138), 4, Accent);
                Line(new Vector2(35, 133), new Vector2(66, 133), 4, Accent);
                Poly(shadow, new Vector2(37, 142), new Vector2(45, 158), new Vector2(58, 142));
            }
            else if (Silhouette == "gunner")
            {
                Poly(shadow, new Vector2(25, 130), new Vector2(38, 148), new Vector2(65, 143), new Vector2(75, 126));
                Line(new Vector2(21, 81), new Vector2(92, 102), 7, Accent);
                Line(new Vector2(61, 115), new Vector2(77, 65), 5, Accent);
                Line(new Vector2(30, 119), new Vector2(76, 53), 4, Accent);
                Line(new Vector2(42, 133), new Vector2(64, 133), 3, Accent);
            }
            else if (Silhouette == "medic")
            {
                Poly(Accent, new Vector2(39, 115), new Vector2(51, 86), new Vector2(63, 115));
                Poly(shadow, new Vector2(14, 40), new Vector2(86, 40), new Vector2(67, 104), new Vector2(34, 104));
                Poly(Accent, new Vector2(2, 32), new Vector2(28, 32), new Vector2(28, 57), new Vector2(2, 57));
                Line(new Vector2(15, 37), new Vector2(15, 52), 3, shadow);
                Line(new Vector2(7, 44), new Vector2(23, 44), 3, shadow);
                Line(new Vector2(72, 91), new Vector2(95, 110), 3, Accent);
                Line(new Vector2(35, 132), new Vector2(68, 132), 5, Accent);
            }
            else
            {
                Poly(shadow, new Vector2(27, 122), new Vector2(49, 157), new Vector2(73, 123));
                Poly(Accent, new Vector2(29, 107), new Vector2(67, 87), new Vector2(66, 98), new Vector2(33, 117));
                Line(new Vector2(75, 61), new Vector2(97, 95), 5, Accent);
                Line(new Vector2(37, 131), new Vector2(63, 131), 3, Accent);
            }
        }

        // Contain-fit, centred. Portraits come in different aspect ratios and must not be stretched; the
        // card's dark background shows through the leftover space on either side.
        private void DrawPortrait()
        {
            var r = rectTransform.rect;
            float fit = Mathf.Min(r.width / Portrait.width, r.height / Portrait.height);
            var size = new Vector2(Portrait.width * fit, Portrait.height * fit);
            Vector2 min = r.center - size * 0.5f;
            mesh.AddVert(new Vector2(min.x, min.y), color, new Vector2(0, 0));
            mesh.AddVert(new Vector2(min.x, min.y + size.y), color, new Vector2(0, 1));
            mesh.AddVert(new Vector2(min.x + size.x, min.y + size.y), color, new Vector2(1, 1));
            mesh.AddVert(new Vector2(min.x + size.x, min.y), color, new Vector2(1, 0));
            mesh.AddTriangle(0, 1, 2);
            mesh.AddTriangle(2, 3, 0);
        }

        private Vector2 P(Vector2 p) { return origin + Vector2.Scale(p, scale); }
        private void Poly(Color c, params Vector2[] pts)
        {
            int start = mesh.currentVertCount;
            foreach (var pt in pts) mesh.AddVert(P(pt), c, Vector2.zero);
            for (int i = 1; i < pts.Length - 1; i++) mesh.AddTriangle(start, start + i, start + i + 1);
        }
        private void Circle(Vector2 p, float radius, Color c)
        {
            var pts = new Vector2[24];
            for (int i = 0; i < pts.Length; i++) { float a = Mathf.PI * 2 * i / pts.Length; pts[i] = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius; }
            Poly(c, pts);
        }
        private void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized * width / 2;
            Poly(c, a - n, b - n, b + n, a + n);
        }
    }
}
