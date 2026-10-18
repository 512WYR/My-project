using UnityEngine;
using UnityEngine.UI;

namespace GameMake.CharacterUI
{
    // A telescoping mechanical connector drawn from the screen edge to the window.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CharacterLinkGraphic : MaskableGraphic
    {
        public Vector2 StartPoint, EndPoint;
        public float Progress;
        public void SetConnection(Vector2 start,Vector2 end,float progress)
        {
            if(StartPoint==start && EndPoint==end && Mathf.Approximately(Progress,progress)) return;
            StartPoint=start; EndPoint=end; Progress=progress; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if(Progress<=0) return;
            var bend=new Vector2(EndPoint.x-18,StartPoint.y);
            var bendEnd=new Vector2(bend.x,EndPoint.y);
            var points=new[]{StartPoint,bend,bendEnd,EndPoint};
            float total=0; for(int i=1;i<points.Length;i++) total+=Vector2.Distance(points[i-1],points[i]);
            float remaining=total*Progress; Vector2 tip=StartPoint;
            for(int i=1;i<points.Length && remaining>0;i++)
            {
                float length=Vector2.Distance(points[i-1],points[i]);
                if(length<.01f) continue;
                tip=Vector2.Lerp(points[i-1],points[i],Mathf.Min(1,remaining/length));
                Rail(vh,points[i-1],tip); remaining-=length;
            }
            Joint(vh,StartPoint); Joint(vh,tip);
        }
        static void Rail(VertexHelper vh,Vector2 a,Vector2 b)
        {
            Bar(vh,a,b,22,new Color32(35,29,23,255));
            Bar(vh,a,b,18,new Color32(153,133,105,255));
            Bar(vh,a,b,12,new Color32(21,22,20,255));
            Bar(vh,a,b,2,new Color32(198,183,151,255));
        }
        static void Joint(VertexHelper vh,Vector2 p)
        {
            Bar(vh,p-new Vector2(7,0),p+new Vector2(7,0),28,new Color32(163,145,114,255));
            Bar(vh,p-new Vector2(4,0),p+new Vector2(4,0),20,new Color32(28,26,23,255));
            Bar(vh,p-new Vector2(2,0),p+new Vector2(2,0),8,new Color32(172,60,43,255));
        }
        static void Bar(VertexHelper vh,Vector2 a,Vector2 b,float width,Color32 color)
        {
            var delta=b-a; if(delta.sqrMagnitude<.0001f) return;
            var normal=new Vector2(-delta.y,delta.x).normalized*(width*.5f); int i=vh.currentVertCount;
            vh.AddVert(a-normal,color,Vector2.zero); vh.AddVert(a+normal,color,Vector2.zero);
            vh.AddVert(b+normal,color,Vector2.zero); vh.AddVert(b-normal,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2); vh.AddTriangle(i,i+2,i+3);
        }
    }
}
