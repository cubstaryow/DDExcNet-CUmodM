using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DDEN.LoongNova
{
    // Native canvas child: below the game's panels, buttons and status icons, never an IMGUI overlay.
    public sealed class TacticalCanvasGraphic : MaskableGraphic
    {
        internal readonly List<UIVertex> Vertices=new List<UIVertex>(8192);
        internal readonly List<int> Indices=new List<int>(12288);
        protected override void OnPopulateMesh(VertexHelper helper)
        {helper.Clear();if(Vertices.Count>0)helper.AddUIVertexStream(Vertices,Indices);}
        internal void ClearGeometry(){Vertices.Clear();Indices.Clear();}
        internal void Finish()=>SetVerticesDirty();
    }
    internal sealed class TacticalCanvasSurface
    {
        private static RectTransform _root;
        private TacticalCanvasGraphic _graphic;
        private readonly string _name;
        private readonly List<TextMeshProUGUI> _labels=new List<TextMeshProUGUI>();
        private int _usedLabels;
        private Vector2 _origin,_x,_y;
        private static RectTransform Root(Canvas canvas)
        {
            if(_root==null)_root=new GameObject("LoongNova tactical HUD",typeof(RectTransform),typeof(Canvas)).GetComponent<RectTransform>();
            _root.GetComponent<Canvas>().overrideSorting=false;
            if(_root.parent!=canvas.transform)_root.SetParent(canvas.transform,false);
            if(_root.GetSiblingIndex()!=0)_root.SetAsFirstSibling();
            _root.anchorMin=Vector2.zero;_root.anchorMax=Vector2.one;_root.offsetMin=_root.offsetMax=Vector2.zero;
            return _root;
        }
        internal TacticalCanvasSurface(string name)
        {
            _name=name; Create();
        }
        private void Create()
        {
            _labels.Clear();
            var go=new GameObject(_name,typeof(RectTransform),typeof(CanvasRenderer),typeof(TacticalCanvasGraphic));
            _graphic=go.GetComponent<TacticalCanvasGraphic>();_graphic.raycastTarget=false;
        }
        internal void Begin()
        {
            if(_graphic==null)Create();
            var canvas=PlayerCamera.main.mainCanvas;var root=Root(canvas);
            if(_graphic.transform.parent!=root)_graphic.transform.SetParent(root,false);
            var rect=_graphic.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            Camera camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,new Vector2(0,Screen.height),camera,out _origin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,new Vector2(Screen.width,Screen.height),camera,out var right);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,Vector2.zero,camera,out var down);
            _x=(right-_origin)/Screen.width;_y=(down-_origin)/Screen.height;
            _graphic.ClearGeometry();_usedLabels=0;Show(true);
        }
        private Vector2 Local(Vector2 point)=>_origin+_x*point.x+_y*point.y;
        internal void Quad(Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color color)
        {
            int i=_graphic.Vertices.Count;if(i>63996)return;
            Add(a,color);Add(b,color);Add(c,color);Add(d,color);
            _graphic.Indices.Add(i);_graphic.Indices.Add(i+1);_graphic.Indices.Add(i+2);
            _graphic.Indices.Add(i);_graphic.Indices.Add(i+2);_graphic.Indices.Add(i+3);
        }
        private void Add(Vector2 point,Color color)
        {var vertex=UIVertex.simpleVert;vertex.position=Local(point);vertex.color=color;_graphic.Vertices.Add(vertex);}
        internal void Line(Vector2 a,Vector2 b,float width,Color color)
        {
            var v=b-a;if(v.sqrMagnitude<0.01f)return;
            var n=new Vector2(-v.y,v.x).normalized*(width*0.5f);Quad(a+n,b+n,b-n,a-n,color);
        }
        internal void Rect(Rect rect,Color color)=>Quad(new Vector2(rect.xMin,rect.yMin),new Vector2(rect.xMax,rect.yMin),new Vector2(rect.xMax,rect.yMax),new Vector2(rect.xMin,rect.yMax),color);
        internal void Label(Rect rect,string text)
        {
            if(_usedLabels==_labels.Count)
            {
                var label=new GameObject("Tactical label",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                label.transform.SetParent(_graphic.transform,false);label.raycastTarget=false;label.fontSize=14;label.fontStyle=FontStyles.Bold;
                label.alignment=TextAlignmentOptions.Center;label.enableWordWrapping=false;
                if(PlayerCamera.main.statusText!=null)label.font=PlayerCamera.main.statusText.font;
                _labels.Add(label);
            }
            var item=_labels[_usedLabels++];item.gameObject.SetActive(true);if(item.text!=text)item.text=text;
            var transform=item.rectTransform;transform.anchorMin=transform.anchorMax=new Vector2(0.5f,0.5f);
            transform.pivot=new Vector2(0.5f,0.5f);transform.anchoredPosition=Local(rect.center);
            transform.sizeDelta=new Vector2(_x.magnitude*rect.width,_y.magnitude*rect.height);item.fontSize=14*_y.magnitude;item.color=Color.white;
        }
        internal void Finish(){for(int i=_usedLabels;i<_labels.Count;i++)_labels[i].gameObject.SetActive(false);_graphic.Finish();}
        internal void Show(bool shown){if(_graphic!=null&&_graphic.gameObject.activeSelf!=shown)_graphic.gameObject.SetActive(shown);}
        internal void Dispose(){if(_graphic!=null)Object.Destroy(_graphic.gameObject);}
        internal static void ReleaseRoot(){if(_root!=null)Object.Destroy(_root.gameObject);_root=null;}
    }
}
