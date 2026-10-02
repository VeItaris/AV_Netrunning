using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;


namespace AV_Netrunning
{
    public class AV_NetrunningMod : Mod
    {
        private static AV_NetrunningMod _instance;

        public static AV_NetrunningMod Instance => _instance;

        public AV_NetrunningMod(ModContentPack content)
            : base(content)
        {
            Harmony harmony = new Harmony("AV_Netrunning");
            harmony.PatchAll();
            _instance = this;
        }
    }
   

    [StaticConstructorOnStartup]
    public static class Pawn_GetInspectString_Patch
    {
        [HarmonyPatch(typeof(Pawn))]
        [HarmonyPatch("GetInspectString")]
        public static class Pawn_GetInspectString
        {
            [HarmonyPostfix]
            public static void Postfix(ref String __result, Pawn __instance)
            {
                String cryotext = "";
                if (__instance.health != null && __instance.health.hediffSet != null)
                {
                    if(__instance.health.hediffSet.HasHediff(ListOfMechtech_HediffDefs.AV_NeuralInterface))
                    {
                        Hediff hediff = __instance.health.hediffSet.GetFirstHediffOfDef(ListOfMechtech_HediffDefs.AV_NeuralInterface);
                        HediffComp_Cryogenicpod cryopodcomp = hediff.TryGetComp<HediffComp_Cryogenicpod>();

                        if (cryopodcomp.useCustomInspectorString && !cryopodcomp.inspectorString.NullOrEmpty())
                        {
                            cryotext = cryopodcomp.inspectorString.CapitalizeFirst();
                            if (NetrunningSettings.DebugLogging)
                            {
                                cryotext += " (HARMONY)";
                            }
                        }
                        else
                        {
                            if(NetrunningSettings.DebugLogging)
                            {
                                cryotext = "(HARMONY-Debug): I'm not allowed to show somethin useful.";
                            }
                        }
                    }
                }

                if(!cryotext.NullOrEmpty())
                {

                    StringBuilder stringBuilder = new StringBuilder();
                    stringBuilder.AppendLine("\n" + cryotext);

                    __result += stringBuilder.ToString().TrimEndNewlines();
                }
            }
        }
     }


    [StaticConstructorOnStartup]
    public static class RestUtility_GetGizmos_Patch
    {
        [HarmonyPatch(typeof(RestUtility))]
        [HarmonyPatch("IsValidBedFor")]
        public static class RestUtility_IsValidBedFor
        {
            [HarmonyPostfix]
            public static void Postfix(ref bool __result, Thing bedThing, Pawn traveler)
            {
                Pawn pawn = traveler;
                if (__result == true)
                {
                    if (bedThing.def?.GetModExtension<ModExtension_Cryopod>() != null)
                    {
                        if (bedThing.def.GetModExtension<ModExtension_Cryopod>().isCryopod == true)
                        {
                            if(pawn.IsPrisoner)
                            {
                                //stay true if cryopod is in prison cell, allows prisoners sleeping in cryopod
                                return;
                            }

                            if (pawn.ownership?.OwnedBed == bedThing)
                            {
                                //stay true if cryopod is assigned, allow sleeping in cryopod
                                return;
                            }
                            else
                            {
                                __result = false;
                                return;
                            }
                        }
                        else
                        {
                            __result = false;
                            return;
                        }
                    }
                }
            }
        }
    }


}



