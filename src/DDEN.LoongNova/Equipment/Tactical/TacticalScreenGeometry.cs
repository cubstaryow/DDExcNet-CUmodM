using System;
namespace DDEN.LoongNova
{
    internal static class TacticalScreenGeometry
    {
        internal static bool Span(float ax,float ay,float bx,float by,float left,float top,float right,float bottom,out float enter,out float exit)
        {
            enter=0;exit=1;
            return Axis(ax,bx-ax,left,right,ref enter,ref exit)&&Axis(ay,by-ay,top,bottom,ref enter,ref exit);
        }
        private static bool Axis(float origin,float direction,float min,float max,ref float enter,ref float exit)
        {
            if(Math.Abs(direction)<0.00001f)return origin>=min&&origin<=max;
            float a=(min-origin)/direction,b=(max-origin)/direction;if(a>b){float t=a;a=b;b=t;}
            enter=Math.Max(enter,a);exit=Math.Min(exit,b);return enter<=exit;
        }
        internal static void Edge(float ox,float oy,float tx,float ty,float left,float top,float right,float bottom,out float px,out float py)
        {
            float dx=tx-ox,dy=ty-oy;
            float x=Math.Abs(dx)<0.00001f?float.PositiveInfinity:((dx>0?right:left)-ox)/dx;
            float y=Math.Abs(dy)<0.00001f?float.PositiveInfinity:((dy>0?bottom:top)-oy)/dy;
            float scale=Math.Max(0,Math.Min(x,y));if(float.IsInfinity(scale))scale=0;
            px=ox+dx*scale;py=oy+dy*scale;
        }
    }
}
