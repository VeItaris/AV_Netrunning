using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;
using Verse;
using Unity.Jobs;
using static HarmonyLib.Code;

namespace AV_Netrunning
{
    public class WorkGiver_Netrunning : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerBuildings.allBuildingsColonist;
        }

        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !pawn.health.hediffSet.HasHediff(HediffDefOf.MechlinkImplant) || !pawn.health.hediffSet.HasHediff(ListOfMechtech_HediffDefs.AV_NeuralInterface); //needs both hediffs to not skip
        }


        public static bool IsValidCryopodFor(Thing bedThing, Pawn sleeper, Pawn traveler, bool checkSocialProperness = false, bool allowMedBedEvenIfSetToNoCare = false, bool ignoreOtherReservations = false, GuestStatus? guestStatus = null)
        {
            //confirm cryopod

            if (bedThing is Building_Bed)
            {
                CheckModExtensions CheckModExt = new CheckModExtensions();
                if (!CheckModExt.IsCryopod(bedThing.def))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if (!RestUtility.CanUseBedNow(bedThing, sleeper, checkSocialProperness, allowMedBedEvenIfSetToNoCare, guestStatus))
            {
                return false;
            }

            Building_Bed building_Bed = bedThing as Building_Bed;
            if (!traveler.CanReserveAndReach(building_Bed, PathEndMode.OnCell, Danger.Some, building_Bed.SleepingSlotsCount, -1, null, ignoreOtherReservations))
            {
                return false;
            }

            if (traveler.HasReserved<JobDriver_TakeToBed>(building_Bed, sleeper))
            {
                return false;
            }

            if (building_Bed.IsForbidden(traveler))
            {
                return false;
            }
            return true;
        }


        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if(IsValidCryopodFor(t, pawn, pawn))
            {
                CompPowerTrader PowerTrader = t.TryGetComp<CompPowerTrader>();
                CompRefuelable Refuelable = t.TryGetComp<CompRefuelable>();
                if (!PowerTrader.PowerOn || !Refuelable.HasFuel)
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(ListOfMechtech_JobDefs.Netrunning, t);
        }
    }


}
