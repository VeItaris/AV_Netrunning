using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Schema;
using UnityEngine;
using Verse;

namespace AV_Netrunning
{
    public class HediffCompProperties_Cryogenicpod : HediffCompProperties
    {
        public HediffDef hediff;
        public HediffDef researchhediff;
        public HediffDef maintenancehediff;
        public ThingDef mote;

        public HediffCompProperties_Cryogenicpod()
        {
            compClass = typeof(HediffComp_Cryogenicpod);
        }
    }
}
