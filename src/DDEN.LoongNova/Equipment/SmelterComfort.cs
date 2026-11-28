using CUCoreLib.Data;
using CUCoreLib.Helpers;
using DDEN.Shared;
using HarmonyLib;
using UnityEngine;
namespace DDEN.LoongNova
{
    [StatusOptions(Key="dden.loongnova.smelter-comfort.v1",SaveEnabled=false,ClientAuthoritative=false)]
    public sealed class SmelterComfort:BodyStatus{public bool Active;}
    [HarmonyPatch(typeof(Body),"Update")]
    internal static class SmelterComfortPatch
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Body,object> Carriers=new System.Runtime.CompilerServices.ConditionalWeakTable<Body,object>();
        internal static bool Carried(Body body)
        {
            if(body==null||!body.alive||body.slots==null)return false;
            // Native slots >2 are back/body carry positions; hands, mouth and nested bags do not count.
            for(int i=3;i<body.slots.Length;i++)
            {
                if(body.slots[i]==null||body.slots[i].isHand)continue;
                var item=body.GetItem(i);
                if(item!=null&&item.id==Registration.SmelterId&&item.condition>0)return true;
            }
            return false;
        }
        [HarmonyPostfix,HarmonyPriority(Priority.Last)]
        private static void Comfortable(Body __instance)
        {
            bool carried=Carried(__instance);
            if(carried)Carriers.GetValue(__instance,_=>new object());
            else if(!Carriers.Remove(__instance))return;
            __instance.GetStatus<SmelterComfort>().Active=carried;
            if(carried&&(!Mp.IsRunning||Mp.IsServer))
                __instance.temperature=SmelterComfortRules.Normalize(__instance.temperature,Time.deltaTime);
        }
    }
}
