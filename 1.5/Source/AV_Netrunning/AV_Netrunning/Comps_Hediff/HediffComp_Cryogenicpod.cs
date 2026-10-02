using RimWorld;
using RimWorld.BaseGen;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Analytics;
using UnityEngine.SocialPlatforms;
using UnityEngine.UIElements;
using Verse;
using Verse.AI;
using Verse.Noise;
using Verse.Sound;


namespace AV_Netrunning
{
    public class HediffComp_Cryogenicpod : HediffComp
    {
        public HediffCompProperties_Cryogenicpod Props => (HediffCompProperties_Cryogenicpod)props;

        public List<Pawn> OverseenMechs => parent.pawn.mechanitor.OverseenPawns;

        public int IsBusy = (int)CryogenicpodAction.idle;
        public enum CryogenicpodAction
        {
            idle,           //0
            connecting,     //1
            disconnecting,  //2   
            repair,         //3 
            maintenance,    //4 
            research,       //5
            shield          //6
        }

        enum CryogenicpodAIMode
        {
            maintenance,    //0
            combat,         //1
            research,       //2   
            repaironly,     //3 
            forcedwork      //4     //not in use right now!!! For 
        }

        public string inspectorString = "Cryopod InspectorString";
        public bool useCustomInspectorString = true;

        public int AiMode = (int)CryogenicpodAIMode.maintenance;

        public LocalTargetInfo curLocalTargetInfo = LocalTargetInfo.Invalid; //invalid so it does not nullpoint...
        public Pawn curTarget;

#pragma warning disable CS0414 // The field 'HediffComp_Cryogenicpod.usedResting' is assigned but its value is never used
        private bool usedResting = false;   //only definined and set, not actually used
#pragma warning restore CS0414 // The field 'HediffComp_Cryogenicpod.usedResting' is assigned but its value is never used

        private int actionTick = 0;

        private int ticksToTakeControl = 0;
        private int ticksToDisconnect = 1;

        
        private readonly int ticksToApplyMaintenanceHediff = 3000;
        private int TicksPerHeal => Mathf.RoundToInt(1f / parent.pawn.GetStatValue(StatDefOf.MechRepairSpeed) * 120f *  actionspeed);
        private int ticksToNextRepair = 0;

        private bool WorkerAI = true;
        private bool AIStarted = false;

        private readonly int Shieldcooldown = 300;    //300 is normal!
        private int TicksForShieldcooldown = 0;

        private SimpleColor Color_Repair = SimpleColor.Blue;
        private SimpleColor Color_Connect = SimpleColor.Red;

        private ResearchProjectDef ResearchProject => Find.ResearchManager.GetProject();

        private float actionspeed = 1f; //research done,


        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!HasCraftSkill())    //no crafting, no effect
            {
                return;
            }

            if (TicksForShieldcooldown > 0)
            {
                if(IsBusy != (int)CryogenicpodAction.shield)
                { 
                    TicksForShieldcooldown--; // resets the cooldown when not shielding
                }
            }

            if (!PawnCanNetrunNow())
            {
                EndAction();
                ResetInspectionString();
                useCustomInspectorString = false;
                return;
            }
            
            if(HasATarget() && !TargetOnSameMap())
            {
                EndAction();
                ResetTarget();
                return;
            }

            if(usedResting)
            {
                actionspeed = 0.1f;
            }
            else
            {
                actionspeed = 1f;
            }



            if(NetrunningSettings.DebugLogging)
            {
                //DrawLine(SimpleColor.Red); //just for debugging
            }
            
            // gizmo actions

            if (actionTick != 0)
            {
                actionTick++;

                if (IsBusy != (int)CryogenicpodAction.idle)
                {
                    if (IsBusy == (int)CryogenicpodAction.connecting)
                    {
                        DrawLine(Color_Connect);
                        ChangeInspectionString("AV_JobString_Cryopod_Connect".Translate());
                        if (actionTick >= ticksToTakeControl)
                        {
                            Connect(curLocalTargetInfo, parent.pawn);
                            EndAction();
                        }
                    }
                    else if (IsBusy == (int)CryogenicpodAction.disconnecting)
                    {
                        DrawLine(Color_Connect);
                        ChangeInspectionString("AV_JobString_Cryopod_Disconnect".Translate());
                        if (actionTick >= 5)
                        {
                            Disconnect(curLocalTargetInfo, parent.pawn);
                            EndAction();
                        }
                    }
                    else if (IsBusy == (int)CryogenicpodAction.repair)
                    {
                        DrawLine(Color_Repair);
                        ChangeInspectionString("AV_JobString_Cryopod_Repair".Translate());
                        if (actionTick != 0)
                        {
                            if (CanRemoteRepair(curLocalTargetInfo) && !curLocalTargetInfo.Pawn.Dead)
                            {
                                if (AIStarted && curLocalTargetInfo.Pawn.needs.energy.CurLevelPercentage <= AIRepairTillEnergy)
                                {
                                    EndAction();
                                }
                                else
                                {
                                    Repair(curLocalTargetInfo, parent.pawn);
                                }
                            }
                            else
                            {
                                EndAction();
                            }
                        }
                    }
                    else if (IsBusy == (int)CryogenicpodAction.maintenance)
                    {
                        MaintenanceTick();
                    }
                    else if (IsBusy == (int)CryogenicpodAction.shield)
                    {
                        ShieldTick();
                    }
                    else if (IsBusy == (int)CryogenicpodAction.research)
                    {
                        ChangeInspectionString("AV_JobString_Cryopod_Research".Translate());
                        Research();
                        if (AiMode != (int)CryogenicpodAIMode.research)
                        {
                            
                            IsBusy = (int)CryogenicpodAction.idle;
                            EndAction();
                        }
                    }
                }
            }
            else if (WorkerAI && IsBusy == (int)CryogenicpodAction.idle)
            {
                ChangeInspectionString("AV_JobString_Cryopod_Idle".Translate());
                AIChooseAction();   //NEEDS TO BE DONE LAST! Otherwise bugs the mechs unreversable ...XD
            }


            if(IsBusy == (int)CryogenicpodAction.idle)
            {
               ChangeInspectionString("AV_JobString_Cryopod_Idle".Translate());
               useCustomInspectorString = false;
            }
        }

        public override void CompPostPostRemoved()
        {
            DestroyMechShield();
            if (NetrunningSettings.DebugLogging)
            {
                Log.Message("AV_Netrunning.HediffComp_Cryogenicpod: shield destroyed post removed");
            }
            
        }

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref WorkerAI, "WorkerAI", true);
            Scribe_Values.Look(ref AiMode, "AiMode", (int)CryogenicpodAIMode.maintenance);
        }

        private void ChangeInspectionString(string inspect_string)
        {
            parent.pawn.CurJob.reportStringOverride = inspect_string + TargetInspectionString();
            //parent.pawn.CurJobDef.reportString = inspect_string;  //this changes the def itself...
            inspectorString = inspect_string + TargetInspectionString();
        }

        private void ResetInspectionString()
        {
            parent.pawn.CurJob.reportStringOverride = parent.pawn.CurJob.def.reportString;
        }

        private String TargetInspectionString()
        {
            if(HasATarget())
            {
                return " " + "AV_GizmoLabel_Cryopod_Targeting".Translate().CapitalizeFirst() + " " + curTarget.Label.ToString().CapitalizeFirst() + ".";
            }
            return "";
        }

        #region Target (general)

        private bool HasATarget()
        {
            if (curTarget != null) 
            {
                return true;
            }
            return false;
        }

        private bool TargetOnSameMap()
        {
            if(HasATarget())
            {
                if (curTarget.Map == parent.pawn.Map)
                {
                    return true;
                }
            }
            return false;         
        }

        public void DrawLine(SimpleColor color)
        {
            if (IsBusy != (int)CryogenicpodAction.idle)
            {
                if (HasATarget())
                {
                    Vector3 vec_target = new Vector3(curTarget.DrawPos.x, AltitudeLayer.BuildingBelowTop.AltitudeFor(), curTarget.DrawPos.z);
                    Vector3 vec_overseer = new Vector3(parent.pawn.DrawPos.x, AltitudeLayer.BuildingBelowTop.AltitudeFor(), parent.pawn.DrawPos.z);
                    GenDraw.DrawLineBetween(vec_target, vec_overseer, color);
                }
            }
        }

        public void UpdateTarget(LocalTargetInfo target)
        {
            curLocalTargetInfo = target;
            curTarget = curLocalTargetInfo.Pawn;
        }

        public void UpdateTarget(Pawn pawn)
        {
            curLocalTargetInfo = LocalTargetInfo.Invalid;   //does this couse problems? I should always ask for curTarget
            curTarget = pawn;
        }
        
        private void ResetTarget()
        {
            curLocalTargetInfo = LocalTargetInfo.Invalid;
            curTarget = null;
        }

        private void Highlight(LocalTargetInfo target)          //simplified CompPlantable targeter
        {
            if (target.IsValid)
            {
                GenDraw.DrawTargetHighlight(target);        //this is the circle over the targeted pawn!
            }
        }

        private void HighlightAOE(LocalTargetInfo target)          //this is a radius like mechanoid power cell explosions
        {
            if (target.IsValid)
            {
                Color color = Color.blue;
                GenDraw.DrawRadiusRing(target.Cell, 3f, color);

                //GenDraw.DrawCircleOutline(target.CenterVector3, 3f,SimpleColor.Blue);
                //GenDraw.DrawTargetingHightlight_Explosion(target.Cell, 3f);

                GenDraw.DrawTargetHighlight(target);        //this is the circle over the targeted pawn!
            }
        }

        private bool IsOverseen(Pawn pawn)
        {
            CompOverseerSubject compOverseerSubject = pawn.TryGetComp<CompOverseerSubject>();

            if (compOverseerSubject != null)
            {
                if (compOverseerSubject.State != OverseerSubjectState.Overseen)
                {
                    return false;
                }
                return true;
            }
            return false;

        }

        #endregion


        #region AI 

        private readonly float AIRepairTillEnergy = 0.10f;  //repairs till 10% energy, starts ai repair at over 15% energy

        private void AIChooseAction()
        {
            if (AiMode == (int)CryogenicpodAIMode.combat)
            {
                if (HasImplantShield() && parent.pawn.Map.dangerWatcher.DangerRating != StoryDanger.None && TicksForShieldcooldown == 0)     
                {
                    Pawn mechToShield = ShieldTargetAI();
                    if (mechToShield != null)
                    {
                        AIStarted = true;
                        StartToShield(mechToShield);
                        return;
                    }
                }
                else if (HasImplantRepair())
                {                  
                    Pawn mechToRepair = RepairTarget();
                    if (mechToRepair != null)
                    {
                        AIStarted = true;
                        StartToRepair(mechToRepair);
                        return;
                    }
                }
            }
            else if (AiMode == (int)CryogenicpodAIMode.maintenance)
            {
                if (HasImplantRepair())
                {
                    Pawn mechToRepair = RepairTarget();
                    if (mechToRepair != null)
                    {
                        AIStarted = true;
                        StartToRepair(mechToRepair);
                        return;
                    }
                }
                AIStarted = true;
                StartToMaintenance();
            }
            else if (AiMode == (int)CryogenicpodAIMode.repaironly)
            {
                if (HasImplantRepair())
                {
                    Pawn mechToRepair = RepairTarget();

                    if (mechToRepair != null)
                    {
                        AIStarted = true;
                        StartToRepair(mechToRepair);
                        return;
                    }
                }
                else
                {
                    AiMode = (int)CryogenicpodAIMode.maintenance;
                }
            }
            else if (AiMode == (int)CryogenicpodAIMode.research)
            {
                if (ResearchProject != null)
                {
                    AIStarted = true;
                    StartResearch();
                }
            }
        }

        private Pawn RepairTarget()
        {
            foreach (Pawn mech in OverseenMechs)
            {
                if (mech.Map == parent.pawn.Map)
                {
                    if(IsOverseen(mech))
                    {
                        if (MechRepairUtility.CanRepair(mech) && mech.needs.energy.CurLevelPercentage >= AIRepairTillEnergy + 0.05f && !mech.Dead) //repair until only 15% energy is left 
                        {
                            if (AIStarted)
                            {
                                CompMechRepairable compMechRepairable = mech.TryGetComp<CompMechRepairable>();
                                if (compMechRepairable == null)
                                {
                                    return null;
                                }
                                if (compMechRepairable.autoRepair)
                                {
                                    Log.Message("4");
                                    return mech;
                                }
                            }

                        }
                    }
                }
            }
            return null;
        }

        private Pawn ShieldTargetAI() //this is AI only!!!!
        {
            foreach (Pawn mech in OverseenMechs)
            {
                if (mech.Map == parent.pawn.Map)
                {
                    if (!mech.RaceProps.IsWorkMech && !mech.Dead && IsOverseen(mech))
                    {
                        if (mech.equipment.HasAnything())
                        {
                            if (!mech.equipment.Primary.def.IsRangedWeapon)
                            {
                                return mech;
                            }
                        }
                        else
                        {
                            return mech;
                        }
                    }          
                }
            }
            if (NetrunningSettings.DebugLogging)
            {
                Log.Message("AV_Netrunning.HediffComp_Cryogenicpod: " + "AV_Message_NoShieldTarget".Translate());
            }
            return null;
        }


        private float GetOverseerProcessingpower()
        {
            float processingPower = 0;

            List<Pawn> MechProcessingPower = parent.pawn.mechanitor.OverseenPawns;

            foreach (Pawn mech in MechProcessingPower)
            {
                if ((!mech.InAggroMentalState || !mech.Dead || !mech.IsGestating()) && PawnOnMap(mech))
                {
                    if (mech.RaceProps.IsWorkMech)
                    {
                        processingPower += mech.GetStatValue(StatDefOf.BandwidthCost);
                    }
                    else
                    {
                        processingPower += mech.GetStatValue(StatDefOf.BandwidthCost) / 2;  //only half the researchpower per bandwidth from combat mechs
                    }

                    Hediff researchhediff = mech.health.hediffSet.GetFirstHediffOfDef(Props.researchhediff);
                    if (researchhediff == null)
                    {
                        mech.health.AddHediff(Props.researchhediff, mech.health.hediffSet.GetBrain());
                    }
                }
            }
            return processingPower;
        }


        private bool PawnOnMap(Pawn mech)
        {
            if (mech.Map == parent.pawn.Map)
            {
                return true;
            }
            return false;
        }


        #endregion


        #region Pawn states and implants

        private void SetCryopodBools()
        {
            if (IsNetrunningWithJob())
            {
                useCustomInspectorString = false;
                usedResting = false;
            }
            else if (IsSleeping())
            {
                useCustomInspectorString = false;
                usedResting = false;
            }
            else if (IsNetrunningWithAssignment())
            {
                useCustomInspectorString = true;
                usedResting = true;
            }
            else
            {
                useCustomInspectorString = false;
                usedResting = false;
            }
        }

        private bool IsNetrunningWithJob()  //real netrunning
        {
            if (parent.pawn.CurJobDef == ListOfMechtech_JobDefs.Netrunning)
            {
                if (((Thing)parent.pawn.CurJob.targetA).Position == parent.pawn.Position)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsNetrunningWithAssignment()   //resting in pod
        {
            if(IsSleeping())
            {
                Log.Error("[AV]Mechtech.HediffComp_Cryogenicpod: Trying to netrun with assignment while sleeping");
            }

            if(parent.pawn.ownership.OwnedBed != null)
            {
                if (parent.pawn.ownership.OwnedBed.def == ListOfMechtech_ThingDefs.AV_Cryogenicpod)
                {
                    if (parent.pawn.ownership.OwnedBed.Position == parent.pawn.Position)
                    {
                        CompPowerTrader compPower = parent.pawn.ownership.OwnedBed.TryGetComp<CompPowerTrader>();
                        CompRefuelable compFuel = parent.pawn.ownership.OwnedBed.TryGetComp<CompRefuelable>();

                        if (compPower != null && compFuel != null)
                        {
                            if (compPower.PowerOn && compFuel.HasFuel)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private bool IsSleeping()   //beeing asleep
        {
            if (parent.pawn.jobs.curDriver.asleep)  
            {
                return true;
            }

            return false;
        }

        public bool PawnCanNetrunNow()
        {
            SetCryopodBools();

            if (!MechanitorUtility.IsMechanitor(parent.pawn))
            {
                Log.Error("AV_Netrunning.HediffComp_Cryogenicpod: Trying to use a cryopod with a non-mechanitor, stopping progress");
                return false;
            }

            if (IsNetrunningWithJob())
            {
                return true;
            }
            if (IsSleeping())
            {
                return false;
            }
            if (IsNetrunningWithAssignment())
            {
                return true;
            }
            return false;
        }

        public bool HasImplantRepair()
        {
            Hediff repairhediff = parent.pawn.health.hediffSet.GetFirstHediffOfDef(ListOfVanilla_Defs.RemoteRepairerImplant);

            if (repairhediff != null)
            {
                return true;
            }
            return false;
        }

        public bool HasImplantShield()
        {
            Hediff shieldhediff = parent.pawn.health.hediffSet.GetFirstHediffOfDef(ListOfVanilla_Defs.RemoteShielderImplant);
            if (shieldhediff != null)
            {
                return true;
            }

            return false;
        }

        public bool HasIntSkill()
        {
            if (parent.pawn.WorkTagIsDisabled(WorkTags.Intellectual))
            {
                return false;
            }
            return true;
        }

        private bool HasCraftSkill()
        {
            if (parent.pawn.WorkTagIsDisabled(WorkTags.Crafting))
            {
                return false;
            }
            return true;
        }

        #endregion


        #region Actions

        private void StartAction()
        {
            actionTick = 1;
            DestroyMechShield();
            CheckStartedForcedAction();
        }

        private void EndAction()
        {
            //Log.Warning("AV_Netrunning.HediffComp_Cryogenicpod: Endaction.");
            actionTick = 0;
            //AIStarted = false;

            DestroyMechShield();
            IsBusy = (int)CryogenicpodAction.idle;
            ResetTarget();
            //reset target in here?
            if (parent.pawn.CurJobDef == ListOfMechtech_JobDefs.Netrunning) //for walking towards the cryopod
            {
                ChangeInspectionString("netrunning.");
            }
        }


        private bool StartedForcedAction = false;

        private void CheckStartedForcedAction()
        {
            if (AIStarted == false)
            {
                StartedForcedAction = true;
            }
            else
            {
                StartedForcedAction = false;
            }
        }

        #endregion


        #region research 

        private void StartResearch()
        {
            StartAction();
            IsBusy = (int)CryogenicpodAction.research;
        }

        private void Research()
        {
            if (ResearchProject != null)    //failsafe needed!
            {
                if (!HasIntSkill())
                {
                    return;
                }
                /*
                if (parent.pawn.WorkTagIsDisabled(WorkTags.Intellectual))
                {
                    return;
                }*/
                float statValue = parent.pawn.GetStatValue(StatDefOf.ResearchSpeed) * actionspeed;
                statValue += GetOverseerProcessingpower() * 0.05f; // 0.05% per used and free bandwidth 
                                                                   //base.TargetThingA.GetStatValue(StatDefOf.ResearchSpeedFactor);    //consider researchbench??
                Find.ResearchManager.ResearchPerformed(statValue, parent.pawn);
                parent.pawn.skills.Learn(SkillDefOf.Intellectual, 0.1f);    //0.1f = normal research exp
            }
        }
        #endregion


        #region Maintenance 
        
        private LocalTargetInfo MaintenanceTargetInfo = LocalTargetInfo.Invalid;
        private void StartToMaintenance()
        {
            StartAction();
            IsBusy = (int)CryogenicpodAction.maintenance;

            ChooseMaintenanceTarget();
        }


        private void MaintenanceTick()
        {
            if(maintenancetarget != null && maintenancetarget.Map == parent.pawn.Map)
            {
                DrawLine(Color_Repair);
                ChangeInspectionString("AV_JobString_Cryopod_Maintain".Translate());
                if (HasCraftSkill())
                {
                    parent.pawn.skills.Learn(SkillDefOf.Crafting, 0.02f);   //0.05f = repairing mechs
                }

                if (actionTick >= ticksToApplyMaintenanceHediff)    //done with maintenance
                {
                    ApplyMaintenanceBuff();
                    EndAction();
                }
            }
            else
            {
                if(NetrunningSettings.DebugLogging)
                {
                    Log.Message("AV_Netrunning.HediffComp_Cryogenicpod: Maintenance target is null, ending action");
                }
                EndAction();
            }
        }

        private Pawn maintenancetarget = null;  //ignore visual studios thingys, this is needed

        public List<Pawn> AllPlayerMechsOnMap = new List<Pawn>();


        public void DEBUG_WriteAllPlayerMechsOnMap()
        {
            for (int i = 0; i < AllPlayerMechsOnMap.Count; i++)
            {
                if (AllPlayerMechsOnMap[i].Label != null)
                {
                    Log.Message("AllPlayerMechsOnMap." + i.ToString() + ": " + AllPlayerMechsOnMap[i]?.Label);
                }
            }
            Log.Message("AllPlayerMechsOnMap writing down is done count should be " + AllPlayerMechsOnMap.Count.ToString());
        }

        public void DEBUG_WriteAllSelectablePawnsOnMap()
        {
            for (int i = 0; i < AllSelectablePawns.Count; i++)
            {
                if (AllSelectablePawns[i].Label != null)
                {
                    Log.Message("AllSelectablePawns." + i.ToString() + ": " + AllSelectablePawns[i]?.Label);
                }
            }
            Log.Message("AllSelectablePawns writing down is done, count should be " + AllSelectablePawns.Count.ToString());
        }

        public List<Pawn> AllSelectablePawns => parent.pawn.Map.mapPawns.AllPawns;

        public void UpdateList_AllPlayerMechsOnMap()
        {
            AllPlayerMechsOnMap.Clear();

            for (int i = 0; i < AllSelectablePawns.Count; i++)
            {
                if (AllSelectablePawns[i]?.Faction == null)
                {
                    // if some mod fucks the faction up this is used!
                    continue;
                }
                if (AllSelectablePawns[i].RaceProps.IsMechanoid && AllSelectablePawns[i].Faction.IsPlayer)
                {
                    AllPlayerMechsOnMap.Add(AllSelectablePawns[i]);
                }
            }
            if(AllPlayerMechsOnMap.Count <= 0)  //maybe this???
            {
                Log.Message("Trying to get a target for cryopod, list is empty");
            }
        }
 
        public void ChooseMaintenanceTarget()
        {
            UpdateList_AllPlayerMechsOnMap();

            maintenancetarget = null;

            if (AllPlayerMechsOnMap.Count <= 0)
            {
                EndAction();
            }
            else
            {
                List<Pawn> overseenMechs = parent.pawn.mechanitor.OverseenPawns;

                //Overseen Mechs
                for (int i = 0; i < overseenMechs.Count; i++)
                {
                    if (overseenMechs[i].RaceProps.IsMechanoid && overseenMechs[i].Faction.IsPlayer && overseenMechs[i].Map == parent.pawn.Map)
                    {
                        if (IsOverseen(overseenMechs[i]))
                        {
                            Hediff hediff = overseenMechs[i].health.hediffSet.GetFirstHediffOfDef(Props.maintenancehediff);

                            if (hediff == null)
                            {
                                UpdateTarget(overseenMechs[i]);
                                maintenancetarget = curTarget;
                                Log.Message("Choose prefered Maintenance Target from overseen mechs");
                                break;
                            }
                        }
                    }
                }
                /*
                if (maintenancetarget == null)
                {
                    //ALL PLAYER MECHS
                    for (int i = 0; i < AllPlayerMechsOnMap.Count; i++)
                    {
                        if (AllPlayerMechsOnMap[i].RaceProps.IsMechanoid && AllPlayerMechsOnMap[i].Faction.IsPlayer)
                        {
                            Hediff hediff = AllPlayerMechsOnMap[i].health.hediffSet.GetFirstHediffOfDef(Props.maintenancehediff);

                            if (hediff == null)
                            {
                                UpdateTarget(AllPlayerMechsOnMap[i]);
                                maintenancetarget = curTarget;
                                //Log.Message("Choose prefered Maintenance Target from all player mechs");
                                break;
                            }
                        }
                    }
                }
                */
                if (maintenancetarget == null)
                {
                    //Random from all player mechs

                    Pawn randomPlayerMech = AllPlayerMechsOnMap.RandomElement();

                    if (randomPlayerMech != null && IsOverseen(randomPlayerMech))
                    {
                        UpdateTarget(randomPlayerMech);

                        maintenancetarget = curTarget;
                    }

                    //maintenancetarget = AllPlayerMechsOnMap.RandomElement();
                    //UpdateTarget(maintenancetarget);
                    //Log.Message("Choose prefered Maintenance Target at random from all player mechs");

                    //AllPlayerMechsOnMap.RandomElement();
                }
            }
        }




        private void ApplyMaintenanceBuff()
        {
            foreach (Pawn mech2 in OverseenMechs)
            {
                Hediff hediff2 = mech2.health.hediffSet.GetFirstHediffOfDef(Props.maintenancehediff);
                if (hediff2 == null)
                {
                    mech2.health.AddHediff(Props.maintenancehediff, mech2.health.hediffSet.GetBrain());
                }
            }

            maintenancetarget.health.AddHediff(Props.maintenancehediff, maintenancetarget.health.hediffSet.GetBrain());

            Hediff hediff = maintenancetarget.health.hediffSet.GetFirstHediffOfDef(Props.maintenancehediff);
            if (hediff == null)
            {
                maintenancetarget.health.AddHediff(Props.maintenancehediff, maintenancetarget.health.hediffSet.GetBrain());
            }
        }
        #endregion


        #region TakeControlRemote 

        public TargetingParameters RemoteConnectTargetingParameters()
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetHumans = false,
                canTargetMechs = true,
                canTargetAnimals = false,
                canTargetLocations = false,
                validator = (TargetInfo x) => CanRemoteConnect((LocalTargetInfo)x)
            };
        }

        public bool CanRemoteConnect(LocalTargetInfo target)
        {
            Pawn pawn = target.Pawn;
            if (pawn == null)
            {
                return false;
            }

            if (pawn.GetOverseer() != parent.pawn && pawn.IsColonyMech && !pawn.Dead) //pawn.Downed && pawn.IsAttacking()) ??
            {
                // has the bandwith to take control
                if (pawn.GetStatValue(StatDefOf.BandwidthCost) <= parent.pawn.mechanitor.TotalBandwidth - parent.pawn.mechanitor.UsedBandwidth)
                {
                    return true;
                }
                else if (NetrunningSettings.DebugLogging)
                {
                    Log.Message("AV_Netrunning.HediffComp_Cryogenicpod: " + "AV_Message_NoBandwidth".Translate());
                }
            }
            return false;
        }

        public void StartToConnect(LocalTargetInfo target)
        {
            UpdateTarget(target);
            ticksToTakeControl = (int)(target.Pawn.GetStatValue(StatDefOf.ControlTakingTime) * 60);

            //int connectTick = Find.TickManager.TicksGame + ticksToTakeControl;
            PawnUtility.ForceWait(target.Pawn, ticksToTakeControl, parent.pawn, maintainPosture: true, maintainSleep: true);
            StartAction();
            IsBusy = (int)CryogenicpodAction.connecting;
        }

        private void Connect(LocalTargetInfo target, Pawn overseer)
        {
            SoundDef sound = SoundDefOf.ControlMech_Complete;  //.PlayOneShotOnCamera(parent.pawn.Map);
            sound.PlayOneShot(new TargetInfo(curTarget.Position, curTarget.Map));
            ResetTarget();
            Pawn mech = target.Pawn;
            if (mech.Faction != parent.pawn.Faction) //failsafe if pawn loses connection from 
            {
                mech.SetFaction(parent.pawn.Faction);
            }
            mech.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
            overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
        }

        #endregion


        #region DisconnectRemote 

        public TargetingParameters RemoteDisconnectTargetingParameters()
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetHumans = false,
                canTargetMechs = true,
                canTargetAnimals = false,
                canTargetLocations = false,
                validator = (TargetInfo x) => CanRemoteDisconnect((LocalTargetInfo)x)
            };
        }

        public bool CanRemoteDisconnect(LocalTargetInfo target)
        {
            Pawn mech = target.Pawn;
            if (mech == null)
            {
                return false;
            }
            if (mech.GetOverseer() == parent.pawn && mech.IsColonyMech && !mech.Dead) //mech.Downed && mech.IsAttacking()) ??
            {
                return true;
            }
            return false;
        }

        public void StartToDisconnect(LocalTargetInfo target)
        {
            UpdateTarget(target);
            ticksToDisconnect = (int)(target.Pawn.GetStatValue(StatDefOf.ControlTakingTime) * 60);

            PawnUtility.ForceWait(target.Pawn, ticksToDisconnect, parent.pawn, maintainPosture: true, maintainSleep: true);
            StartAction();
            IsBusy = (int)CryogenicpodAction.disconnecting;
        }

        private void Disconnect(LocalTargetInfo target, Pawn overseer)
        {
            ResetTarget();
            Pawn mech = target.Pawn;
            if (target.Pawn.Faction != parent.pawn.Faction)     //failsafe if pawn looses connection by canconnecttohive
            {
                mech.SetFaction(parent.pawn.Faction);   //maybe cool message instead?? 
            }
            if (target.Pawn.drafter.Drafted)     //failsafe if pawn looses connection by canconnecttohive
            {
                target.Pawn.drafter.Drafted = !target.Pawn.drafter.Drafted;
            }
            if (mech.GetOverseer() == parent.pawn)
            {
                overseer.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
                mech.GetOverseer()?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
            }



        }

        #endregion


        #region RepairRemote 

        public TargetingParameters RemoteRepairTargetingParameters()
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetHumans = false,
                canTargetMechs = true,
                canTargetAnimals = false,
                canTargetLocations = false,
                validator = (TargetInfo x) => CanRemoteRepair((LocalTargetInfo)x)
            };
        }

        public bool CanRemoteRepair(LocalTargetInfo target)
        {
            Pawn mech = target.Pawn;
            if (mech == null)
            {
                return false;
            }
            if (mech.IsColonyMech && !mech.Dead && MechRepairUtility.CanRepair(mech))
            {
                return true;
            }
            return false;
        }

        public void StartToRepair(LocalTargetInfo target)
        {
            UpdateTarget(target);
            //ForceWaitRemoteRepair(curLocalTargetInfo);
            StartAction();
            IsBusy = (int)CryogenicpodAction.repair;
        }

        private void ForceWaitRemoteRepair(LocalTargetInfo target)
        {
            //deactivated, so mechs can still do combat
            //PawnUtility.ForceWait(target.Pawn, TicksPerHeal - 1, Cryohediff.pawn, maintainPosture: true, maintainSleep: true);  // -1 only in here! for ticks so rounding does not fuck up things
        }

        private void Repair(LocalTargetInfo target, Pawn overseer)
        {
            Pawn mech = target.Pawn;

            ticksToNextRepair--;
            if (ticksToNextRepair <= 0)
            {
                mech.needs.energy.CurLevel -= mech.GetStatValue(StatDefOf.MechEnergyLossPerHP);
                MechRepairUtility.RepairTick(mech);                                                 //too much for some reaon
                ticksToNextRepair = TicksPerHeal;

                if (CanRemoteRepair(curLocalTargetInfo))
                {
                    ForceWaitRemoteRepair(curLocalTargetInfo);
                }
            }
            //pawn.rotationTracker.FaceTarget(mech);
            if (overseer.skills != null)    //overseer always has crafting
            {
                overseer.skills.Learn(SkillDefOf.Crafting, 0.05f);
            }

            //PlaySustainerOrSound(SoundDefOf.RepairMech_Remote);

        }

        #endregion


        #region ShieldRemote 


        private MechShield mechShield;

        private CompProjectileInterceptor projectileInterceptor;

        private CompProjectileInterceptor ProjectileInterceptor
        {
            get
            {
                if (projectileInterceptor == null && mechShield != null)
                {
                    projectileInterceptor = mechShield.GetComp<CompProjectileInterceptor>();
                }
                return projectileInterceptor;
            }
        }

        public TargetingParameters RemoteShieldTargetingParameters()
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetHumans = false,
                canTargetMechs = true,
                canTargetAnimals = false,
                canTargetLocations = false,
                validator = (TargetInfo x) => CanRemoteShield((LocalTargetInfo)x)
            };
        }

        public bool CanRemoteShield(LocalTargetInfo target)     //ai choosing should not be in here
        {
            Pawn mech = target.Pawn;
            if (mech == null)
            {
                return false;
            }
            if (mech.IsColonyMech && !mech.Dead)
            {
                return true;
            }
            return false;     
        }

        public void StartToShield(LocalTargetInfo target)
        {
            UpdateTarget(target);
            //ForceWaitRemoteRepair(curLocalTargetInfo);
            StartAction();            
            IsBusy = (int)CryogenicpodAction.shield;
        }
 
        private void ShieldTick()      //this is NOT a tick action, instead a one time thing
        {
            ChangeInspectionString("AV_JobString_Cryopod_Shield".Translate());

            if (CanRemoteShield(curLocalTargetInfo) && !curLocalTargetInfo.Pawn.Dead)
            {
                if (projectileInterceptor != null)
                {
                    if (AIStarted && parent.pawn.Map.dangerWatcher.DangerRating == StoryDanger.None)    //ends for ai if there is no more need to shield
                    {
                        EndAction();
                    }
                    else if (projectileInterceptor.currentHitPoints <= 0.9)      //ends if mechshield is down, needs to be more than 0 !!!
                    {
                        EndAction();
                    }
                }
                else if (mechShield == null)
                {
                    Shield(curLocalTargetInfo, parent.pawn);
                }
                DrawLine(Color_Repair);
            }
        }

        private void Shield(LocalTargetInfo target, Pawn overseer)      //this is NOT a tick action, instead a one time thing
        {
            Pawn mech = target.Pawn;

            mechShield = (MechShield)GenSpawn.Spawn(ThingDefOf.MechShield, mech.Position, mech.Map);
            mechShield.SetTarget(mech);
            int num = (int)overseer.GetStatValue(StatDefOf.MechRemoteShieldEnergy);
            ProjectileInterceptor.maxHitPointsOverride = num;
            ProjectileInterceptor.currentHitPoints = num;

            SoundDef sound = ListOfVanilla_Defs.ShieldMech_Start;
            sound.PlayOneShot(new TargetInfo(curTarget.Position, curTarget.Map));

            TicksForShieldcooldown = Shieldcooldown;
        }

        private void DestroyMechShield()
        {
            if (mechShield != null)
            {
                if (!mechShield.Destroyed)
                {
                    mechShield.Destroy();
                }
                mechShield = null;
                projectileInterceptor = null;   //removes nullpointer??
                if(HasATarget())
                {
                    SoundDef sound = ListOfVanilla_Defs.ShieldMech_Complete;
                    sound.PlayOneShot(new TargetInfo(curTarget.Position, curTarget.Map));
                }
            }
            if(IsBusy == (int)CryogenicpodAction.shield)
            {
                TicksForShieldcooldown = Shieldcooldown;
            }
        }


        #endregion


        #region Gizmostuff 

        private string GetAIModeGizmoLabel_Combat()
        {
            if (HasImplantRepair() || HasImplantShield())
            {
                return "AV_GizmoDesc_Cryopod_AIMode_Combat".Translate();
            }
            return "AV_GizmoDesc_Cryopod_AIMode_Combat".Translate() + " (" + "AV_GizmoDesc_Cryopod_noImplant".Translate() + ")";
        }

        private string GetAIModeGizmoLabel_Repair()
        {
            if (HasImplantRepair())
            {
                return "AV_GizmoDesc_Cryopod_AIMode_Repair".Translate();
            }
            return "AV_GizmoDesc_Cryopod_AIMode_Repair".Translate() + " (" + "AV_GizmoDesc_Cryopod_noImplant".Translate() + ")";
        }

        private string GetAIModeGizmoLabel_Research()
        {

            foreach (SkillRecord skill in parent.pawn.skills.skills)
            {
                if (skill.def == SkillDefOf.Intellectual && skill.PermanentlyDisabled)
                {
                    return "AV_GizmoDesc_Cryopod_AIMode_Research".Translate() + " (" + "AV_GizmoDesc_Cryopod_noInt".Translate() + ")";
                }
            }
            return "AV_GizmoDesc_Cryopod_AIMode_Research".Translate();
        }


        public void AIMode()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>()
            {
                new FloatMenuOption("AV_GizmoDesc_Cryopod_AIMode_Maintenance".Translate().CapitalizeFirst(), delegate
                {
                    AiMode = (int)CryogenicpodAIMode.maintenance;
                }, MenuOptionPriority.High),

                new FloatMenuOption(GetAIModeGizmoLabel_Combat(), delegate
                {
                    if (HasImplantRepair() || HasImplantShield())
                    {
                        AiMode = (int)CryogenicpodAIMode.combat;
                    }
                    else
                    {
                        Messages.Message("AV_GizmoMessage_Cryopod_AIMode_NoRepair".Translate().CapitalizeFirst(), MessageTypeDefOf.NeutralEvent);
                    }
                    
                }, MenuOptionPriority.High),

                new FloatMenuOption(GetAIModeGizmoLabel_Repair(), delegate
                {
                    if (HasImplantRepair())
                    {
                        AiMode = (int)CryogenicpodAIMode.repaironly;
                    }
                    else
                    {
                       Messages.Message("AV_GizmoMessage_Cryopod_AIMode_NoRepair".Translate().CapitalizeFirst(), MessageTypeDefOf.NeutralEvent);
                    }
                }, MenuOptionPriority.High),

                new FloatMenuOption(GetAIModeGizmoLabel_Research(), delegate
                {
                    if(HasIntSkill())
                    {
                        if(ResearchProject == null)
                        {
                            Messages.Message("AV_GizmoMessage_Cryopod_AIMode_NoResearchProj".Translate().CapitalizeFirst(), MessageTypeDefOf.NeutralEvent);
                        }
                        AiMode = (int)CryogenicpodAIMode.research;
                    }
                    else
                    {
                        Messages.Message("AV_GizmoMessage_Cryopod_AIMode_NoIntellect".Translate().CapitalizeFirst(), MessageTypeDefOf.NeutralEvent);
                    }

                }, MenuOptionPriority.High),
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public string CurrentActionString()
        {
            if (IsBusy == (int)CryogenicpodAction.idle)
            {
                return "AV_GizmoDesc_Cryopod_Action_Idle".Translate();
            }
            if (IsBusy == (int)CryogenicpodAction.maintenance)
            {
                return "AV_GizmoDesc_Cryopod_Action_Maintenance".Translate();
            }
            if (IsBusy == (int)CryogenicpodAction.repair)
            {
                return "AV_GizmoDesc_Cryopod_Action_Repair".Translate();
            }
            if (IsBusy == (int)CryogenicpodAction.disconnecting)
            {
                return "AV_GizmoDesc_Cryopod_Action_Disconnect".Translate();
            }
            if (IsBusy == (int)CryogenicpodAction.connecting)
            {
                return "AV_GizmoDesc_Cryopod_Action_Connect".Translate(); 
            }
            if (IsBusy == (int)CryogenicpodAction.research)
            {
                return "AV_GizmoDesc_Cryopod_Action_Research".Translate();
            }
            if (IsBusy == (int)CryogenicpodAction.shield)
            {
                return "AV_GizmoDesc_Cryopod_Action_Shield".Translate();
            }
            Log.Warning("AV_Netrunning.HediffComp_Cryogenicpod: Could not get current action string");
            return "ERROR";
        }

        public string CurrentAIModeString()
        {
            if (AiMode == (int)CryogenicpodAIMode.combat)
            {
                return "AV_GizmoDesc_Cryopod_AIMode_Combat".Translate();
            }
            if (AiMode == (int)CryogenicpodAIMode.repaironly)
            {
                return "AV_GizmoDesc_Cryopod_AIMode_Repair".Translate();
            }
            if (AiMode == (int)CryogenicpodAIMode.maintenance)
            {
                return "AV_GizmoDesc_Cryopod_AIMode_Maintenance".Translate();
            }
            if (AiMode == (int)CryogenicpodAIMode.research)
            {
                return "AV_GizmoDesc_Cryopod_AIMode_Research".Translate();
            }
            Log.Warning("AV_Netrunning.HediffComp_Cryogenicpod: Could not get current ai-mode string");
            return "ERROR";
        }

        private Texture2D GetShieldTexture()
        {
            if (TicksForShieldcooldown == 0)
            {
                return ContentFinder<Texture2D>.Get("UI/Gizmos/AV_ShieldMech");
            }
            else
            {
                return ContentFinder<Texture2D>.Get("UI/Gizmos/AV_ShieldMech_cooldown");
            }
        }

        private Texture2D GetAIMODETexture()
        {
            if (AiMode == (int)CryogenicpodAIMode.combat)
            {
                return ContentFinder<Texture2D>.Get("UI/Gizmos/AV_CryoMode_Shielding");
            }
            if (AiMode == (int)CryogenicpodAIMode.repaironly)
            {
                return ContentFinder<Texture2D>.Get("UI/Gizmos/AV_CryoMode_Repair");
            }
            if (AiMode == (int)CryogenicpodAIMode.maintenance)
            {
                return ContentFinder<Texture2D>.Get("UI/Gizmos/AV_CryoMode_Maintain");
            }
            if (AiMode == (int)CryogenicpodAIMode.research)
            {
                return ContentFinder<Texture2D>.Get("UI/Gizmos/AV_CryoMode_Research");
            }
            Log.Warning("AV_Netrunning.HediffComp_Cryogenicpod: Could not get current ai-mode texture, using default");
            return ContentFinder<Texture2D>.Get("UI/Gizmos/AV_Cryo_AI");
        }

        private string GetShieldGizmoLabel()
        {
            if (TicksForShieldcooldown != 0)
            {
                int time = TicksForShieldcooldown / 60;
                return "AV_GizmoLabel_Cryopod_Shield_Cooldown".Translate() + ": " + time.ToString() + "s";
            }
            return "AV_GizmoLabel_Cryopod_Shield".Translate();
        }

        #endregion

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            if (parent.pawn.Faction.def == FactionDefOf.PlayerColony || NetrunningSettings.DebugLogging)
            {
                if (PawnCanNetrunNow())
                {
                    #region ai mode

                    if (NetrunningSettings.DebugLogging)   //change of ai usage only in my dev settings!
                    {
                        Command_Toggle useAI_Toggle = new Command_Toggle();
                        useAI_Toggle.defaultLabel = CurrentActionString();
                        useAI_Toggle.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/AV_Cryo_AI");
                        useAI_Toggle.defaultDesc = "AV_GizmoDesc_Cryopod_AIToggle".Translate().CapitalizeFirst();
                        useAI_Toggle.isActive = () => WorkerAI;
                        useAI_Toggle.toggleAction = delegate
                        {
                            WorkerAI = !WorkerAI;
                        };
                        yield return useAI_Toggle;
                    }


                    Command_Action AIMode_Action = new Command_Action();
                    AIMode_Action.defaultLabel = "AV_GizmoLabel_Cryopod_AIMode".Translate().CapitalizeFirst() + ": " + CurrentAIModeString();
                    AIMode_Action.icon = GetAIMODETexture();
                    AIMode_Action.defaultDesc = "AV_GizmoDesc_Cryopod_AIMode".Translate().CapitalizeFirst();
                    AIMode_Action.action = delegate
                    {
                        AIMode();
                    };
                    yield return AIMode_Action;

                    #endregion

                    #region Dis-/connect

                    Command_Action remoteControll_Action = new Command_Action();
                    remoteControll_Action.defaultLabel = "AV_GizmoLabel_Cryopod_Connect".Translate();
                    remoteControll_Action.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/AV_remote_connect");
                    remoteControll_Action.defaultDesc = "AV_GizmoDesc_Cryopod_Connect".Translate().CapitalizeFirst();  
                    remoteControll_Action.action = delegate
                    {
                        Find.Targeter.BeginTargeting(RemoteConnectTargetingParameters(), StartToConnect, Highlight, CanRemoteConnect);
                        AIStarted = false;
                    };
                    yield return remoteControll_Action;


                    Command_Action disconnect_Action = new Command_Action();
                    disconnect_Action.defaultLabel = "AV_GizmoLabel_Cryopod_Disconnect".Translate();
                    disconnect_Action.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/AV_remote_disconnect");
                    disconnect_Action.defaultDesc = "AV_GizmoDesc_Cryopod_Disconnect".Translate().CapitalizeFirst();
                    disconnect_Action.action = delegate
                    {
                        Find.Targeter.BeginTargeting(RemoteDisconnectTargetingParameters(), StartToDisconnect, Highlight, CanRemoteDisconnect);
                        AIStarted = false;
                    };
                    yield return disconnect_Action;

                    #endregion

                    #region implant based skills

                    if (HasImplantRepair())
                    {
                        Command_Action repair_Action = new Command_Action();
                        repair_Action.defaultLabel = "AV_GizmoLabel_Cryopod_Repair".Translate();
                        repair_Action.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/AV_RemoteRepair");
                        repair_Action.defaultDesc = "AV_GizmoDesc_Cryopod_Repair".Translate().CapitalizeFirst();            
                        repair_Action.action = delegate
                        {
                            Find.Targeter.BeginTargeting(RemoteRepairTargetingParameters(), StartToRepair, Highlight, CanRemoteRepair);
                            AIStarted = false;
                        };
                        yield return repair_Action;
                    }


                    if (HasImplantShield())
                    {
                        Command_Action shield_Action = new Command_Action();
                        shield_Action.defaultLabel = GetShieldGizmoLabel();  
                        shield_Action.icon = GetShieldTexture();
                        shield_Action.defaultDesc = "AV_GizmoDesc_Cryopod_Shield".Translate().CapitalizeFirst();
                        shield_Action.action = delegate
                        {
                            if (TicksForShieldcooldown == 0)
                            {
                                Find.Targeter.BeginTargeting(RemoteShieldTargetingParameters(), StartToShield, Highlight, CanRemoteShield);
                                AIStarted = false;
                            }
                            else
                            {
                                string message = "AV_Gizmo_Cryopod_Shieldcooldown".Translate().CapitalizeFirst();
                                Messages.Message(message, parent.pawn, MessageTypeDefOf.CautionInput);
                            }
                        };
                        yield return shield_Action;
                    }

                    #endregion

                    if (StartedForcedAction)
                    {
                        Command_Action stop_Action = new Command_Action();
                        stop_Action.defaultLabel = "AV_GizmoLabel_Cryopod_Cancel".Translate(); 
                        stop_Action.icon = ContentFinder<Texture2D>.Get("UI/Gizmos/AV_Cryo_Cancel"); 
                        stop_Action.defaultDesc = "AV_GizmoDesc_Cryopod_Cancel".Translate().CapitalizeFirst();
                        stop_Action.action = delegate
                        {
                            StartedForcedAction = false;    //removes this gizmo if clicked
                            EndAction();
                        };
                        yield return stop_Action;
                    }
                }
            }
        }
    }
}
