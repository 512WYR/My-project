using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameMake.SharedUI
{
    // Refract the already drawn screen, including its text, without consuming gameplay randomness.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RetroScreenNoise : MaskableGraphic
    {
        public Material DistortionMaterial;
        [Range(1,8)] public int BandCount=1;
        public Vector2 ThicknessRange=new Vector2(.6f,1.2f);
        public Vector2 SpeedRange=new Vector2(260,420);
        public Vector2 DisplacementRange=new Vector2(2,4);
        public Vector2 ReappearDelayRange=new Vector2(3,5);
        public bool Animate=true;
        readonly Vector4[] bands=new Vector4[8];
        readonly float[] speeds=new float[8];
        readonly float[] cooldowns=new float[8];
        System.Random random;
        Material instance;
        protected override void OnEnable()
        {
            base.OnEnable(); raycastTarget=false;
            if(!Application.isPlaying) { material=DistortionMaterial; return; }
            random=new System.Random(unchecked(Environment.TickCount*397 ^ GetInstanceID()));
            if(DistortionMaterial)
            {
                instance=new Material(DistortionMaterial) { hideFlags=HideFlags.HideAndDontSave };
                material=instance;
            }
            for(int i=0;i<bands.Length;i++)
            {
                Respawn(i,1);
                cooldowns[i]=i*Range(ReappearDelayRange.x,ReappearDelayRange.y);
                if(cooldowns[i]>0) bands[i].x=-2;
            }
            Upload(); SetVerticesDirty();
        }
        protected override void OnDisable()
        {
            material=null;
            if(instance)
            {
                if(Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
            }
            instance=null; base.OnDisable();
        }
        float Range(float min,float max)=>Mathf.Lerp(min,Mathf.Max(min,max),(float)random.NextDouble());
        void Respawn(int index,float y)
        {
            float height=Mathf.Max(1,rectTransform.rect.height);
            bands[index]=new Vector4(y,Range(ThicknessRange.x,ThicknessRange.y)/height,
                Range(DisplacementRange.x,DisplacementRange.y)*(random.NextDouble()<.5?-1:1),Range(.14f,.20f));
            speeds[index]=Range(SpeedRange.x,SpeedRange.y);
        }
        void Update()
        {
            if(!Animate || !instance) return;
            float height=Mathf.Max(1,rectTransform.rect.height);
            float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);
            for(int i=0;i<Mathf.Clamp(BandCount,1,8);i++)
            {
                if(cooldowns[i]>0)
                {
                    cooldowns[i]=Mathf.Max(0,cooldowns[i]-Time.unscaledDeltaTime);
                    if(cooldowns[i]<=0) Respawn(i,1);
                    continue;
                }
                bands[i].x-=speeds[i]*dt/height;
                if(bands[i].x < -bands[i].y)
                {
                    bands[i].x=-2;
                    cooldowns[i]=Mathf.Max(.1f,Range(ReappearDelayRange.x,ReappearDelayRange.y));
                }
            }
            Upload();
        }
        void Upload()
        {
            if(!instance) return;
            instance.SetVectorArray("_Bands",bands);
            instance.SetInt("_BandCount",Mathf.Clamp(BandCount,1,8));
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r=rectTransform.rect;
            vh.AddVert(new Vector3(r.xMin,r.yMin),Color.white,new Vector2(0,0));
            vh.AddVert(new Vector3(r.xMin,r.yMax),Color.white,new Vector2(0,1));
            vh.AddVert(new Vector3(r.xMax,r.yMax),Color.white,new Vector2(1,1));
            vh.AddVert(new Vector3(r.xMax,r.yMin),Color.white,new Vector2(1,0));
            vh.AddTriangle(0,1,2); vh.AddTriangle(0,2,3);
        }
    }
}
