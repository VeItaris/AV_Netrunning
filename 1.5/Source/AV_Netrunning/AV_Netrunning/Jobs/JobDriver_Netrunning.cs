using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse.AI;
using Verse;
using UnityEngine.UIElements;
using System.CodeDom.Compiler;
using Verse.Noise;

namespace AV_Netrunning
{
    public class JobDriver_Netrunning : JobDriver
    {

        public const TargetIndex CryopodIndex = TargetIndex.A;
        public Building Cryopod => job.GetTarget(TargetIndex.A).Thing as Building;

        private CompPowerTrader PowerTrader => Cryopod.TryGetComp<CompPowerTrader>();
        private CompRefuelable Refuelable => Cryopod.TryGetComp<CompRefuelable>();

        private  int ticksLeft = 5000;    // 5k = 2h , research has 4k

        public override bool TryMakePreToilReservations(bool errorOnFailed)     //reserve and check if progressbar should be shown
        {
            if (Cryopod != null && !pawn.Reserve(Cryopod, job, 1, 0, null, errorOnFailed))
            {
                return false;
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(CryopodIndex);

            yield return Toils_Goto.GotoThing(CryopodIndex, PathEndMode.OnCell);

            Toil toil = ToilMaker.MakeToil("MakeNewToils");
            toil.initAction = delegate
            {
                pawn.jobs.posture = PawnPosture.LayingInBedFaceUp;
                
                if (PawnUtility.PlayerForcedJobNowOrSoon(pawn))
                {
                    ticksLeft = 60000;
                    //Log.Message("forced action");
                }

            };
            
            toil.tickAction = delegate  //apply debuffs when done
            {
                ticksLeft--;
                if (ticksLeft <= 0)
                {
                    if (pawn.Drafted)
                    {
                        //continue this job
                    }
                    else
                    {
                        Hediff NeuralInterfaceHediff = pawn.health.hediffSet.GetFirstHediffOfDef(ListOfMechtech_HediffDefs.AV_NeuralInterface);
                        if(NeuralInterfaceHediff != null)
                        {
                            HediffComp_Cryogenicpod hediffcomp = NeuralInterfaceHediff.TryGetComp<HediffComp_Cryogenicpod>();
                            if(hediffcomp.IsBusy == (int)HediffComp_Cryogenicpod.CryogenicpodAction.shield) 
                            {
                                //continue this job
                            }
                            else
                            {
                                GiveDebuffChance(pawn);  
                                ReadyForNextToil();
                            }
                        }
                        else
                        {
                            GiveDebuffChance(pawn);
                            ReadyForNextToil();
                        }
                    }
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Never;

            toil.FailOn(() => !CryopdIsUseable());

            yield return toil;
        }


        public void GiveDebuffChance(Pawn pawn) //nullpointer for when trying to add hediff
        {
            if(!pawn.Dead)
            {
                if (Rand.Chance(NetrunningSettings.CryopodSevereSicknessChance))
                {
                    pawn.health.AddHediff(ListOfVanilla_Defs.SleepingSickness);
                }
                else if (Rand.Chance(NetrunningSettings.CryopodSevereSicknessChance + NetrunningSettings.CryopodMildSicknessChance))
                {
                    pawn.health.AddHediff(HediffDefOf.CryptosleepSickness);
                }
            }
        }

        public bool CryopdIsUseable()
        {
            if(!PowerTrader.PowerOn || !Refuelable.HasFuel)
            {
                return false;
            }
            return true;
        }

    }
}