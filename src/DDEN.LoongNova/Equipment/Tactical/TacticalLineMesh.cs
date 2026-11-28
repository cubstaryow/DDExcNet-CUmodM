using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace DDEN.LoongNova
{
    // Screen-space quads avoid world bloom, color grading and one-pixel hardware lines.
    internal sealed class TacticalLineMesh
    {
        private struct Segment { internal Vector2 A,B; internal Color Color; }
        private List<Segment> _front=new List<Segment>(4096),_back=new List<Segment>(4096);
        private readonly List<Segment> _projected=new List<Segment>(4096);
        private readonly Material _material;
        private readonly float _width;
        private bool _shown;
        internal TacticalLineMesh(string name,int order,Transform owner)
        {
            _width=order>=96?2.5f:2f;
            _material=new Material(Shader.Find("Hidden/Internal-Colored"));
            _material.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull",(int)CullMode.Off);_material.SetInt("_ZWrite",0);
            _material.SetInt("_ZTest",(int)CompareFunction.Always);
        }
        internal void Begin()=>_back.Clear();
        internal void Line(Vector2 a,Vector2 b,Color color)
        {if(_back.Count<16384)_back.Add(new Segment{A=a,B=b,Color=color});}
        internal void Box(Bounds bounds,Color color)
        {
            Vector2 a=bounds.min,b=bounds.max;
            Line(a,new Vector2(b.x,a.y),color);Line(new Vector2(b.x,a.y),b,color);
            Line(b,new Vector2(a.x,b.y),color);Line(new Vector2(a.x,b.y),a,color);
        }
        internal void Commit(){var previous=_front;_front=_back;_back=previous;}
        internal void Show(bool show)=>_shown=show;
        internal void Draw(Camera camera,Rect protectedArea)
        {
            if(!_shown||_front.Count==0||camera==null||!_material.SetPass(0))return;
            _projected.Clear();
            foreach(var line in _front)
            {
                var a=camera.WorldToScreenPoint(line.A);var c=camera.WorldToScreenPoint(line.B);
                if(a.z<=0||c.z<=0)continue;
                a.y=Screen.height-a.y;c.y=Screen.height-c.y;
                if(Mathf.Max(a.x,c.x)<-8||Mathf.Min(a.x,c.x)>Screen.width+8||Mathf.Max(a.y,c.y)<-8||Mathf.Min(a.y,c.y)>Screen.height+8)continue;
                // Terrain edges use stable pixel centers; oblique weapon beams retain their true angle.
                if(Mathf.Abs(a.x-c.x)<0.01f||Mathf.Abs(a.y-c.y)<0.01f)
                {a.x=Mathf.Round(a.x)+0.5f;a.y=Mathf.Round(a.y)+0.5f;c.x=Mathf.Round(c.x)+0.5f;c.y=Mathf.Round(c.y)+0.5f;}
                if(TacticalScreenGeometry.Span(a.x,a.y,c.x,c.y,protectedArea.xMin-5,protectedArea.yMin-5,protectedArea.xMax+5,protectedArea.yMax+5,out float enter,out float exit))
                {
                    if(enter>0)Project(a,Vector2.Lerp(a,c,enter),line.Color);
                    if(exit<1)Project(Vector2.Lerp(a,c,exit),c,line.Color);
                }
                else Project(a,c,line.Color);
            }
            GL.PushMatrix();
            try
            {
                GL.LoadPixelMatrix(0,Screen.width,Screen.height,0);GL.Begin(GL.QUADS);
                try
                {
                    // Draw all under-strokes first. Later segments cannot erase earlier colored corners.
                    foreach(var line in _projected)Quad(line.A,line.B,_width+2.5f,new Color(0.015f,0.025f,0.035f,0.94f));
                    foreach(var line in _projected)Quad(line.A,line.B,_width,line.Color);
                }
                finally{GL.End();}
            }
            finally{GL.PopMatrix();}
        }
        private void Project(Vector2 a,Vector2 c,Color color)=>_projected.Add(new Segment{A=a,B=c,Color=color});
        private static void Quad(Vector2 a,Vector2 b,float width,Color color)
        {
            var d=b-a;if(d.sqrMagnitude<0.01f)return;
            var n=new Vector2(-d.y,d.x).normalized*(width*0.5f);GL.Color(color);
            GL.Vertex3(a.x+n.x,a.y+n.y,0);GL.Vertex3(b.x+n.x,b.y+n.y,0);
            GL.Vertex3(b.x-n.x,b.y-n.y,0);GL.Vertex3(a.x-n.x,a.y-n.y,0);
        }
        internal void Dispose(){Object.Destroy(_material);_front.Clear();_back.Clear();}
    }
}
