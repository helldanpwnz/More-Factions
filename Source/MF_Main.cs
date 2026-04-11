using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using HarmonyLib;

namespace MoreFactions
{
    // Обертка для списка генов (для корректного сохранения в словаре)
    public class GeneListWrapper : IExposable
    {
        public List<string> geneNames = new List<string>();
        public void ExposeData()
        {
            Scribe_Collections.Look(ref geneNames, "geneNames", LookMode.Value);
            if (geneNames == null) geneNames = new List<string>();
        }
    }

    // ============================================ БЛОК 3: МЕНЕДЖЕР МИРА ============================================
    [StaticConstructorOnStartup]
    public static class MF_VEF_Fixer
    {
        static MF_VEF_Fixer()
        {
            foreach (var def in DefDatabase<FactionDef>.AllDefs.Where(d => d.defName.StartsWith("MF_") && d.modContentPack?.PackageId.ToLower() == "helldan.morefactions"))
            {
                def.hidden = true;
                def.requiredCountAtGameStart = 0;
                def.maxConfigurableAtWorldCreation = 0;
            }

            // МАГИЧЕСКОЕ РЕШЕНИЕ 2.0:
            // Генерируем 500 "теневых" заглушек программно ИСКЛЮЧИТЕЛЬНО для словаря Scribe (defsByName).
            // Не добавляем их в `DefDatabase.Add` или списки (defsList).
            // Это решает проблему и с сейвами, и с Character Editor (он о них никогда не узнает).
            var defsByName = (Dictionary<string, XenotypeDef>)AccessTools.Field(typeof(DefDatabase<XenotypeDef>), "defsByName").GetValue(null);
            
            if (defsByName != null)
            {
                for (int i = 1; i <= 500; i++)
                {
                    string defName = $"MF_Evolution_{i:D3}";
                    if (!defsByName.ContainsKey(defName))
                    {
                        var stubDef = new XenotypeDef
                        {
                            defName = defName,
                            label = "Hybrid",
                            inheritable = true,
                            description = "A unique evolutionary subspecies.",
                            descriptionShort = "A unique evolutionary subspecies.",
                            iconPath = "UI/Icons/Xenotypes/Baseliner",
                            genes = new List<GeneDef>()
                        };
                        
                        defsByName[defName] = stubDef;
                    }
                }
            }
        }
    }
    
    // Класс-обертка для сохранения ксенотипов, так как XenotypeSet не умеет сохраняться сам (не IExposable)
    public class MF_XenoSaveData : IExposable
    {
        public List<XenotypeDef> xenos = new List<XenotypeDef>();
        public List<float> chances = new List<float>();

        public void ExposeData()
        {
            Scribe_Collections.Look(ref xenos, "xenos", LookMode.Def);
            Scribe_Collections.Look(ref chances, "chances", LookMode.Value);
        }

        public static MF_XenoSaveData FromSet(XenotypeSet set)
        {
            var data = new MF_XenoSaveData();
            if (set == null) return data;
            
            // Динамически ищем список шансов
            var field = HarmonyLib.AccessTools.Field(typeof(XenotypeSet), "xenotypeChances") 
                     ?? HarmonyLib.AccessTools.Field(typeof(XenotypeSet), "individualXenotypeChances");

            if (field != null)
            {
                var list = field.GetValue(set) as System.Collections.IEnumerable;
                if (list != null)
                {
                    foreach (var obj in list)
                    {
                        var def = HarmonyLib.AccessTools.Field(obj.GetType(), "xenotype").GetValue(obj) as XenotypeDef;
                        var val = (float)HarmonyLib.AccessTools.Field(obj.GetType(), "chance").GetValue(obj);
                        if (def != null) { data.xenos.Add(def); data.chances.Add(val); }
                    }
                }
            }
            return data;
        }

        public XenotypeSet ToSet()
        {
            var set = new XenotypeSet();

            // ФИНАЛЬНЫЙ ФИКС Baseliner 0%: обнуляем поле шанса
            var baselinerField = HarmonyLib.AccessTools.Field(typeof(XenotypeSet), "baselinerChance");
            if (baselinerField != null) baselinerField.SetValue(set, 0f);

            var field = HarmonyLib.AccessTools.Field(typeof(XenotypeSet), "xenotypeChances") 
                     ?? HarmonyLib.AccessTools.Field(typeof(XenotypeSet), "individualXenotypeChances");

            if (field != null && xenos.Count > 0)
            {
                var newList = Activator.CreateInstance(field.FieldType) as System.Collections.IList;
                var entryType = field.FieldType.GetGenericArguments()[0];

                for (int i = 0; i < xenos.Count; i++)
                {
                    if (xenos[i] == null) continue;
                    var entry = Activator.CreateInstance(entryType);
                    HarmonyLib.AccessTools.Field(entryType, "xenotype").SetValue(entry, xenos[i]);
                    HarmonyLib.AccessTools.Field(entryType, "chance").SetValue(entry, chances[i]);
                    newList.Add(entry);
                }
                field.SetValue(set, newList);
            }
            return set;
        }
    }

    [StaticConstructorOnStartup]
    public class MoreFactionsManager : WorldComponent
    {
        public List<FactionDef> allHiddenFactionDefs = new List<FactionDef>();
        public List<FactionDef> activePool = new List<FactionDef>();
        public List<FactionDef> usedDefs = new List<FactionDef>();
        public Dictionary<int, string> factionArchetypes = new Dictionary<int, string>();
        public Dictionary<int, string> factionIcons = new Dictionary<int, string>();
        public Dictionary<int, bool> factionPermanentEnemy = new Dictionary<int, bool>();
        public static Dictionary<TechLevel, int> maxIconCounts = new Dictionary<TechLevel, int>();
        public Dictionary<int, MF_XenoSaveData> mutatedXenos = new Dictionary<int, MF_XenoSaveData>();

        // Persistent storage for dynamic xenotypes (indexed by defName)
        public Dictionary<string, string> savedLabels = new Dictionary<string, string>();
        public Dictionary<string, string> savedDescriptions = new Dictionary<string, string>();
        public Dictionary<string, GeneListWrapper> savedGenes = new Dictionary<string, GeneListWrapper>();

        public Dictionary<int, GeneListWrapper> hybridXenoGenes = new Dictionary<int, GeneListWrapper>();
        public Dictionary<int, string> hybridXenoLabels = new Dictionary<int, string>();
        public Dictionary<int, string> hybridXenoDefNames = new Dictionary<int, string>();
        
        // Culling: FactionID -> Tick when it was defeated
        public Dictionary<int, int> factionDefeatTicks = new Dictionary<int, int>();
        
        private const int POOL_SIZE = 100;
        private bool initialized = false;
        private int nextTriggerTick = -1;

        private static readonly Dictionary<TechLevel, string> SpawnLetterTitles = new()
        {
            [TechLevel.Animal] = "MF_SpawnLetterTitle_Animal".Translate(),
            [TechLevel.Neolithic] = "MF_SpawnLetterTitle_Neolithic".Translate(),
            [TechLevel.Medieval] = "MF_SpawnLetterTitle_Medieval".Translate(),
            [TechLevel.Industrial] = "MF_SpawnLetterTitle_Industrial".Translate(),
            [TechLevel.Spacer] = "MF_SpawnLetterTitle_Spacer".Translate(),
            [TechLevel.Ultra] = "MF_SpawnLetterTitle_Ultra".Translate(),
            [TechLevel.Archotech] = "MF_SpawnLetterTitle_Archotech".Translate()
        };      
        

        private static readonly Dictionary<TechLevel, string> SpawnDescriptions = new()
        {
            [TechLevel.Neolithic] = "MF_SpawnDescription_Neolithic".Translate(),
            [TechLevel.Medieval] = "MF_SpawnDescription_Medieval".Translate(),
            [TechLevel.Industrial] = "MF_SpawnDescription_Industrial".Translate(),
            [TechLevel.Spacer] = "MF_SpawnDescription_Spacer".Translate(),
            [TechLevel.Ultra] = "MF_SpawnDescription_Ultra".Translate()
        };

        public MoreFactionsManager(World world) : base(world) { }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            
            // КРИТИЧЕСКИЙ СБРОС: Перед восстановлением нужно сбросить ВСЕ дефы в дефолтное состояние (скрытые).
            // Это решает проблему "утечки" данных из прошлого сейва в текущую сессию RAM.
            foreach (var def in DefDatabase<FactionDef>.AllDefs.Where(d => d.defName.StartsWith("MF_") && d.modContentPack?.PackageId.ToLower() == "helldan.morefactions"))
            {
                def.hidden = true;
            }

            // Сбрасываем статические кеши других классов
            MF_CommsUtility.ResetCheck();
            
            // Восстанавливаем видимость только для реально живых фракций
            var settlements = Find.WorldObjects.Settlements;
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                if (faction.def != null && faction.def.defName.StartsWith("MF_") && faction.def.modContentPack?.PackageId.ToLower() == "helldan.morefactions")
                {
                    // Если у фракции есть хоть одно поселение - раскрываем её тип в базе
                    if (settlements.Any(s => s.Faction == faction))
                    {
                        faction.def.hidden = false;
                    }
                    
                    // 1. Восстанавливаем данные архетипа (пешки, идеология и т.д.)
                    if (factionArchetypes != null && factionArchetypes.TryGetValue(faction.loadID, out string sourceDefNames))
                    {
                        var sources = sourceDefNames.Split('|').Select(n => DefDatabase<FactionDef>.GetNamedSilentFail(n)).Where(s => s != null).ToList();
                        if (sources.Count > 0)
                        {
                            // КРИТИЧЕСКИЙ ФИКС: Передаем сохраненный статус враждебности
                            bool isPermEnemy = factionPermanentEnemy.TryGetValue(faction.loadID, out bool savedVal) ? savedVal : sources[0].permanentEnemy;
                            MF_FactionRandomizer.ApplyData(faction.def, sources, false, faction.loadID, fixedPermEnemy: isPermEnemy);
                        }
                    }

                    // 2. Восстанавливаем мутировавшие гены из сейва
                    if (mutatedXenos != null && mutatedXenos.TryGetValue(faction.loadID, out MF_XenoSaveData savedData))
                    {
                        faction.def.xenotypeSet = savedData.ToSet();
                    }

                    // 3. Восстанавливаем иконку
                    if (factionIcons != null && factionIcons.TryGetValue(faction.loadID, out string iconPath))
                    {
                        faction.def.factionIconPath = iconPath;
                        if (MoreFactionsMod.settings.showDebugLogs)
                            Log.Message($"[MF] Иконка фракции {faction.Name} восстановлена: {iconPath}");
                    }
                }
            }

            // 3. Восстанавливаем динамические данные (labels, genes, descriptions) для всех заглушек
            RestoreXenotypes();

            // Дополнительный фикс для VFE: добавляем MF_ фракции в список игнорируемых
            try
            {
                var spawningStateType = GenTypes.GetTypeInAnyAssembly("VEF.Factions.NewFactionSpawningState");
                if (spawningStateType != null)
                {
                    var component = Find.World.GetComponent(spawningStateType);
                    if (component != null)
                    {
                        var ignoreMethod = AccessTools.Method(spawningStateType, "Ignore", new Type[] { typeof(IEnumerable<FactionDef>) });
                        if (ignoreMethod != null)
                        {
                            var mfDefs = DefDatabase<FactionDef>.AllDefs.Where(d => d.defName.StartsWith("MF_") && d.modContentPack?.PackageId.ToLower() == "helldan.morefactions");
                            ignoreMethod.Invoke(component, new object[] { mfDefs });
                        }
                    }
                }
            }
            catch { /* Ignore if VFE not present */ }
        }

        public override void WorldComponentTick()
        {
            int currentTick = Find.TickManager.TicksGame;

            // ОПТИМИЗАЦИЯ: выполняем логику только раз в 2500 тиков (1 час)
            if (currentTick % 2500 != 0 && initialized) return;

            // Ежедневная проверка Culling-системы
            if (currentTick % 60000 == 0 && initialized) PerformCulling();

            if (!initialized && currentTick > 100)
            {
                Initialize();
                initialized = true;
            }

            int interval = MoreFactionsMod.settings.triggerIntervalHours * 2500;

            if (nextTriggerTick < 0) 
            {
                nextTriggerTick = currentTick + interval + Rand.Range(-interval / 4, interval / 4);
            }

            // Если интервал в настройках уменьшен - сбрасываем таймер для пересчета
            if (nextTriggerTick > currentTick + interval + 10000) nextTriggerTick = -1;

            if (currentTick >= nextTriggerTick)
            {
                nextTriggerTick = currentTick + interval;

                int currentFactionsCount = Find.FactionManager.AllFactions.Count(f => f.def.defName.StartsWith("MF_") && f.def.modContentPack?.PackageId.ToLower() == "helldan.morefactions");
                if (currentFactionsCount >= MoreFactionsMod.settings.maxFactionsCount)
                {
                    if (MoreFactionsMod.settings.showDebugLogs) Log.Message($"[MF] Спавн отменен: достигнут лимит фракций ({currentFactionsCount}/{MoreFactionsMod.settings.maxFactionsCount})");
                    return;
                }

                // Независимая проверка шанса на спавн в руинах
                if (Rand.Value < MoreFactionsMod.settings.spawnOnRuinsChance / 100f)
                {
                    if (MoreFactionsMod.settings.showDebugLogs) Log.Message($"[MF] Сработал триггер спавна В РУИНАХ!");
                    TrySpawnRandomSettlement(true);
                }

                // Независимая проверка шанса на спавн на свободном месте
                if (Rand.Value < MoreFactionsMod.settings.randomSettlementChance / 100f)
                {
                    if (MoreFactionsMod.settings.showDebugLogs) Log.Message($"[MF] Сработал триггер спавна НА СВОБОДНОМ МЕСТЕ!");
                    TrySpawnRandomSettlement(false);
                }

                // Логика независимости (отдельный файл)
                MF_IndependenceLogic.CheckAndSpawn(this);
            }
        }

        private void PerformCulling()
        {
            if (!MoreFactionsMod.settings.enableFactionCulling) return;

            int currentTick = Find.TickManager.TicksGame;
            int ticksToCull = MoreFactionsMod.settings.factionCullDays * 60000;
            bool culledAnything = false;

            var keys = hybridXenoDefNames.Keys.ToList();
            foreach (var loadID in keys)
            {
                Faction fac = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.loadID == loadID);
                
                // Если фракция уничтожена или удалена из мира
                if (fac == null || fac.defeated)
                {
                    if (!factionDefeatTicks.ContainsKey(loadID))
                    {
                        factionDefeatTicks[loadID] = currentTick; // Фиксируем время смерти
                    }
                    else if (currentTick - factionDefeatTicks[loadID] >= ticksToCull)
                    {
                        // Время вышло! Утилизируем.
                        // Очистка иконок и освобождение дефа
                        if (factionIcons.ContainsKey(loadID)) factionIcons.Remove(loadID);
                        if (fac != null && usedDefs.Contains(fac.def)) usedDefs.Remove(fac.def);

                        string oldDefName = hybridXenoDefNames[loadID];
                        savedGenes.Remove(oldDefName);
                        savedLabels.Remove(oldDefName);
                        savedDescriptions.Remove(oldDefName);
                        
                        hybridXenoDefNames.Remove(loadID);
                        hybridXenoLabels.Remove(loadID);
                        hybridXenoGenes.Remove(loadID);
                        mutatedXenos.Remove(loadID);
                        factionDefeatTicks.Remove(loadID);
                        
                        // Очистка самой "скорлупки" прямо в памяти (defsByName)
                        var defsByName = (Dictionary<string, XenotypeDef>)AccessTools.Field(typeof(DefDatabase<XenotypeDef>), "defsByName").GetValue(null);
                        if (defsByName != null && defsByName.ContainsKey(oldDefName))
                        {
                            var def = defsByName[oldDefName];
                            def.label = "Hybrid";
                            def.description = "MF_HybridDescriptionBase".Translate();
                            def.genes = new List<GeneDef>();
                        }
                        
                        culledAnything = true;
                    }
                }
                else
                {
                    // Если фракция каким-то чудом воскресла, отменяем таймер сноса
                    if (factionDefeatTicks.ContainsKey(loadID))
                    {
                        factionDefeatTicks.Remove(loadID);
                    }
                }
            }

            if (culledAnything && MoreFactionsMod.settings.showDebugLogs)
            {
                Log.Message("[MF] Culling System: Уничтоженные фракции очищены. Ксенотипы возвращены в свободный пул.");
            }
        }

        static MoreFactionsManager()
        {
            foreach (TechLevel tech in Enum.GetValues(typeof(TechLevel)))
            {
                int count = 0;
                for (int i = 1; i <= 300; i++)
                {
                    string testPath = $"UI/FactionIcons/{tech}/MF_{tech}_{i:D2}";
                    if (ContentFinder<UnityEngine.Texture2D>.Get(testPath, false) != null)
                        count = i;
                    else if (i > 50) break;
                }
                maxIconCounts[tech] = count;
            }
            Log.Message($"[MF] Сканирование иконок завершено. Найдено: Neolithic({maxIconCounts[TechLevel.Neolithic]}), Medieval({maxIconCounts[TechLevel.Medieval]}), Industrial({maxIconCounts[TechLevel.Industrial]}), Spacer({maxIconCounts[TechLevel.Spacer]}), Ultra({maxIconCounts[TechLevel.Ultra]})");
        }

        private void Initialize()
        {
            ResetPlaceholders();

            allHiddenFactionDefs = DefDatabase<FactionDef>.AllDefs
                .Where(d => d.defName.StartsWith("MF_") && d.modContentPack?.PackageId.ToLower() == "helldan.morefactions" && d.maxConfigurableAtWorldCreation == 0).ToList();

            // Проверяем лидеров и инициализируем данные для всех фракций мода
            foreach (var faction in Find.FactionManager.AllFactions)
            {
                if (faction.def != null && faction.def.defName.StartsWith("MF_") && faction.def.modContentPack?.PackageId.ToLower() == "helldan.morefactions")
                {
                    MF_FactionRandomizer.EnsureLeader(faction);
                    
                    if (factionIcons.TryGetValue(faction.loadID, out string iconPath))
                    {
                        faction.def.factionIconPath = iconPath;
                        try {
                            var field = typeof(FactionDef).GetField("factionIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (field != null) field.SetValue(faction.def, null);
                        } catch { }
                    }
                    else if (!factionArchetypes.ContainsKey(faction.loadID))
                    {
                        MF_FactionRandomizer.Randomize(faction);
                    }
                }
            }
            
            if (MoreFactionsMod.settings.showDebugLogs)
            {
                Log.Message($"[MF] Инициализация завершена. Доступно фракций: {allHiddenFactionDefs.Count}");
            }

            RefreshActivePool();
            usedDefs.Clear();
        }

        public void RefreshActivePool()
        {
            activePool.Clear();
            var shuffled = allHiddenFactionDefs.InRandomOrder().Take(POOL_SIZE).ToList();
            activePool.AddRange(shuffled);
        }
        
        public void RestoreXenotypes()
        {
            if (!ModsConfig.BiotechActive) return;

            // СБРОС: Перед восстановлением очищаем заглушки до дефолтных значений
            ResetPlaceholders();

            foreach (var kvp in savedLabels)
            {
                string defName = kvp.Key;
                XenotypeDef def = DefDatabase<XenotypeDef>.GetNamedSilentFail(defName);
                if (def != null)
                {
                    // Бронируем ячейку в оперативной памяти (чтобы она не была перехвачена другими мирами в этой сессии)
                    MF_XenotypeUtility.memoryLease[defName] = this.GetHashCode();

                    def.label = kvp.Value;
                    
                    if (savedDescriptions.TryGetValue(defName, out string desc))
                        def.description = desc;
                    
                    if (savedGenes.TryGetValue(defName, out GeneListWrapper wrapper) && wrapper != null)
                        def.genes = wrapper.geneNames.Select(name => DefDatabase<GeneDef>.GetNamed(name, false)).Where(g => g != null).ToList(); 
                    
                    def.inheritable = true;
                }
            }
        }

        private void ReconstructHybridXenos()
        {
            // Устаревший метод, оставлен для совместимости или миграции если нужно, 
            // но теперь мы используем более надежный RestoreXenotypes()
            RestoreXenotypes();
        }

        private void ResetPlaceholders()
        {
            foreach (var def in DefDatabase<XenotypeDef>.AllDefs.Where(d => d.defName.StartsWith("MF_Evolution_")))
            {
                def.label = "Hybrid";
                def.description = "MF_HybridDescriptionBase".Translate();
                def.genes = new List<GeneDef>();
                def.inheritable = true;
                try { typeof(Def).GetMethod("ClearCachedData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.Invoke(def, null); } catch { }
            }
        }

        
        public void OnFactionUsed(FactionDef usedDef)
        {
            activePool.Remove(usedDef);
            if (activePool.Count < POOL_SIZE / 4) RefreshActivePool();
        }

        private bool TrySpawnRandomSettlement(bool onRuins = false)
        {
            if (activePool.Count == 0)
            {
                if (MoreFactionsMod.settings.showDebugLogs) Log.Message("[MF] Пул пуст, обновляю...");
                RefreshActivePool();
                if (activePool.Count == 0) return false;
            }
            
            var availableDefs = allHiddenFactionDefs.Except(usedDefs).ToList();
            if (availableDefs.Count == 0) 
            { 
                usedDefs.Clear(); 
                availableDefs = allHiddenFactionDefs.ToList(); 
            }

            if (availableDefs.Count == 0)
            {
                Log.Error("[MF] Ошибка: Список доступных фракций (availableDefs) пуст!");
                return false;
            }
            
            TechLevel selectedTech = TechLevel.Neolithic;
            var availableTechs = availableDefs.Select(d => d.techLevel).Distinct().ToList();

            Dictionary<TechLevel, float> currentWeights = new Dictionary<TechLevel, float>
            {
                [TechLevel.Neolithic] = Mathf.Floor(MoreFactionsMod.settings.weightNeolithic),
                [TechLevel.Medieval] = Mathf.Floor(MoreFactionsMod.settings.weightMedieval),
                [TechLevel.Industrial] = Mathf.Floor(MoreFactionsMod.settings.weightIndustrial),
                [TechLevel.Spacer] = Mathf.Floor(MoreFactionsMod.settings.weightSpacer),
                [TechLevel.Ultra] = Mathf.Floor(MoreFactionsMod.settings.weightUltra)
            };

            var weightedTechs = availableTechs.Where(t => currentWeights.ContainsKey(t) && currentWeights[t] > 0f).ToList();

            if (weightedTechs.Count > 0)
            {
                float totalWeight = weightedTechs.Sum(t => currentWeights[t]);
                float roll = Rand.Range(0f, totalWeight);
                float current = 0f;
                bool found = false;
                
                foreach (var tech in weightedTechs)
                {
                    current += currentWeights[tech];
                    if (roll <= current)
                    {
                        selectedTech = tech;
                        found = true;
                        break;
                    }
                }
                if (!found) selectedTech = weightedTechs.Last();
            }
            else
            {
                return false;
            }

            if (MoreFactionsMod.settings.showDebugLogs) Log.Message($"[MF] Выбран техуровень: {selectedTech}");

            var techDefs = availableDefs.Where(d => d.techLevel == selectedTech).ToList();
            if (techDefs.Count == 0) return false;

            FactionDef randomDef = techDefs.RandomElement();

            // 1. ПРЕДВАРИТЕЛЬНЫЙ ПОИСК ТАЙЛА
            int newTile = -1;
            WorldObject ruinToRemove = null;

            if (onRuins)
            {
                var ruins = Find.WorldObjects.DestroyedSettlements.ToList();
                if (!ruins.Any())
                {
                    if (MoreFactionsMod.settings.showDebugLogs) Log.Message("[MF] Отмена: руины не найдены.");
                    return false;
                }
                ruinToRemove = ruins.RandomElement();
                newTile = ruinToRemove.Tile;
            }
            else
            {
                // Ищем свободный тайл заранее с проверкой дистанции
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    int candidate = TileFinder.RandomSettlementTileFor(null);
                    if (candidate > 0 && TileFinder.IsValidTileForNewSettlement(candidate))
                    {
                        // В первые 50 попыток ищем место подальше (используем быструю геометрическую проверку)
                        if (attempt < 50)
                        {
                            bool tooClose = false;
                            var settlements = Find.WorldObjects.Settlements;
                            Vector3 candidatePos = Find.WorldGrid.GetTileCenter(candidate);
                            for (int i = 0; i < settlements.Count; i++)
                            {
                                var s = settlements[i];
                                float dist = Vector3.Distance(candidatePos, Find.WorldGrid.GetTileCenter(s.Tile));
                                if (s.Faction == Faction.OfPlayer && dist < 30f) { tooClose = true; break; }
                                if (dist < 20f) { tooClose = true; break; }
                            }
                            if (tooClose) continue;
                        }

                        newTile = candidate;
                        break;
                    }
                }
            }

            if (newTile == -1)
            {
                if (MoreFactionsMod.settings.showDebugLogs) Log.Message($"[MF] Отмена: не удалось найти тайл для {randomDef.defName}.");
                return false;
            }

            // 2. ГЕНЕРАЦИЯ ОБЪЕКТОВ (только если место уже найдено!)
            if (MoreFactionsMod.settings.showDebugLogs) Log.Message($"[MF] Место найдено ({newTile}), генерирую фракцию: {randomDef.defName}");

            float relRoll = Rand.Value * 100f;
            bool isPirate = relRoll < MoreFactionsMod.settings.playerRelationPermHostileChance;
            int startingGoodwill = isPirate ? -100 : (relRoll < MoreFactionsMod.settings.playerRelationPermHostileChance + MoreFactionsMod.settings.playerRelationNeutralChance ? 0 : -80);
            randomDef.permanentEnemy = isPirate;

            Faction newFaction = null;
            try
            {
                FactionGeneratorParms parms = new FactionGeneratorParms(randomDef);
                newFaction = FactionGenerator.NewGeneratedFaction(parms);
            }
            catch (Exception ex)
            {
                Log.Warning($"[MF] Leader generation for {randomDef.defName} caused an error, but spawning anyway: {ex.Message}");
                newFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.def == randomDef && !f.defeated);
            }
            
            if (newFaction == null)
            {
                Log.Error("[MF] Ошибка: FactionGenerator вернул null!");
                return false;
            }

            // ПРИМЕНЯЕМ РАНДОМИЗАТОР: Передаем уже решенный статус пиратства
            MF_FactionRandomizer.Randomize(newFaction, fixedPermEnemy: isPirate);
            
            usedDefs.Add(randomDef);
            
            if (ruinToRemove != null)
            {
                ruinToRemove.Destroy();
            }


            // ТЕПЕРЬ ДОБАВЛЯЕМ В МИР (В самый конец!)
            randomDef.hidden = false;
            if (!Find.FactionManager.AllFactions.Contains(newFaction))
            {
                Find.FactionManager.Add(newFaction);
            }
            
            // Установка отношений после добавления фракции в мир
            newFaction.TryAffectGoodwillWith(Faction.OfPlayer, startingGoodwill - newFaction.GoodwillWith(Faction.OfPlayer), false, false);
            
            // --- ГЕНЕТИЧЕСКАЯ ЭВОЛЮЦИЯ (ЗАГЛУШКИ) ---
            if (ModsConfig.BiotechActive && MoreFactionsMod.settings.generateHybrids && Rand.Value < MoreFactionsMod.settings.hybridSpawnChance / 100f)
            {
                XenotypeDef evolutionXeno = MF_XenotypeUtility.AssignEvolutionaryXenotype(newFaction, newFaction.def.xenotypeSet);
                MF_XenotypeUtility.SetFactionXenotype(newFaction, evolutionXeno, false);
                if (evolutionXeno != null)
                {
                    // Сохраняем генетический код (через обертку)
                    this.hybridXenoDefNames[newFaction.loadID] = evolutionXeno.defName;
                    var wrapper = new GeneListWrapper();
                    wrapper.geneNames = evolutionXeno.genes.Select(g => g.defName).ToList();
                    this.hybridXenoGenes[newFaction.loadID] = wrapper;
                    this.hybridXenoLabels[newFaction.loadID] = evolutionXeno.label;
                    
                    // КРИТИЧЕСКИЙ ФИКС: Обновляем mutatedXenos, чтобы новый гибридный состав сохранился в сейве
                    this.mutatedXenos[newFaction.loadID] = MF_XenoSaveData.FromSet(newFaction.def.xenotypeSet);
                }
            }
            
            // --- ГЕНЕРАЦИЯ УНИКАЛЬНОЙ ИДЕОЛОГИИ ---
            if (ModsConfig.IdeologyActive && newFaction.ideos != null)
            {
                Ideo randomIdeo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(newFaction.def));
                Find.IdeoManager.Add(randomIdeo);
                newFaction.ideos.SetPrimary(randomIdeo);
            }
            
            // Удаляем возможные "артефакты" спавна баз от ванильного генератора
            var artifacts = Find.WorldObjects.AllWorldObjects.Where(o => o.Faction == newFaction).ToList();
            foreach (var art in artifacts) art.Destroy();
            
            Settlement newSettlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newSettlement.SetFaction(newFaction);
            newSettlement.Tile = newTile;
            newSettlement.Name = SettlementNameGenerator.GenerateSettlementName(newSettlement, null);
            Find.WorldObjects.Add(newSettlement);
            
            foreach (var otherFaction in Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.defeated && !f.Hidden && f != newFaction))
            {
                int goodwill = Rand.Range(-100, 30);
                newFaction.TryAffectGoodwillWith(otherFaction, goodwill, false, false);
                otherFaction.TryAffectGoodwillWith(newFaction, goodwill, false, false);
            }
            
            string title = SpawnLetterTitles.TryGetValue(randomDef.techLevel, out string titleText) 
                ? titleText : "MF_SpawnLetterTitle_Default".Translate();

            string description = SpawnDescriptions.TryGetValue(randomDef.techLevel, out string desc) 
                ? "MF_SpawnDescription_WithName".Translate(desc, newSettlement.Name)
                : "MF_SpawnDescription_Default".Translate(newSettlement.Name);

            MF_EventManager.Fire("MF_RandomSettlement", title, description, LetterDefOf.NeutralEvent, newSettlement);
            
            OnFactionUsed(randomDef);
            
            if (MoreFactionsMod.settings.showDebugLogs)
                Log.Message($"[MF] СПАВН ЗАВЕРШЕН! {newFaction.Name} на тайле {newTile}");
            
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activePool, "activePool", LookMode.Def);
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref nextTriggerTick, "nextTriggerTick", -1);
            Scribe_Collections.Look(ref usedDefs, "usedDefs", LookMode.Def);
            Scribe_Collections.Look(ref factionArchetypes, "factionArchetypes", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref factionIcons, "factionIcons", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref factionPermanentEnemy, "factionPermanentEnemy", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref mutatedXenos, "mutatedXenos", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref savedLabels, "savedLabels", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref savedDescriptions, "savedDescriptions", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref savedGenes, "savedGenes", LookMode.Value, LookMode.Deep);

            Scribe_Collections.Look(ref hybridXenoGenes, "hybridXenoGenes", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref hybridXenoLabels, "hybridXenoLabels", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref hybridXenoDefNames, "hybridXenoDefNames", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref factionDefeatTicks, "factionDefeatTicks", LookMode.Value, LookMode.Value);
            
            if (savedLabels == null) savedLabels = new Dictionary<string, string>();
            if (savedDescriptions == null) savedDescriptions = new Dictionary<string, string>();
            if (savedGenes == null) savedGenes = new Dictionary<string, GeneListWrapper>();
            if (factionIcons == null) factionIcons = new Dictionary<int, string>();
            if (factionArchetypes == null) factionArchetypes = new Dictionary<int, string>();
            if (factionPermanentEnemy == null) factionPermanentEnemy = new Dictionary<int, bool>();
            if (mutatedXenos == null) mutatedXenos = new Dictionary<int, MF_XenoSaveData>();
            if (hybridXenoGenes == null) hybridXenoGenes = new Dictionary<int, GeneListWrapper>();
            if (hybridXenoLabels == null) hybridXenoLabels = new Dictionary<int, string>();
            if (hybridXenoDefNames == null) hybridXenoDefNames = new Dictionary<int, string>();
            if (factionDefeatTicks == null) factionDefeatTicks = new Dictionary<int, int>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                initialized = true;
                ReconstructHybridXenos();

                // Повторная инициализация списков дефов
                allHiddenFactionDefs = DefDatabase<FactionDef>.AllDefs
                    .Where(d => d.defName.StartsWith("MF_") && d.modContentPack?.PackageId.ToLower() == "helldan.morefactions" && d.maxConfigurableAtWorldCreation == 0)
                    .ToList();
                
                if (activePool == null) activePool = new List<FactionDef>();
                if (usedDefs == null) usedDefs = new List<FactionDef>();

                // КРИТИЧЕСКИЙ ФИКС: раскрываем фракции ПРЯМО ТУТ, не дожидаясь FinalizeInit
                // Это фиксит исчезновение фракций из списка при загрузке
                foreach (var def in DefDatabase<FactionDef>.AllDefs.Where(d => d.defName.StartsWith("MF_") && d.modContentPack?.PackageId.ToLower() == "helldan.morefactions")) def.hidden = true;
                
                var settlements = Find.WorldObjects.Settlements;
                foreach (var faction in Find.FactionManager.AllFactions)
                {
                    if (faction.def != null && faction.def.defName.StartsWith("MF_") && faction.def.modContentPack?.PackageId.ToLower() == "helldan.morefactions")
                    {
                        if (settlements.Any(s => s.Faction == faction))
                        {
                            faction.def.hidden = false;
                        }
                    }
                }
            }
        }
    }

    // ============================================ БЛОК 4: ИНТЕГРАЦИЯ СОБЫТИЙ И ПИСЕМ ============================================
    public class IncidentParms_MF : IncidentParms
    {
        public LookTargets specificLookTargets;
    }
    
    public class IncidentWorker_GenericMF : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            string label = parms.customLetterLabel ?? def.label;
            string text = parms.customLetterText ?? def.description;
            LetterDef type = parms.customLetterDef ?? LetterDefOf.NeutralEvent;
            LookTargets look = (parms as IncidentParms_MF)?.specificLookTargets;
            
            bool shouldSendLetter = true;
            if (MoreFactionsMod.settings.requireCommsForNews && type != LetterDefOf.ThreatBig && type != LetterDefOf.ThreatSmall)
            {
                if (!MF_CommsUtility.PlayerHasCommunications()) shouldSendLetter = false;
            }

            if (shouldSendLetter)
                Find.LetterStack.ReceiveLetter(label, text, type, look);
            
            return true; 
        }
    }

    public static class MF_EventManager
    {
        public static void Fire(string defName, string title, string message, LetterDef type = null, LookTargets lookTargets = null)
        {
            IncidentDef def = DefDatabase<IncidentDef>.GetNamed(defName, false);
            if (def == null) return;

            IncidentParms_MF parms = new IncidentParms_MF
            {
                target = Find.World,
                customLetterLabel = title,
                customLetterText = message,
                customLetterDef = type ?? LetterDefOf.NeutralEvent,
                specificLookTargets = lookTargets
            };
            
            def.Worker.TryExecute(parms);
        }
    }

    public static class MF_CommsUtility
    {
        private static bool cachedResult = false;
        private static int lastCheckTick = -1;
        private static readonly ThingDef CommsConsoleDef = ThingDef.Named("CommsConsole");

        public static void ResetCheck()
        {
            lastCheckTick = -1;
        }

        public static bool PlayerHasCommunications()
        {
            if (Find.TickManager.TicksGame - lastCheckTick < 60 && lastCheckTick != -1 && lastCheckTick <= Find.TickManager.TicksGame) return cachedResult;
            lastCheckTick = Find.TickManager.TicksGame;
            cachedResult = CheckMapsForComms();
            return cachedResult;
        }

        private static bool CheckMapsForComms()
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (!maps[i].IsPlayerHome) continue;
                foreach (Building building in maps[i].listerBuildings.AllBuildingsColonistOfDef(CommsConsoleDef))
                {
                    var power = building.GetComp<CompPowerTrader>();
                    if (power == null || power.PowerOn) return true;
                }
            }
            return false; // Сократил проверку других модов для чистоты, можешь вернуть, если они критичны
        }
    }
}