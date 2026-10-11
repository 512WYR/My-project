using Ruin.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // Vector silhouettes keep the prototype self-contained and easily replaceable by sprites.
    public sealed class SilhouetteGraphic : Graphic
    {
        public string Silhouette;
        public EnemyPattern Enemy;
        public bool IsEnemy;
        public Color Accent = new Color(0.7f, 0.7f, 0.7f);
        private VertexHelper mesh;
        private Vector2 origin;
        private Vector2 scale;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); mesh = vh;
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
