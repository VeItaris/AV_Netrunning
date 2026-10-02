using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace AV_Netrunning
{

    public class ModExtension_Cryopod : DefModExtension
    {
        public bool isCryopod = false;
    }

    public class CheckModExtensions
    {
        public bool IsCryopod(Def defToCheck)
        {
            if (defToCheck.HasModExtension<ModExtension_Cryopod>())
            {
                return defToCheck.GetModExtension<ModExtension_Cryopod>().isCryopod;
            }
            return false;
        }
    }

}

