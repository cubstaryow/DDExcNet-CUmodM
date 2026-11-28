using System;
namespace DDEN.LoongNova
{
    internal static class SmelterComfortRules
    {
        internal static float Normalize(float value,float seconds)
        {
            if(float.IsNaN(value)||float.IsInfinity(value)||float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return value;
            float step=seconds*0.1f;
            return value<37?Math.Min(37,value+step):Math.Max(37,value-step);
        }
    }
}
