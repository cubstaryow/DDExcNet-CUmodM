using System.Collections.Generic;
using UnityEngine;
namespace DDEN.LoongNova
{
    // Screen-space quads avoid world bloom, color grading and one-pixel hardware lines.
    internal sealed class TacticalLineMesh
    {
        private struct Segment { internal Vector2 A,B; internal Color Color; }
        private List<Segment> _front=new List<Segment>(4096),_back=new List<Segment>(4096);
        private readonly List<Segment> _projected=new List<Segment>(4096);
        private readonly TacticalCanvasSurface _shadow,_color;
        private readonly float _width;
        private bool _shown;
        internal TacticalLineMesh(string name,int order,Transform owner)
        { _width=order>=96?2.5f:2f;_shadow=new TacticalCanvasSurface(name+" outline");_color=new TacticalCanvasSurface(name); }
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
        internal void Show(bool show){_shown=show;_shadow.Show(show);_color.Show(show);}
        internal void Draw(Camera camera)
        {
            if(!_shown||camera==null)return;
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
                Project(a,c,line.Color);
            }
            _shadow.Begin();_color.Begin();
            foreach(var line in _projected)_shadow.Line(line.A,line.B,_width+2.5f,new Color(0.015f,0.025f,0.035f,0.94f));
            foreach(var line in _projected)_color.Line(line.A,line.B,_width,line.Color);
            _shadow.Finish();_color.Finish();
        }
        private void Project(Vector2 a,Vector2 c,Color color)=>_projected.Add(new Segment{A=a,B=c,Color=color});
        internal void Dispose(){_shadow.Dispose();_color.Dispose();_front.Clear();_back.Clear();_projected.Clear();}
    }
}
