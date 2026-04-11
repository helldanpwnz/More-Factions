using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;
using HarmonyLib;

namespace MoreFactions
{
    // ============================================ БЛОК 1: НАСТРОЙКИ ============================================
    public enum HybridInheritanceMode
    {
        OnlyInheritable,
        SeparateCategories,
        AllInheritable
    }

    public class MoreFactionsSettings : ModSettings
    {
        public bool showDebugLogs = false;
        public bool enableUIPatch = true;
        public bool onlyExistingXenotypes = false;
        public bool generateHybrids = true;
        public HybridInheritanceMode hybridInheritanceMode = HybridInheritanceMode.OnlyInheritable;
        public float hybridSpawnChance = 30f;
        public float migrationChanceRandom = 30f;
        public float migrationChanceIndependence = 30f;
        public bool requireCommsForNews = false;
        public int triggerIntervalHours = 23;
        public float randomSettlementChance = 5f; 
        public float spawnOnRuinsChance = 10f; // Chance to spawn on ruins instead of random tile
        public float hybridGroupsChance = 50f; // Chance for hybrid squads
        public int hybridGroupsMin = 1; // Minimum archetypes to mix
        public int hybridGroupsMax = 3; // Maximum archetypes to mix
        public float hybridIndependenceChance = 15f; // Chance for hybrid squads in independence split
        public int hybridIndependenceCount = 2; // Archetypes to mix in independence split
        public int maxFactionsCount = 20; // Maximum number of factions from this mod in the world

        // Генетическая Эволюция
        public bool allowArchiteGenes = false;
        public int minMetabolism = -10;
        public int maxNewGenes = 2;
        
        // Очистка (Culling)
        public bool enableFactionCulling = false;
        public int factionCullDays = 30;
        
        // Веса появления по техуровням (0-100)
        public float weightNeolithic = 35f;
        public float weightMedieval = 30f;
        public float weightIndustrial = 20f;
        public float weightSpacer = 10f;
        public float weightUltra = 5f;

        // Шансы отношений с игроком при спавне (0 или -80)
        public float playerRelationNeutralChance = 45f;
        public float playerRelationHostileChance = 45f;
        public float playerRelationPermHostileChance = 10f;
        
        public float randomSpawnIdeoInheritChance = 70f;
        
        // Список включенных/выключенных архетипов для рандома (defName -> bool)
        public Dictionary<string, bool> enabledArchetypes = new Dictionary<string, bool>();
        public Dictionary<string, bool> enabledXenotypes = new Dictionary<string, bool>();
        
        public override void ExposeData()
        {
            Scribe_Values.Look(ref randomSettlementChance, "randomSettlementChance", 5f);
            Scribe_Values.Look(ref spawnOnRuinsChance, "spawnOnRuinsChance", 10f);
            Scribe_Values.Look(ref hybridGroupsChance, "hybridGroupsChance", 50f);
            Scribe_Values.Look(ref hybridGroupsMin, "hybridGroupsMin", 1);
            Scribe_Values.Look(ref hybridGroupsMax, "hybridGroupsMax", 3);
            Scribe_Values.Look(ref hybridIndependenceChance, "hybridIndependenceChance", 15f);
            Scribe_Values.Look(ref hybridIndependenceCount, "hybridIndependenceCount", 2);
            Scribe_Values.Look(ref triggerIntervalHours, "triggerIntervalHours", 23);
            Scribe_Values.Look(ref requireCommsForNews, "requireCommsForNews", false);
            Scribe_Values.Look(ref showDebugLogs, "showDebugLogs", false);
            Scribe_Values.Look(ref enableUIPatch, "enableUIPatch", true);
            Scribe_Values.Look(ref onlyExistingXenotypes, "onlyExistingXenotypes", false);
            Scribe_Values.Look(ref generateHybrids, "generateHybrids", true);
            Scribe_Values.Look(ref hybridInheritanceMode, "hybridInheritanceMode", HybridInheritanceMode.OnlyInheritable);
            Scribe_Values.Look(ref hybridSpawnChance, "hybridSpawnChance", 30f);
            Scribe_Values.Look(ref migrationChanceRandom, "migrationChanceRandom", 30f);
            Scribe_Values.Look(ref migrationChanceIndependence, "migrationChanceIndependence", 30f);
            Scribe_Values.Look(ref maxFactionsCount, "maxFactionsCount", 20);
            Scribe_Values.Look(ref enableFactionCulling, "enableFactionCulling", false);
            Scribe_Values.Look(ref factionCullDays, "factionCullDays", 30);

            Scribe_Values.Look(ref weightNeolithic, "weightNeolithic", 35f);
            Scribe_Values.Look(ref weightMedieval, "weightMedieval", 30f);
            Scribe_Values.Look(ref weightIndustrial, "weightIndustrial", 20f);
            Scribe_Values.Look(ref weightSpacer, "weightSpacer", 10f);
            Scribe_Values.Look(ref weightUltra, "weightUltra", 5f);
            
            Scribe_Values.Look(ref independenceSpawnChance, "independenceSpawnChance", 5f);
            Scribe_Values.Look(ref minSettlementsForIndependence, "minSettlementsForIndependence", 5);
            Scribe_Values.Look(ref chanceRelationsHostile, "chanceRelationsHostile", 33.3f);
            Scribe_Values.Look(ref chanceRelationsNeutral, "chanceRelationsNeutral", 33.3f);
            Scribe_Values.Look(ref chanceRelationsAlly, "chanceRelationsAlly", 33.4f);
            Scribe_Values.Look(ref playerRelationNeutralChance, "playerRelationNeutralChance", 45f);
            Scribe_Values.Look(ref playerRelationHostileChance, "playerRelationHostileChance", 45f);
            Scribe_Values.Look(ref playerRelationPermHostileChance, "playerRelationPermHostileChance", 10f);
            Scribe_Values.Look(ref randomSpawnIdeoInheritChance, "randomSpawnIdeoInheritChance", 70f);
            Scribe_Values.Look(ref chanceTechAdvance, "chanceTechAdvance", 5f);
            Scribe_Values.Look(ref chanceTechRegress, "chanceTechRegress", 5f);
            Scribe_Values.Look(ref maxTechLevelForIndependence, "maxTechLevelForIndependence", TechLevel.Ultra);
            
            Scribe_Values.Look(ref allowArchiteGenes, "allowArchiteGenes", false);
            Scribe_Values.Look(ref minMetabolism, "minMetabolism", -10);
            Scribe_Values.Look(ref maxNewGenes, "maxNewGenes", 2);

            Scribe_Collections.Look(ref enabledArchetypes, "enabledArchetypes", LookMode.Value, LookMode.Value);
            if (enabledArchetypes == null) enabledArchetypes = new Dictionary<string, bool>();
            
            Scribe_Collections.Look(ref enabledXenotypes, "enabledXenotypes", LookMode.Value, LookMode.Value);
            if (enabledXenotypes == null) enabledXenotypes = new Dictionary<string, bool>();
        }

        public float independenceSpawnChance = 5f;
        public int minSettlementsForIndependence = 5;
        public float chanceRelationsHostile = 33.3f;
        public float chanceRelationsNeutral = 33.3f;
        public float chanceRelationsAlly = 33.4f;
        public float chanceTechAdvance = 5f;
        public float chanceTechRegress = 5f;
        public TechLevel maxTechLevelForIndependence = TechLevel.Ultra;
    }

    // ============================================ БЛОК 2: МОД И НАСТРОЙКИ UI ============================================
    public class MoreFactionsMod : Mod
    {
        public static MoreFactionsSettings settings;
        private static Vector2 scrollPosition = Vector2.zero;
        private static bool showArchetypeSettings = false;
        private static bool showXenotypeSettings = false;
        private static Dictionary<TechLevel, bool> showXenoTech = new Dictionary<TechLevel, bool>();
        
        public MoreFactionsMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<MoreFactionsSettings>();
            
            // Initializing Harmony for UI patches
            var harmony = new Harmony("com.morefactions.rimworld");
            harmony.PatchAll();
        }
        
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect outerRect = inRect.ContractedBy(10f);
            Rect scrollViewRect = new Rect(0f, 0f, outerRect.width - 25f, 3500f); 
            
            Widgets.BeginScrollView(outerRect, ref scrollPosition, scrollViewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(scrollViewRect);
            
            // ================== ГРУППА 1: ОБЩИЕ НАСТРОЙКИ ==================
            DrawHeader(listing, "MF_Settings_Group_General".Translate());
            
            listing.CheckboxLabeled("MF_RequireCommsForNews".Translate(), ref settings.requireCommsForNews, "MF_RequireCommsForNews_Tip".Translate());
            listing.CheckboxLabeled("MF_Setting_ShowDebugLogs".Translate(), ref settings.showDebugLogs, "MF_Setting_ShowDebugLogs_Tip".Translate());
            listing.CheckboxLabeled("MF_Setting_EnableUIPatch".Translate(), ref settings.enableUIPatch, "MF_Setting_EnableUIPatch_Tip".Translate());
            listing.CheckboxLabeled("MF_OnlyExistingXenotypes".Translate(), ref settings.onlyExistingXenotypes, "MF_OnlyExistingXenotypes_Desc".Translate());
            listing.CheckboxLabeled("MF_GenerateHybrids".Translate(), ref settings.generateHybrids, "MF_GenerateHybrids_Desc".Translate());
            if (settings.generateHybrids)
            {
                listing.Label("  " + "MF_Setting_HybridInheritanceMode".Translate());
                if (listing.RadioButton("    " + "MF_HybridMode_OnlyInheritable".Translate(), settings.hybridInheritanceMode == HybridInheritanceMode.OnlyInheritable))
                    settings.hybridInheritanceMode = HybridInheritanceMode.OnlyInheritable;
                if (listing.RadioButton("    " + "MF_HybridMode_SeparateCategories".Translate(), settings.hybridInheritanceMode == HybridInheritanceMode.SeparateCategories))
                    settings.hybridInheritanceMode = HybridInheritanceMode.SeparateCategories;
                if (listing.RadioButton("    " + "MF_HybridMode_AllInheritable".Translate(), settings.hybridInheritanceMode == HybridInheritanceMode.AllInheritable))
                    settings.hybridInheritanceMode = HybridInheritanceMode.AllInheritable;

                listing.Gap(6f);
                listing.Label("  " + "MF_Setting_HybridSpawnChance".Translate(settings.hybridSpawnChance.ToString("F0")));
                settings.hybridSpawnChance = listing.Slider(settings.hybridSpawnChance, 0f, 100f);
                
                listing.Label("  " + "MF_Setting_MigrationChanceRandom".Translate(settings.migrationChanceRandom.ToString("F0")));
                settings.migrationChanceRandom = listing.Slider(settings.migrationChanceRandom, 0f, 100f);

                listing.Label("  " + "MF_Setting_MigrationChanceIndependence".Translate(settings.migrationChanceIndependence.ToString("F0")));
                settings.migrationChanceIndependence = listing.Slider(settings.migrationChanceIndependence, 0f, 100f);

                listing.Gap(6f);
                listing.Label("  " + "MF_Setting_GeneticEvolution".Translate());
                listing.CheckboxLabeled("    " + "MF_Setting_AllowArchiteGenes".Translate(), ref settings.allowArchiteGenes, "MF_Setting_AllowArchiteGenes_Tip".Translate());
                
                listing.Label("    " + "MF_Setting_MinMetabolism".Translate(settings.minMetabolism));
                settings.minMetabolism = (int)listing.Slider(settings.minMetabolism, -30f, 10f);

                listing.Label("    " + "MF_Setting_MaxNewGenes".Translate(settings.maxNewGenes));
                settings.maxNewGenes = (int)listing.Slider(settings.maxNewGenes, 1f, 10f);
                
                listing.Gap(6f);
                listing.CheckboxLabeled("  " + "MF_Setting_EnableFactionCulling".Translate(), ref settings.enableFactionCulling, "MF_Setting_EnableFactionCulling_Tip".Translate());
                if (settings.enableFactionCulling)
                {
                    listing.Label("    " + "MF_Setting_FactionCullDays".Translate(settings.factionCullDays));
                    settings.factionCullDays = (int)listing.Slider(settings.factionCullDays, 1f, 300f);
                }
            }
            
            listing.Gap(6f);
            listing.Label("MF_Setting_TriggerIntervalHours".Translate() + ": " + settings.triggerIntervalHours);
            settings.triggerIntervalHours = (int)listing.Slider(settings.triggerIntervalHours, 1f, 168f);
            
            listing.Gap(6f);
            listing.Label("MF_Setting_MaxFactionsCount".Translate() + ": " + settings.maxFactionsCount);
            settings.maxFactionsCount = (int)listing.Slider(settings.maxFactionsCount, 1f, 100f);
            
            listing.Gap(6f);
            listing.Label("MF_Setting_PlayerRelationChances".Translate());
            float prVal;
            listing.Label("  - " + "MF_Setting_PlayerRelationNeutralChance".Translate() + ": " + settings.playerRelationNeutralChance.ToString("F0") + "%");
            prVal = listing.Slider(settings.playerRelationNeutralChance, 0f, 100f);
            if (prVal != settings.playerRelationNeutralChance) AdjustPlayerRelationWeights(0, prVal);

            listing.Label("  - " + "MF_Setting_PlayerRelationHostileChance".Translate() + ": " + settings.playerRelationHostileChance.ToString("F0") + "%");
            prVal = listing.Slider(settings.playerRelationHostileChance, 0f, 100f);
            if (prVal != settings.playerRelationHostileChance) AdjustPlayerRelationWeights(1, prVal);

            listing.Label("  - " + "MF_Setting_PlayerRelationPermHostileChance".Translate() + ": " + settings.playerRelationPermHostileChance.ToString("F0") + "%");
            prVal = listing.Slider(settings.playerRelationPermHostileChance, 0f, 100f);
            if (prVal != settings.playerRelationPermHostileChance) AdjustPlayerRelationWeights(2, prVal);

            listing.Gap(12f);
            DrawXenotypeSection(listing, ref scrollViewRect);
            DrawArchetypeSection(listing, ref scrollViewRect);
            listing.GapLine(24f);

            // ================== ГРУППА 2: СЛУЧАЙНЫЕ ФРАКЦИИ ==================
            DrawHeader(listing, "MF_Settings_Group_Random".Translate());

            listing.Label("MF_Setting_RandomSettlementChance".Translate() + ": " + settings.randomSettlementChance.ToString("F1") + "%");
            settings.randomSettlementChance = listing.Slider(settings.randomSettlementChance, 0f, 100f);

            listing.Label("MF_Setting_SpawnOnRuinsChance".Translate() + ": " + settings.spawnOnRuinsChance.ToString("F1") + "%");
            settings.spawnOnRuinsChance = listing.Slider(settings.spawnOnRuinsChance, 0f, 100f);

            listing.Label("MF_Setting_HybridGroupsChance".Translate(settings.hybridGroupsChance.ToString("F0")));
            settings.hybridGroupsChance = listing.Slider(settings.hybridGroupsChance, 0f, 100f);

            if (settings.hybridGroupsChance > 0)
            {
                listing.Label("MF_Setting_HybridGroupsMin".Translate() + ": " + settings.hybridGroupsMin);
                settings.hybridGroupsMin = (int)listing.Slider(settings.hybridGroupsMin, 1f, 10f);
                
                listing.Label("MF_Setting_HybridGroupsMax".Translate() + ": " + settings.hybridGroupsMax);
                settings.hybridGroupsMax = (int)listing.Slider(settings.hybridGroupsMax, 1f, 10f);
                if (settings.hybridGroupsMax < settings.hybridGroupsMin) settings.hybridGroupsMax = settings.hybridGroupsMin;
            }

            listing.Gap(12f);
            DrawTechWeightSliders(listing);

            listing.Label("MF_Setting_IdeoInheritChance_Random".Translate() + ": " + settings.randomSpawnIdeoInheritChance.ToString("F0") + "%");
            settings.randomSpawnIdeoInheritChance = listing.Slider(settings.randomSpawnIdeoInheritChance, 0f, 100f);

            listing.GapLine(24f);

            // ================== ГРУППА 3: НЕЗАВИСИМОСТЬ ФРАКЦИЙ ==================
            DrawHeader(listing, "MF_Settings_Group_Independence".Translate());

            listing.Label("MF_Setting_IndependenceSpawnChance".Translate() + ": " + settings.independenceSpawnChance.ToString("F1") + "%");
            settings.independenceSpawnChance = listing.Slider(settings.independenceSpawnChance, 0f, 100f);

            listing.Label("MF_Setting_ChanceTechAdvance".Translate() + ": " + settings.chanceTechAdvance.ToString("F1") + "%");
            settings.chanceTechAdvance = listing.Slider(settings.chanceTechAdvance, 0f, 100f);

            listing.Label("MF_Setting_ChanceTechRegress".Translate() + ": " + settings.chanceTechRegress.ToString("F1") + "%");
            settings.chanceTechRegress = listing.Slider(settings.chanceTechRegress, 0f, 100f);

            listing.Label("MF_Setting_MaxTechLevelForIndependence".Translate() + ": " + settings.maxTechLevelForIndependence.ToStringHuman());
            settings.maxTechLevelForIndependence = (TechLevel)Mathf.RoundToInt(listing.Slider((int)settings.maxTechLevelForIndependence, (int)TechLevel.Neolithic, (int)TechLevel.Ultra));

            listing.Label("MF_Setting_MinSettlementsForIndependence".Translate() + ": " + settings.minSettlementsForIndependence);
            settings.minSettlementsForIndependence = (int)listing.Slider(settings.minSettlementsForIndependence, 1f, 50f);

            listing.Gap(6f);
            listing.Label("MF_Setting_RelationsChances".Translate());
            
            float val;
            string hostileLabel = "  - " + "MF_Setting_RelationsHostileChance".Translate() + ": " + settings.chanceRelationsHostile.ToString("F0") + "%";
            Rect hostileRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(hostileRect, hostileLabel);
            TooltipHandler.TipRegion(hostileRect, "MF_Setting_RelationsHostileChance_Tip".Translate());
            val = listing.Slider(settings.chanceRelationsHostile, 0f, 100f);
            if (val != settings.chanceRelationsHostile) AdjustRelationWeights(0, val);

            string neutralLabel = "  - " + "MF_Setting_RelationsNeutralChance".Translate() + ": " + settings.chanceRelationsNeutral.ToString("F0") + "%";
            Rect neutralRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(neutralRect, neutralLabel);
            TooltipHandler.TipRegion(neutralRect, "MF_Setting_RelationsNeutralChance_Tip".Translate());
            val = listing.Slider(settings.chanceRelationsNeutral, 0f, 100f);
            if (val != settings.chanceRelationsNeutral) AdjustRelationWeights(1, val);

            string allyLabel = "  - " + "MF_Setting_RelationsAllyChance".Translate() + ": " + settings.chanceRelationsAlly.ToString("F0") + "%";
            Rect allyRect = listing.GetRect(Text.LineHeight);
            Widgets.Label(allyRect, allyLabel);
            TooltipHandler.TipRegion(allyRect, "MF_Setting_RelationsAllyChance_Tip".Translate());
            val = listing.Slider(settings.chanceRelationsAlly, 0f, 100f);
            if (val != settings.chanceRelationsAlly) AdjustRelationWeights(2, val);


            listing.GapLine(24f);
            
            if (listing.ButtonText("MF_Setting_ResetButton".Translate()))
            {
                settings.randomSettlementChance = 5f;
                settings.spawnOnRuinsChance = 10f;
                settings.hybridGroupsChance = 50f;
                settings.hybridGroupsMin = 1;
                settings.hybridGroupsMax = 3;
                settings.showDebugLogs = false;
                settings.enableUIPatch = true;
                settings.triggerIntervalHours = 23;
                settings.requireCommsForNews = false;
                settings.generateHybrids = true;
                settings.hybridInheritanceMode = HybridInheritanceMode.OnlyInheritable;
                settings.hybridSpawnChance = 30f;
                settings.migrationChanceRandom = 30f;
                settings.migrationChanceIndependence = 30f;
                settings.maxFactionsCount = 20;
                settings.enableFactionCulling = false;
                settings.factionCullDays = 30;
                settings.weightNeolithic = 35f;
                settings.weightMedieval = 30f;
                settings.weightIndustrial = 20f;
                settings.weightSpacer = 10f;
                settings.weightUltra = 5f;
                settings.independenceSpawnChance = 5f;
                settings.minSettlementsForIndependence = 5;
                settings.chanceRelationsHostile = 33.3f;
                settings.chanceRelationsNeutral = 33.3f;
                settings.chanceRelationsAlly = 33.4f;
                settings.playerRelationNeutralChance = 45f;
                settings.playerRelationHostileChance = 45f;
                settings.playerRelationPermHostileChance = 10f;
                settings.randomSpawnIdeoInheritChance = 70f;
                settings.chanceTechRegress = 5f;
                settings.maxTechLevelForIndependence = TechLevel.Ultra;
                
                settings.allowArchiteGenes = false;
                settings.minMetabolism = -10;
                settings.maxNewGenes = 2;

                settings.enabledArchetypes.Clear();
                settings.enabledXenotypes.Clear();
            }
            
            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawHeader(Listing_Standard listing, string label)
        {
            listing.Gap(12f);
            Text.Font = GameFont.Medium;
            GUI.color = Color.cyan;
            listing.Label(label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            listing.GapLine(6f);
        }

        private void DrawRelationSliders(Listing_Standard listing, string header, string label1, ref float val1, string label2, ref float val2)
        {
            listing.Label(header);
            listing.Label("  - " + label1 + ": " + val1.ToString("F0") + "%");
            float newVal = listing.Slider(val1, 0f, 100f);
            if (newVal != val1) { val1 = newVal; val2 = 100f - newVal; }

            listing.Label("  - " + label2 + ": " + val2.ToString("F0") + "%");
            newVal = listing.Slider(val2, 0f, 100f);
            if (newVal != val2) { val2 = newVal; val1 = 100f - newVal; }
        }

        private void DrawTechWeightSliders(Listing_Standard listing)
        {
            float val;
            listing.Label("MF_Setting_WeightNeolithic".Translate() + ": " + settings.weightNeolithic.ToString("F0") + "%");
            val = listing.Slider(settings.weightNeolithic, 0f, 100f);
            if (val != settings.weightNeolithic) AdjustWeights(0, val);

            listing.Label("MF_Setting_WeightMedieval".Translate() + ": " + settings.weightMedieval.ToString("F0") + "%");
            val = listing.Slider(settings.weightMedieval, 0f, 100f);
            if (val != settings.weightMedieval) AdjustWeights(1, val);

            listing.Label("MF_Setting_WeightIndustrial".Translate() + ": " + settings.weightIndustrial.ToString("F0") + "%");
            val = listing.Slider(settings.weightIndustrial, 0f, 100f);
            if (val != settings.weightIndustrial) AdjustWeights(2, val);

            listing.Label("MF_Setting_WeightSpacer".Translate() + ": " + settings.weightSpacer.ToString("F0") + "%");
            val = listing.Slider(settings.weightSpacer, 0f, 100f);
            if (val != settings.weightSpacer) AdjustWeights(3, val);

            listing.Label("MF_Setting_WeightUltra".Translate() + ": " + settings.weightUltra.ToString("F0") + "%");
            val = listing.Slider(settings.weightUltra, 0f, 100f);
            if (val != settings.weightUltra) AdjustWeights(4, val);
        }

        private void DrawArchetypeSection(Listing_Standard listing, ref Rect scrollViewRect)
        {
            string labelSuffix = showArchetypeSettings ? " [^]" : " [v]";
            if (listing.ButtonText("MF_Setting_Archetypes_Title".Translate() + labelSuffix))
            {
                showArchetypeSettings = !showArchetypeSettings;
            }

            if (showArchetypeSettings)
            {
                listing.GapLine();

                var allArchetypes = DefDatabase<FactionDef>.AllDefs
                    .Where(d => !d.defName.StartsWith("MF_") && d.defName != "Empire" && d.pawnGroupMakers != null && !d.isPlayer && !d.hidden && d.techLevel > TechLevel.Animal)
                    .OrderBy(d => d.techLevel).ThenBy(d => d.label).ToList();

                scrollViewRect.height += (allArchetypes.Count * 28f) + 200f;

                TechLevel lastTech = TechLevel.Undefined;
                foreach (var def in allArchetypes)
                {
                    if (def.techLevel != lastTech)
                    {
                        lastTech = def.techLevel;
                        listing.Gap(12f);
                        GUI.color = Color.cyan;
                        listing.Label(lastTech.ToString().ToUpper());
                        GUI.color = Color.white;

                        // БАЗОВЫЙ ШАБЛОН ДЛЯ ЭТОГО ТЕХ УРОВНЯ
                        string baseKey = "MF_BaseTemplate_" + lastTech.ToString();
                        if (!settings.enabledArchetypes.ContainsKey(baseKey)) settings.enabledArchetypes[baseKey] = false;
                        bool baseV = settings.enabledArchetypes[baseKey];
                        
                        string baseLabel = "MF_Setting_BaseTemplate_Label".Translate();
                        if (baseLabel == "MF_Setting_BaseTemplate_Label") baseLabel = "Base Faction Template";
                        string baseDesc = "MF_Setting_BaseTemplate_Desc".Translate();
                        if (baseDesc == "MF_Setting_BaseTemplate_Desc") baseDesc = "Allows the faction to occasionally keep its original baseline pawns instead of copying an archetype.";
                        
                        listing.CheckboxLabeled("  " + baseLabel + " (" + lastTech.ToString() + ")", ref baseV, baseDesc);
                        settings.enabledArchetypes[baseKey] = baseV;
                    }
                    if (!settings.enabledArchetypes.ContainsKey(def.defName)) settings.enabledArchetypes[def.defName] = true;
                    bool v = settings.enabledArchetypes[def.defName];
                    listing.CheckboxLabeled("  " + def.LabelCap + $" ({def.defName})", ref v, def.description);
                    settings.enabledArchetypes[def.defName] = v;
                }
            }
        }

        private void DrawXenotypeSection(Listing_Standard listing, ref Rect scrollViewRect)
        {
            string xenoSuffix = showXenotypeSettings ? " [^]" : " [v]";
            if (listing.ButtonText("MF_Setting_Xenotypes_Title".Translate() + xenoSuffix))
            {
                showXenotypeSettings = !showXenotypeSettings;
            }

            if (showXenotypeSettings)
            {
                listing.GapLine();
                var allXenotypes = DefDatabase<XenotypeDef>.AllDefs
                    .Where(x => !string.IsNullOrEmpty(x.label) && (x.genes == null || !x.genes.Any(g => (g.disabledWorkTags & WorkTags.Violent) != 0)) 
                           && !x.defName.StartsWith("MF_Evolution_"))
                    .OrderBy(x => x.label).ToList();

                scrollViewRect.height += (allXenotypes.Count * 28f) + 400f;

                foreach (var xDef in allXenotypes)
                {
                    string key = "Global_" + xDef.defName;
                    if (!settings.enabledXenotypes.ContainsKey(key)) settings.enabledXenotypes[key] = true;
                    bool active = settings.enabledXenotypes[key];
                    listing.CheckboxLabeled("  " + xDef.LabelCap + $" ({xDef.defName})", ref active, xDef.description);
                    settings.enabledXenotypes[key] = active;
                }

                listing.Gap(12f);
                listing.Label("MF_Setting_Xenotypes_ByTech".Translate());
                
                foreach (var tech in new[] { TechLevel.Neolithic, TechLevel.Medieval, TechLevel.Industrial, TechLevel.Spacer, TechLevel.Ultra })
                {
                    if (!showXenoTech.ContainsKey(tech)) showXenoTech[tech] = false;
                    listing.Gap(4f);
                    string techSuffix = showXenoTech[tech] ? " [^]" : " [v]";
                    if (listing.ButtonText("    " + tech.ToString().ToUpper() + techSuffix)) showXenoTech[tech] = !showXenoTech[tech];

                    if (showXenoTech[tech])
                    {
                        foreach (var xDef in allXenotypes)
                        {
                            string key = tech.ToString() + "_" + xDef.defName;
                            string globalKey = "Global_" + xDef.defName;
                            if (!settings.enabledXenotypes.ContainsKey(key)) settings.enabledXenotypes[key] = true;
                            if (!settings.enabledXenotypes.ContainsKey(globalKey)) settings.enabledXenotypes[globalKey] = true;

                            bool globalActive = settings.enabledXenotypes[globalKey];
                            bool active = settings.enabledXenotypes[key];

                            if (!globalActive)
                            {
                                GUI.color = Color.gray;
                                listing.Label("      " + xDef.LabelCap + "MF_Setting_GlobalOff".Translate());
                                GUI.color = Color.white;
                            }
                            else
                            {
                                listing.CheckboxLabeled("      " + xDef.LabelCap, ref active);
                                settings.enabledXenotypes[key] = active;
                            }
                        }
                    }
                }
            }
        }


        private void AdjustWeights(int changedIndex, float newValue)
        {
            float[] weights = { settings.weightNeolithic, settings.weightMedieval, settings.weightIndustrial, settings.weightSpacer, settings.weightUltra };
            float oldValue = weights[changedIndex];
            float delta = newValue - oldValue;
            
            weights[changedIndex] = newValue;
            
            float otherSum = 0;
            for (int i = 0; i < 5; i++) if (i != changedIndex) otherSum += weights[i];

            if (otherSum > 0.01f)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (i != changedIndex)
                    {
                        weights[i] -= delta * (weights[i] / otherSum);
                        if (weights[i] < 0) weights[i] = 0;
                    }
                }
            }
            else
            {
                float share = delta / 4f;
                for (int i = 0; i < 5; i++)
                {
                    if (i != changedIndex)
                    {
                        weights[i] = Math.Max(0, weights[i] - share);
                    }
                }
            }

            float total = 0;
            for (int i = 0; i < 5; i++) total += weights[i];
            if (total > 0)
            {
                for (int i = 0; i < 5; i++) weights[i] = (weights[i] / total) * 100f;
            }
            else
            {
                weights[changedIndex] = 100f;
            }

            settings.weightNeolithic = weights[0];
            settings.weightMedieval = weights[1];
            settings.weightIndustrial = weights[2];
            settings.weightSpacer = weights[3];
            settings.weightUltra = weights[4];
        }

        private void AdjustRelationWeights(int changedIndex, float newValue)
        {
            float[] weights = { settings.chanceRelationsHostile, settings.chanceRelationsNeutral, settings.chanceRelationsAlly };
            float oldValue = weights[changedIndex];
            float delta = newValue - oldValue;
            weights[changedIndex] = newValue;

            float otherSum = 0;
            for (int i = 0; i < 3; i++) if (i != changedIndex) otherSum += weights[i];

            if (otherSum > 0.01f)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (i != changedIndex)
                    {
                        weights[i] -= delta * (weights[i] / otherSum);
                        if (weights[i] < 0) weights[i] = 0;
                    }
                }
            }
            else
            {
                float share = delta / 2f;
                for (int i = 0; i < 3; i++)
                {
                    if (i != changedIndex) weights[i] = Math.Max(0, weights[i] - share);
                }
            }

            float total = weights[0] + weights[1] + weights[2];
            if (total > 0)
            {
                for (int i = 0; i < 3; i++) weights[i] = (weights[i] / total) * 100f;
            }
            else weights[changedIndex] = 100f;

            settings.chanceRelationsHostile = weights[0];
            settings.chanceRelationsNeutral = weights[1];
            settings.chanceRelationsAlly = weights[2];
        }

        private void AdjustPlayerRelationWeights(int changedIndex, float newValue)
        {
            float[] weights = { settings.playerRelationNeutralChance, settings.playerRelationHostileChance, settings.playerRelationPermHostileChance };
            float oldValue = weights[changedIndex];
            float delta = newValue - oldValue;
            weights[changedIndex] = newValue;

            float otherSum = 0;
            for (int i = 0; i < 3; i++) if (i != changedIndex) otherSum += weights[i];

            if (otherSum > 0.01f)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (i != changedIndex)
                    {
                        weights[i] -= delta * (weights[i] / otherSum);
                        if (weights[i] < 0) weights[i] = 0;
                    }
                }
            }
            else
            {
                float share = delta / 2f;
                for (int i = 0; i < 3; i++) if (i != changedIndex) weights[i] = Math.Max(0, weights[i] - share);
            }

            float total = weights[0] + weights[1] + weights[2];
            if (total > 0)
            {
                for (int i = 0; i < 3; i++) weights[i] = (weights[i] / total) * 100f;
            }
            else weights[changedIndex] = 100f;

            settings.playerRelationNeutralChance = weights[0];
            settings.playerRelationHostileChance = weights[1];
            settings.playerRelationPermHostileChance = weights[2];
        }
        
        public override void WriteSettings()
        {
            base.WriteSettings();
            settings.Write();
        }
        
        public override string SettingsCategory() => "More Factions";
    }
}
