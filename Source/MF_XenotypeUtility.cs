using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using HarmonyLib;

namespace MoreFactions
{
    public static class MF_XenotypeUtility
    {
        // Минималистичный реестр: Имя ячейки -> HashCode мира (ID сессии)
        public static Dictionary<string, int> memoryLease = new Dictionary<string, int>();

        // КЕШИРОВАНИЕ ДЛЯ ОПТИМИЗАЦИИ
        public static readonly FieldInfo XenotypeChancesField = AccessTools.Field(typeof(XenotypeSet), "xenotypeChances") 
                                                               ?? AccessTools.Field(typeof(XenotypeSet), "individualXenotypeChances")
                                                               ?? AccessTools.Field(typeof(XenotypeSet), "xenoTypes");
        public static readonly FieldInfo BaselinerChanceField = AccessTools.Field(typeof(XenotypeSet), "baselinerChance");

        private static HashSet<XenotypeDef> cachedWorldXenos = new HashSet<XenotypeDef>();
        private static int lastXenoCacheTick = -1;

        public static HashSet<XenotypeDef> GetWorldXenotypes()
        {
            if (Find.TickManager.TicksGame == lastXenoCacheTick && lastXenoCacheTick != -1) return cachedWorldXenos;
            
            var res = new HashSet<XenotypeDef>();
            if (Find.FactionManager == null) return res;
            if (XenotypeChancesField == null) return res;

            foreach (var fac in Find.FactionManager.AllFactions)
            {
                if (fac.def?.xenotypeSet == null) continue;
                var list = XenotypeChancesField.GetValue(fac.def.xenotypeSet) as System.Collections.IEnumerable;
                if (list != null)
                {
                    foreach (var o in list)
                    {
                        var xDef = AccessTools.Field(o.GetType(), "xenotype").GetValue(o) as XenotypeDef;
                        if (xDef != null) res.Add(xDef);
                    }
                }
            }
            cachedWorldXenos = res;
            lastXenoCacheTick = Find.TickManager.TicksGame;
            return res;
        }

        // Метод для захвата свободной заглушки и её настройки (Сложная гибридизация)
        public static XenotypeDef AssignEvolutionaryXenotype(Faction faction, XenotypeSet parentSet)
        {
            if (!ModsConfig.BiotechActive || parentSet == null) return null;

            var manager = Find.World.GetComponent<MoreFactionsManager>();
            XenotypeDef placeholder = FindFreePlaceholder(manager);
            if (placeholder == null) return null;

            // 1. Анализируем родительский состав
            XenotypeDef dominantXeno = GetDominantXeno(parentSet);
            List<XenotypeDef> otherXenos = GetOtherXenos(parentSet, dominantXeno);

            List<GeneDef> alphaGenes = new List<GeneDef>();
            List<GeneDef> secondaryGenes = new List<GeneDef>();

            var mode = MoreFactionsMod.settings.hybridInheritanceMode;

            // Сбор генов согласно настройкам
            void CollectGenes(XenotypeDef source, List<GeneDef> targetList)
            {
                if (source?.genes == null) return;
                
                if (mode == HybridInheritanceMode.OnlyInheritable)
                {
                    // Берем только гены исконно наследуемых рас
                    if (source.inheritable) targetList.AddRange(source.genes);
                }
                else if (mode == HybridInheritanceMode.SeparateCategories)
                {
                    // В XenotypeDef нельзя разделить на категории, 
                    // поэтому в этом режиме мы берем всё, но помечаем гибрид наследственным только если доминант наследственный
                    targetList.AddRange(source.genes);
                }
                else // AllInheritable
                {
                    // Берем всё и превращаем в наследственное
                    targetList.AddRange(source.genes);
                }
            }

            CollectGenes(dominantXeno, alphaGenes);
            foreach (var other in otherXenos) CollectGenes(other, secondaryGenes);
            secondaryGenes = secondaryGenes.Distinct().ToList();

            // Если в итоге пусто (например, OnlyInheritable у ксеногенных родителей) - фолбек на базу
            if (alphaGenes.Count == 0 && secondaryGenes.Count == 0)
            {
                alphaGenes.AddRange(XenotypeDefOf.Baseliner.genes ?? new List<GeneDef>());
            }

            // 2. Формируем базовый геном (Сплав)
            float alphaShare = Rand.Range(0.8f, 1.0f);
            List<GeneDef> resultGenes = new List<GeneDef>();

            // Берем 80-100% генов лидера
            int alphaTakeCount = (int)(alphaGenes.Count * alphaShare);
            AddGenesNonConflicting(resultGenes, alphaGenes.InRandomOrder().Take(alphaTakeCount).ToList());

            // Если есть место, берем у остальных (минимум 1, если лидер не взял 100%)
            if (alphaShare < 1.0f || (alphaGenes.Count == 0 && secondaryGenes.Count > 0))
            {
                int targetOthers = Math.Max(1, (int)(alphaGenes.Count * (1f - alphaShare)));
                AddGenesNonConflicting(resultGenes, secondaryGenes.InRandomOrder().Take(targetOthers).ToList());
            }

            // 3. Генетический дрейф (Потеря 0-2 генов)
            int lossCount = Rand.RangeInclusive(0, 2);
            for (int i = 0; i < lossCount && resultGenes.Count > 0; i++)
                resultGenes.Remove(resultGenes.RandomElement());

            // 4. Приток новых мутаций (масштабируется под Максимум)
            int gainCount = Rand.RangeInclusive(1, MoreFactionsMod.settings.maxNewGenes);
            for (int i = 0; i < gainCount; i++)
            {
                var mut = GetRandomMutation(resultGenes);
                if (mut != null) AddGenesNonConflicting(resultGenes, new List<GeneDef> { mut });
            }

            // 5. Стабилизация лимитов (Баланс)
            // Разброс лимита зависит от максимального количества новых генов (2.5x)
            int variance = (int)(2.5f * MoreFactionsMod.settings.maxNewGenes);
            int targetLimit = alphaGenes.Count + Rand.RangeInclusive(-variance, variance);
            targetLimit = Math.Max(5, Math.Min(25, targetLimit));

            // Чистим или дотягиваем до лимита
            while (resultGenes.Count > targetLimit && resultGenes.Count > 5)
                resultGenes.Remove(resultGenes.RandomElement());
            
            while (resultGenes.Count < targetLimit && resultGenes.Count < 25)
            {
                var mut = GetRandomMutation(resultGenes);
                if (mut != null) AddGenesNonConflicting(resultGenes, new List<GeneDef> { mut });
                else break;
            }

            // --- КРИТИЧЕСКИЙ ФИКС: ПРЕДОХРАНИТЕЛЬ МЕТАБОЛИЗМА ---
            int currentMet = resultGenes.Sum(g => g.biostatMet);
            if (currentMet < MoreFactionsMod.settings.minMetabolism)
            {
                var badGenes = resultGenes.Where(g => g.biostatMet < 0).OrderBy(g => g.biostatMet).ToList();
                foreach (var bg in badGenes)
                {
                    resultGenes.Remove(bg);
                    currentMet -= bg.biostatMet;
                    if (currentMet >= MoreFactionsMod.settings.minMetabolism) break;
                }
            }

            // 6. Настройка объекта
            placeholder.label = GenerateHybridName();
            placeholder.description = GenerateHybridDescription(parentSet, placeholder.label);
            placeholder.genes = resultGenes.Distinct().ToList();
            placeholder.inheritable = true;

            // Сброс кеша для корректного отображения в UI
            try { 
                typeof(Def).GetMethod("ClearCachedData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(placeholder, null);
                
                // ВОЗВРАТ В UI: Добавляем активный ксенотип обратно в defsList (чтобы редакторы его видели)
                var defsList = (List<XenotypeDef>)AccessTools.Field(typeof(DefDatabase<XenotypeDef>), "defsList").GetValue(null);
                if (defsList != null && !defsList.Contains(placeholder)) defsList.Add(placeholder);
            } catch { }

            // --- НОВОЕ: ЗАПИСЫВАЕМ В МЕНЕДЖЕР ДЛЯ СОХРАНЕНИЯ В СЕЙВ ---
            if (manager != null)
            {
                manager.savedLabels[placeholder.defName] = placeholder.label;
                manager.savedDescriptions[placeholder.defName] = placeholder.description;
                manager.savedGenes[placeholder.defName] = new GeneListWrapper { geneNames = placeholder.genes.Select(g => g.defName).ToList() };
                manager.hybridXenoDefNames[faction.loadID] = placeholder.defName;
                MF_XenotypeUtility.memoryLease[placeholder.defName] = manager.GetHashCode();
            }

            Log.Message($"[MF] Гибрид {faction.Name} ({placeholder.label}) создан. Гены: {resultGenes.Count}.");
            return placeholder;
        }

        private static string GenerateHybridDescription(XenotypeSet parentSet, string label)
        {
            var sorted = GetSortedXenotypes(parentSet);
            string origin = "MF_HybridDescriptionBase".Translate();

            if (sorted.Count > 0)
            {
                string principal = sorted[0].Key.label;

                if (sorted.Count == 1)
                {
                    origin = "MF_HybridDescriptionSingle".Translate(principal);
                }
                else if (sorted.Count == 2)
                {
                    string secondary = sorted[1].Key.label;
                    origin = "MF_HybridDescriptionPartial".Translate(principal, secondary);
                }
                else // Три и более
                {
                    string secondary = sorted[1].Key.label;
                    var extras = sorted.Skip(2).Take(3).Select(x => x.Key.label).ToList();
                    string extrasStr = string.Join(", ", extras);
                    origin = "MF_HybridDescriptionFull".Translate(principal, secondary, extrasStr);
                }
            }

            // Генерируем 3 случайных индекса 0-19 для построения краткого и хлесткого повествования
            string start = ("MF_Flavor_Start_" + Rand.Range(0, 20)).Translate();
            string connect = ("MF_Flavor_Connect_" + Rand.Range(0, 20)).Translate();
            string final = ("MF_Flavor_End_" + Rand.Range(0, 20)).Translate();

            // Каждый блок теперь отделен двойным переносом для четкой структуры
            return $"{origin}\n\n{label} — {start}\n\n{connect}\n\n{final}";
        }

        private static List<KeyValuePair<XenotypeDef, float>> GetSortedXenotypes(XenotypeSet set)
        {
            var res = new List<KeyValuePair<XenotypeDef, float>>();
            if (set == null) return res;

            var field = XenotypeChancesField;

            if (field != null)
            {
                var list = field.GetValue(set) as System.Collections.IEnumerable;
                if (list != null)
                {
                    foreach (var obj in list)
                    {
                        var def = AccessTools.Field(obj.GetType(), "xenotype").GetValue(obj) as XenotypeDef;
                        var val = (float)AccessTools.Field(obj.GetType(), "chance").GetValue(obj);
                        if (def != null && val > 0) res.Add(new KeyValuePair<XenotypeDef, float>(def, val));
                    }
                }
            }
            return res.OrderByDescending(x => x.Value).ToList();
        }

private static string GenerateHybridName()
{
    // Генерируем случайное число от 0.0 до 1.0 для определения сложности слова
    float chance = Rand.Value;

    // Всегда берем начало (Syllable1) и финал (Syllable4 — те самые "иды/яне")
    // Используем диапазон 0-199, так как мы заполнили до этого индекса
    string start = ("MF_Syllable1_" + Rand.Range(0, 301)).Translate();
    string end = ("MF_Syllable4_" + Rand.Range(0, 301)).Translate(); 

    // 1. Случай 60% (от 0.0 до 0.6): Всего 2 части (Начало + Конец)
    if (chance < 0.60f)
    {
        return (start + end).CapitalizeFirst();
    }

    // 2. Случай 30% (от 0.6 до 0.9): 3 части (Начало + Переход1 + Конец)
    if (chance < 0.9f)
    {
        string mid1 = ("MF_Syllable2_" + Rand.Range(0, 301)).Translate();
        return (start + mid1 + end).CapitalizeFirst();
    }

    // 3. Остальные 10% (от 0.9 до 1.0): Все 4 части (Начало + Переход1 + Переход2 + Конец)
    else
    {
        string mid1 = ("MF_Syllable2_" + Rand.Range(0, 301)).Translate();
        string mid2 = ("MF_Syllable3_" + Rand.Range(0, 301)).Translate();
        return (start + mid1 + mid2 + end).CapitalizeFirst();
    }
}

        private static List<XenotypeDef> GetOtherXenos(XenotypeSet set, XenotypeDef dominant)
        {
            var res = new List<XenotypeDef>();
            var chancesField = AccessTools.Field(typeof(XenotypeSet), "xenotypeChances") 
                            ?? AccessTools.Field(typeof(XenotypeSet), "individualXenotypeChances");
            if (chancesField != null)
            {
                var list = chancesField.GetValue(set) as System.Collections.IEnumerable;
                if (list != null)
                {
                    foreach (var o in list)
                    {
                        var def = AccessTools.Field(o.GetType(), "xenotype").GetValue(o) as XenotypeDef;
                        if (def != null && def != dominant) res.Add(def);
                    }
                }
            }
            return res;
        }

        private static XenotypeDef FindFreePlaceholder(MoreFactionsManager manager)
        {
            // Получаем все наши скрытые заглушки (теперь они только в defsByName, чтобы CE их не видел)
            var defsByName = (Dictionary<string, XenotypeDef>)AccessTools.Field(typeof(DefDatabase<XenotypeDef>), "defsByName").GetValue(null);
            if (defsByName == null) return null;

            var placeholders = defsByName.Values
                .Where(d => d.defName.StartsWith("MF_Evolution_"))
                .OrderBy(d => d.defName)
                .ToList();

            // Ищем ту, которая еще не занята ни одной фракцией в менеджере
            foreach (var p in placeholders)
            {
                // Если ячейка занята другим миром в этой RAM-сессии - пропускаем
                if (memoryLease.TryGetValue(p.defName, out int ownerID) && ownerID != manager.GetHashCode()) continue;

                if (!manager.hybridXenoDefNames.ContainsValue(p.defName))
                {
                    return p;
                }
            }
            return null;
        }

        public static void SetFactionXenotype(Faction faction, XenotypeDef xenoDef, bool isIndependence)
        {
            if (faction == null || faction.def?.xenotypeSet == null) return;
            
            XenotypeSet oldSet = faction.def.xenotypeSet;
            XenotypeSet newSet = new XenotypeSet();
            if (BaselinerChanceField != null) BaselinerChanceField.SetValue(newSet, 0f);
            
            var chancesField = XenotypeChancesField;

            if (chancesField != null)
            {
                Type listType = chancesField.FieldType;
                Type chanceType = listType.GetGenericArguments()[0];
                var newList = Activator.CreateInstance(listType) as System.Collections.IList;

                float remainingWeight = 1.0f;

                if (xenoDef != null)
                {
                    // 1. Генерируем СЛУЧАЙНЫЙ вес гибрида (от 1% до 100%)
                    float hybridWeight = Rand.Range(0.01f, 1.0f);
                    remainingWeight = 1.0f - hybridWeight;

                    // Добавляем Новый Гибрид
                    var hybridChance = Activator.CreateInstance(chanceType);
                    AccessTools.Field(chanceType, "xenotype").SetValue(hybridChance, xenoDef);
                    AccessTools.Field(chanceType, "chance").SetValue(hybridChance, hybridWeight);
                    newList.Add(hybridChance);
                }

                // --- БЛОК МИГРАЦИИ: Подмешиваем старых гибридов из мира ---
                var mgr = Find.World?.GetComponent<MoreFactionsManager>();
                float migrationChance = isIndependence ? MoreFactionsMod.settings.migrationChanceIndependence : MoreFactionsMod.settings.migrationChanceRandom;

                if (mgr != null && mgr.hybridXenoDefNames.Count > 0 && (Rand.Value < migrationChance / 100f) && remainingWeight > 0.1f)
                {
                    var poolRef = mgr.hybridXenoDefNames.Values.Distinct().ToList();
                    
                    if (MoreFactionsMod.settings.onlyExistingXenotypes)
                    {
                        var activeXenos = GetWorldXenotypes();
                        poolRef = poolRef.Where(name => activeXenos.Any(x => x.defName == name)).ToList();
                    }

                    var worldHybrids = poolRef
                                        .Select(name => DefDatabase<XenotypeDef>.GetNamed(name, false))
                                        .Where(x => x != null && x != xenoDef)
                                        .InRandomOrder()
                                        .Take(Rand.RangeInclusive(1, 2))
                                        .ToList();

                    foreach (var wh in worldHybrids)
                    {
                        float weight = Rand.Range(0.05f, 0.20f);
                        var migratorChance = Activator.CreateInstance(chanceType);
                        AccessTools.Field(chanceType, "xenotype").SetValue(migratorChance, wh);
                        AccessTools.Field(chanceType, "chance").SetValue(migratorChance, weight);
                        newList.Add(migratorChance);
                        // Уменьшаем остаток для оригиналов
                        remainingWeight = Math.Max(0, remainingWeight - weight);
                    }
                }

                // 2. Добавляем старых жителей (оригинальную популяцию), если осталось место
                if (remainingWeight > 0.001f)
                {
                    var oldList = chancesField.GetValue(oldSet) as System.Collections.IEnumerable;
                    if (oldList != null)
                    {
                        foreach (var oldEntry in oldList)
                        {
                            var oldXeno = AccessTools.Field(chanceType, "xenotype").GetValue(oldEntry) as XenotypeDef;
                            var oldChance = (float)AccessTools.Field(chanceType, "chance").GetValue(oldEntry);
                            float resChance = oldChance * remainingWeight;

                            // Если шанс меньше 0.5% - он будет отображаться как 0%, так что просто пропускаем его
                            if (resChance < 0.005f) continue;

                            if (oldXeno != xenoDef)
                            {
                                var newEntry = Activator.CreateInstance(chanceType);
                                AccessTools.Field(chanceType, "xenotype").SetValue(newEntry, oldXeno);
                                AccessTools.Field(chanceType, "chance").SetValue(newEntry, resChance);
                                newList.Add(newEntry);
                            }
                        }
                    }
                }

                // 3. ФИЛЬТРАЦИЯ И НОРМАЛИЗАЦИЯ (минимум 1% и сумма 100%)
                var tempDict = new Dictionary<XenotypeDef, float>();
                foreach (var entry in newList)
                {
                    XenotypeDef def = (XenotypeDef)AccessTools.Field(chanceType, "xenotype").GetValue(entry);
                    float val = (float)AccessTools.Field(chanceType, "chance").GetValue(entry);
                    if (def != null && val > 0.001f) // Изначальный фильтр мусора
                    {
                        if (tempDict.ContainsKey(def)) tempDict[def] += val;
                        else tempDict[def] = val;
                    }
                }

                // Оставляем только тех, кто вносит заметный вклад (>= 1%)
                // Это предотвращает появление "0%" в UI RimWorld
                var keys = tempDict.Keys.ToList();
                foreach (var k in keys) if (tempDict[k] < 0.009f) tempDict.Remove(k); 

                if (tempDict.Count == 0) tempDict[XenotypeDefOf.Baseliner] = 1.0f;

                float totalSumRaw = tempDict.Values.Sum();
                var normalizedDict = new Dictionary<XenotypeDef, float>();
                float runningSum = 0f;

                foreach (var kvp in tempDict)
                {
                    float norm = (float)Math.Round(kvp.Value / totalSumRaw, 2);
                    if (norm < 0.01f) norm = 0.01f; // Гарантированный пол в 1%
                    normalizedDict[kvp.Key] = norm;
                    runningSum += norm;
                }

                // Корректируем сумму до 1.0 на самом крупном элементе
                float finalDiff = 1.0f - runningSum;
                if (normalizedDict.Count > 0)
                {
                    var maxK = normalizedDict.OrderByDescending(x => x.Value).First().Key;
                    // Специально переливаем "через край", чтобы RimWorld не рисовал Baseliner
                    normalizedDict[maxK] += finalDiff + 0.0001f;
                }

                // Пересобираем финальный список для RimWorld
                newList.Clear();
                foreach (var kvp in normalizedDict)
                {
                    var newEntry = Activator.CreateInstance(chanceType);
                    AccessTools.Field(chanceType, "xenotype").SetValue(newEntry, kvp.Key);
                    AccessTools.Field(chanceType, "chance").SetValue(newEntry, kvp.Value);
                    newList.Add(newEntry);
                }

                chancesField.SetValue(newSet, newList);
                faction.def.xenotypeSet = newSet;
            }
        }

        public static XenotypeDef GetDominantXeno(XenotypeSet set)
        {
            if (set == null) return XenotypeDefOf.Baseliner;
            var field = XenotypeChancesField;
            if (field != null)
            {
                var list = field.GetValue(set) as System.Collections.IEnumerable;
                if (list != null)
                {
                    XenotypeDef bestDef = null;
                    float bestChance = -1f;
                    foreach (var obj in list)
                    {
                        var def = AccessTools.Field(obj.GetType(), "xenotype").GetValue(obj) as XenotypeDef;
                        var val = (float)AccessTools.Field(obj.GetType(), "chance").GetValue(obj);
                        if (def != null && val > bestChance) { bestChance = val; bestDef = def; }
                    }
                    if (bestDef != null) return bestDef;
                }
            }
            return XenotypeDefOf.Baseliner;
        }

        private static void AddGenesNonConflicting(List<GeneDef> current, List<GeneDef> toAdd)
        {
            foreach (var g in toAdd)
            {
                if (current.Contains(g)) continue;
                bool hasConflict = false;
                foreach (var existing in current)
                {
                    if (existing.ConflictsWith(g)) { hasConflict = true; break; }
                }
                if (!hasConflict) current.Add(g);
            }
        }

        private static GeneDef GetRandomMutation(List<GeneDef> currentGenes)
        {
            return DefDatabase<GeneDef>.AllDefs
                .Where(g => g.biostatMet != 0 && 
                            g.canGenerateInGeneSet && 
                            !string.IsNullOrEmpty(g.iconPath) &&
                            (MoreFactionsMod.settings.allowArchiteGenes || g.biostatArc == 0) && // Фильтр Архо-генов
                            !currentGenes.Contains(g) && 
                            !currentGenes.Any(cg => cg.ConflictsWith(g)))
                .ToList()
                .RandomElementWithFallback();
        }
    }
}
