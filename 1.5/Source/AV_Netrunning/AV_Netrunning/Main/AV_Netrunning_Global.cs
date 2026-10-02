using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Noise;
using static HarmonyLib.Code;
using static AV_Framework.SettingsHelper;
//using static AV_Framework.FrameworkSettings;
using AV_Framework;

namespace AV_Netrunning
{
    public class NetrunningSettings : ModSettings
    {
        //default values for settings

        public static float DefaultCryopodSevereSicknessChance  = 0.01f;
        public static float DefaultCryopodSevereMildChance      = 0.1f;

        public static bool DefaultDebugLogging                  = false;
        public static bool DefaultDebugOnLoad                   = false;

        //apply default values for settings on first start with this mod

        public static float CryopodSevereSicknessChance = DefaultCryopodSevereSicknessChance;
        public static float CryopodMildSicknessChance   = DefaultCryopodSevereMildChance;

        public static bool DebugLogging                 = DefaultDebugLogging;
        public static bool DebugOnLoad                  = DefaultDebugOnLoad;

        // The part that writes our settings to file. Note that saving is by ref.
        public override void ExposeData()
        {

            Scribe_Values.Look(ref CryopodSevereSicknessChance, "CryopodSevereSicknessChance");
            Scribe_Values.Look(ref CryopodMildSicknessChance, "CryopodMildSicknessChance");

            Scribe_Values.Look(ref DebugLogging, "DebugLogging");
            Scribe_Values.Look(ref DebugOnLoad, "DefaultDebugOnLoad");

            base.ExposeData();
        }
    }

    public class AV_Netrunning : Mod
    {
        readonly NetrunningSettings settings;

        // A mandatory constructor which resolves the reference to our settings.
        public AV_Netrunning(ModContentPack content) : base(content)
        {
            this.settings = GetSettings<NetrunningSettings>();
        }

        private int CurrentPage = (int)AV_SettingsFolder.Left;

        private Texture2D Steve => ContentFinder<Texture2D>.Get("UI/Widgets/AV_centipede");
        private Texture2D Steve_bugs => ContentFinder<Texture2D>.Get("UI/Widgets/AV_bouble_bugs");
        private Texture2D Steve_nodev => ContentFinder<Texture2D>.Get("UI/Widgets/AV_bouble_nodev");

        private string label = "";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            int Folderchanged = DrawFolderStructure(inRect, CurrentPage, "Content-Settings", "DEV-Settings");

            if (Folderchanged != 0)
            {
                CurrentPage = Folderchanged;
            }
            CorrectCurrentFolder(CurrentPage, 2);

            Listing_Standard listingStandard = new Listing_Standard();

            listingStandard.Begin(inRect);

            Foldergap(listingStandard);

            if (CurrentPage == (int)AV_SettingsFolder.Center)
            {
                DrawDEVSettings(listingStandard, inRect);
            }
            else if (CurrentPage == (int)AV_SettingsFolder.Left)
            {
                DrawContentSettings(listingStandard, inRect);
            }

            listingStandard.End();


            Rect nameRect = DrawLogo(Steve, inRect);
            nameRect.y -= 5f;
            if (CurrentPage == (int)AV_SettingsFolder.Center)
            {
                if (Prefs.DevMode)
                {
                    DrawCenteredLabelAdjusted(nameRect, "Steve", x_offset: 2f);
                    DrawLogo(Steve_bugs, inRect, 160, 150);
                }
                else
                {
                    DrawCenteredLabelAdjusted(nameRect, "Steve", true, "_____", x_offset: 2f, x_offset_drafted: 1f);
                    DrawLogo(Steve_nodev, inRect, 140, 150);
                }
            }
            else
            {
                DrawCenteredLabelAdjusted(nameRect, "Steve", x_offset: 2f);
            }


            base.DoSettingsWindowContents(inRect);
        }


        public void DrawContentSettings(Listing_Standard listingStandard, Rect inRect)
        {
            Rect labelRect = inRect;
            labelRect.y = listingStandard.CurHeight;

            listingStandard.Gap();
            labelRect.y = listingStandard.CurHeight;
            DrawCenteredLabel(listingStandard, labelRect, "AV_SettingsLabel_difficulty".Translate().CapitalizeFirst());
            listingStandard.GapLine();

            label = "AV_SettingsSliderLabel_CryopodMildSickness".Translate().CapitalizeFirst() + ": " + (NetrunningSettings.CryopodMildSicknessChance * 100).ToString() + "%";
            float cryopodMildSicknessChance = listingStandard.SliderLabeled(label, NetrunningSettings.CryopodMildSicknessChance, 0f, 0.5f, 0.5f, "AV_SettingsSliderDesc_MechDrop_CryopodMildSickness".Translate().CapitalizeFirst());
            NetrunningSettings.CryopodMildSicknessChance = (float)Math.Round(cryopodMildSicknessChance, 3);

            label = "AV_SettingsSliderLabel_CryopodSevereSickness".Translate().CapitalizeFirst() + ": " + (NetrunningSettings.CryopodSevereSicknessChance * 100).ToString() + "%";
            float cryopodSevereSicknessChance = listingStandard.SliderLabeled(label, NetrunningSettings.CryopodSevereSicknessChance, 0f, 0.1f, 0.5f, "AV_SettingsSliderDesc_MechDrop_CryopodSevereSickness".Translate().CapitalizeFirst());
            NetrunningSettings.CryopodSevereSicknessChance = (float)Math.Round(cryopodSevereSicknessChance, 3);

        }

        public void DrawDEVSettings(Listing_Standard listingStandard, Rect inRect)
        {
            listingStandard.CheckboxLabeled("Devmode: Show devtools and debug logging", ref NetrunningSettings.DebugLogging, "Activate devtools and debug logging");
            listingStandard.CheckboxLabeled("Devmode: Debug on safegame loading", ref NetrunningSettings.DebugOnLoad, "Lets time based thingys trigger directly when they spawn");
        }

        public void ListingResetButton(Rect reset_rect)
        {
            Listing_Standard listingStandard = new Listing_Standard();

            listingStandard.Begin(reset_rect);

            if (listingStandard.ButtonText("AV_SettingsButtonLabel_reset".Translate().CapitalizeFirst()))
            {
                ResetSettings();
            }
            listingStandard.End();
        }


        public void ResetSettings()
        {

            NetrunningSettings.CryopodSevereSicknessChance = NetrunningSettings.DefaultCryopodSevereSicknessChance;
            NetrunningSettings.CryopodMildSicknessChance = NetrunningSettings.DefaultCryopodSevereMildChance;

            NetrunningSettings.DebugLogging = NetrunningSettings.DefaultDebugLogging;
            NetrunningSettings.DebugOnLoad = NetrunningSettings.DefaultDebugOnLoad;
        }


        // Override SettingsCategory to show up in the list of settings.
        public override string SettingsCategory()
        {
            return "[AV] Netrunning";
        }
    }

}
