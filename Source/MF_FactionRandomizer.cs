using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using HarmonyLib;
using System.Reflection;

namespace MoreFactions
{
    public static class MF_FactionRandomizer
    {
        public static void EnsureLeader(Faction faction)
        {
            if (faction == null || faction.leader != null) return;

            // 1. Стандартная генерация (с защитой от вылета)
            try { faction.TryGenerateNewLeader(); }
            catch { /* Игнорируем ошибку, переходим к фолбеку */ }

            // 2. Фолбек, если стандартная не сработала (например, нет подходящих групп)
            if (faction.leader == null && faction.def != null)
            {
                var fallbackKind = faction.def.fixedLeaderKinds?.FirstOrDefault() ?? faction.def.basicMemberKind;
                if (fallbackKind == null && faction.def.pawnGroupMakers != null)
                {
                    var option = faction.def.pawnGroupMakers.SelectMany(m => m.options).OrderByDescending(o => o.selectionWeight).FirstOrDefault();
                    fallbackKind = option?.kind;
                }
                if (fallbackKind != null)
                {
                    faction.leader = PawnGenerator.GeneratePawn(fallbackKind, faction);
                    if (!Find.WorldPawns.Contains(faction.leader)) Find.WorldPawns.PassToWorld(faction.leader);
                }
            }
        }

        public static void Randomize(Faction faction, XenotypeSet inheritFrom = null, bool? fixedPermEnemy = null)
        {
            if (faction == null || faction.def == null || !faction.def.defName.StartsWith("MF_") || faction.def.modContentPack?.PackageId.ToLower() != "helldan.morefactions") return;

            var templates = DefDatabase<FactionDef>.AllDefs
                .Where(d => d.techLevel == faction.def.techLevel && 
                            !(d.defName.StartsWith("MF_") && d.modContentPack?.PackageId.ToLower() == "helldan.morefactions") && 
                            d.defName != "Empire" && 
                            d.pawnGroupMakers != null &&
                            !d.isPlayer && !d.hidden &&
                            (!MoreFactionsMod.settings.enabledArchetypes.TryGetValue(d.defName, out bool enabled) || enabled))
                .ToList();

            // ДОБАВЛЯЕМ БАЗОВЫЙ ШАБЛОН:
            // Сама фракция в её оригинальном нетронутом виде (как она прописана в XML мода) 
            // выступает полноправным кандидатом-архетипом.
            // Применяется только если в настройках включен этот "Архетип по умолчанию".
            string baseKey = "MF_BaseTemplate_" + faction.def.techLevel.ToString();
            if (MoreFactionsMod.settings.enabledArchetypes.TryGetValue(baseKey, out bool baseEnabled) && baseEnabled)
            {
                templates.Add(faction.def);
            }

            // ФОЛБЕК: Если для данного техуровня всё выключено — ищем любой другой разрешенный архетип
            if (templates.Count == 0)
            {
                templates = DefDatabase<FactionDef>.AllDefs
                    .Where(d => !d.defName.StartsWith("MF_") && 
                                d.defName != "Empire" && 
                                d.pawnGroupMakers != null &&
                                !d.isPlayer && !d.hidden &&
                                MoreFactionsMod.settings.enabledArchetypes.TryGetValue(d.defName, out bool enabled) && enabled)
                    .ToList();
                
                // Если всё равно ничего нет, подкидываем базу
                if (templates.Count == 0) templates.Add(faction.def);
            }

            if (templates.Count == 0) return;

            var sources = new List<FactionDef> { templates.RandomElement() };
            bool random = inheritFrom == null;

            // Если включен шанс на гибрид - добираем доп. архетипы того же техуровня
            float hybridChance = random ? MoreFactionsMod.settings.hybridGroupsChance : MoreFactionsMod.settings.hybridIndependenceChance;
            int hMin = random ? MoreFactionsMod.settings.hybridGroupsMin : MoreFactionsMod.settings.hybridIndependenceCount;
            int hMax = random ? MoreFactionsMod.settings.hybridGroupsMax : MoreFactionsMod.settings.hybridIndependenceCount;

            if (Rand.Value < hybridChance / 100f)
            {
                int targetCount = Rand.RangeInclusive(hMin, hMax);
                if (targetCount > 1)
                {
                    var extra = templates.Where(t => !sources.Contains(t)).InRandomOrder().Take(targetCount - 1);
                    sources.AddRange(extra);
                }
            }

            // Если задано наследование - временно подменяем ксенотипы у главного донора
            XenotypeSet originalSet = null;
            var primary = sources[0];
            if (inheritFrom != null) { originalSet = primary.xenotypeSet; primary.xenotypeSet = inheritFrom; }

            // Случайным образом назначаем статус постоянного врага
            bool isPermEnemy = fixedPermEnemy ?? sources[0].permanentEnemy; 
            if (!fixedPermEnemy.HasValue && random && Rand.Value < MoreFactionsMod.settings.playerRelationPermHostileChance / 100f) isPermEnemy = true;
            
            var mgr = Find.World?.GetComponent<MoreFactionsManager>();
            if (mgr != null) mgr.factionPermanentEnemy[faction.loadID] = isPermEnemy;

            ApplyData(faction.def, sources, true, faction.loadID, random, -1f, isPermEnemy);
            
            EnsureLeader(faction);

            if (inheritFrom != null) primary.xenotypeSet = originalSet;

            if (mgr != null) mgr.factionArchetypes[faction.loadID] = string.Join("|", sources.Select(s => s.defName));

            if (MoreFactionsMod.settings.showDebugLogs)
                Log.Message($"[MF] Фракция {faction.Name} ({faction.def.defName}) рандомизирована под: {mgr.factionArchetypes[faction.loadID]} (PermHostile: {isPermEnemy})");
        }

        public static void ApplyData(FactionDef target, List<FactionDef> sources, bool mutate = true, int factionLoadID = -1, bool random = false, float forceIdeoInheritChance = -1f, bool? fixedPermEnemy = null)
        {
            if (target == null || sources == null || sources.Count == 0) return;
            var source = sources[0];

            // 1. ПОЛУЧЕНИЕ ГРУПП (с сохранением оригинала для фолбека)
            var originalPlaceholderMakers = target.pawnGroupMakers;
            
            if (sources.Count > 1)
            {
                target.pawnGroupMakers = MergeGroupMakers(sources);
            }
            else
            {
                // Безопасное копирование: защищаемся от null у донора
                target.pawnGroupMakers = source.pawnGroupMakers != null 
                    ? new List<PawnGroupMaker>(source.pawnGroupMakers) 
                    : new List<PawnGroupMaker>();
            }

            // Статус постоянного врага
            bool targetPermStatus = sources[0].permanentEnemy;
            if (fixedPermEnemy.HasValue) targetPermStatus = fixedPermEnemy.Value;
            else if (random) targetPermStatus = Rand.Value < MoreFactionsMod.settings.playerRelationPermHostileChance / 100f;

            if (target.permanentEnemy != targetPermStatus) target.permanentEnemy = targetPermStatus;

            // 2. СТРАХОВКА (ФОЛБЕК): Теперь она сработает всегда, даже если у доноров было null
            if (originalPlaceholderMakers != null)
            {
                if (target.pawnGroupMakers == null) target.pawnGroupMakers = new List<PawnGroupMaker>();

                bool hasSettlements = target.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Settlement);
                bool hasTraders = target.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Trader);
                bool hasCombat = target.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Combat);

                if (!hasSettlements || !hasTraders || !hasCombat)
                {
                    if (!hasSettlements) target.pawnGroupMakers.AddRange(originalPlaceholderMakers.Where(m => m.kindDef == PawnGroupKindDefOf.Settlement));
                    if (!hasTraders) target.pawnGroupMakers.AddRange(originalPlaceholderMakers.Where(m => m.kindDef == PawnGroupKindDefOf.Trader));
                    if (!hasCombat) target.pawnGroupMakers.AddRange(originalPlaceholderMakers.Where(m => m.kindDef == PawnGroupKindDefOf.Combat));
                }
            }

            target.apparelStuffFilter = source.apparelStuffFilter;
            target.fixedLeaderKinds = source.fixedLeaderKinds;
            target.leaderTitle = source.leaderTitle;
            target.basicMemberKind = source.basicMemberKind;
            
            // КРИТИЧЕСКИЙ ФИКС ДЛЯ ROYALTY:
            // Если мы скопировали настройки от Империи, игра может попытаться выдать титулы, 
            // что приведет к NRE. Отключаем наследование титулов для нашей фракции.
            target.royalTitleInheritanceWorkerClass = null;
            
            // Если лидер привязан к системе Royalty, это может вызвать ошибку.
            // Для безопасности убеждаемся, что мы не пытаемся использовать специфичных лордов Империи как обычных лидеров.
            if (target.fixedLeaderKinds != null && target.fixedLeaderKinds.Any(k => k.defName.StartsWith("Empire_Royal_")))
            {
                target.fixedLeaderKinds = null; // Сбрасываем, чтобы игра выбрала дефолтного лидера
            }

            // БЕЗОПАСНОЕ КОПИРОВАНИЕ mustHaveLeader (через рефлексию, чтобы не было ошибок билда)
            var mustHaveField = typeof(FactionDef).GetField("mustHaveLeader", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (mustHaveField != null) mustHaveField.SetValue(target, mustHaveField.GetValue(source));
            
            // ПРОВЕРКА НА ПОДДЕРЖКУ КСЕНОТИПОВ
            // Если архетип использует кастомные расы или автор не включил useFactionXenotypes,
            // мы не генерируем новые ксенотипы, чтобы UI отражал реальный состав фракции.
            // Строгая проверка: если ХОТЯ БЫ ОДНА гуманоидная пешка не поддерживает ксенотипы,
            // мы отменяем мутации, иначе в смешанных фракциях (люди + кошкодевочки) будет визуальный баг.
            if (mutate && target.pawnGroupMakers != null)
            {
                bool hasCustomRaces = false;
                foreach (var maker in target.pawnGroupMakers)
                {
                    var allOptions = new List<PawnGenOption>();
                    if (maker.options != null) allOptions.AddRange(maker.options);
                    if (maker.traders != null) allOptions.AddRange(maker.traders);
                    if (maker.guards != null) allOptions.AddRange(maker.guards);
                    if (maker.carriers != null) allOptions.AddRange(maker.carriers);

                    // Ищем любую ГУМАНОИДНУЮ пешку, которая игнорирует ксенотипы фракции
                    if (allOptions.Any(o => o.kind != null && o.kind.RaceProps != null && o.kind.RaceProps.Humanlike && !o.kind.useFactionXenotypes))
                    {
                        hasCustomRaces = true;
                        break;
                    }
                }
                
                if (hasCustomRaces)
                {
                    mutate = false;
                    if (MoreFactionsMod.settings.showDebugLogs)
                        Log.Message($"[MF] В архетипе {source.defName} найдены кастомные расы (без useFactionXenotypes). Рандомизация ксенотипов отменена.");
                }
            }

            if (mutate)
            {
                MutateXenotypes(target, source.xenotypeSet, target.techLevel, random);
                if (factionLoadID >= 0)
                {
                    var mgr = Find.World?.GetComponent<MoreFactionsManager>();
                    if (mgr != null) mgr.mutatedXenos[factionLoadID] = MF_XenoSaveData.FromSet(target.xenotypeSet);
                }
            }
            else
            {
                target.xenotypeSet = source.xenotypeSet;
            }

            if (ModsConfig.IdeologyActive)
            {
                // Если шанс передан явно — используем его. Иначе: для рандом спавна — из настроек, для раскола — 100% (база).
                float chance = forceIdeoInheritChance >= 0 ? forceIdeoInheritChance : (random ? MoreFactionsMod.settings.randomSpawnIdeoInheritChance : 100f);
                
                if (Rand.Value < (chance / 100f))
                {
                    target.allowedMemes = source.allowedMemes;
                    target.requiredMemes = source.requiredMemes;
                    target.structureMemeWeights = source.structureMemeWeights;
                }
                else
                {
                    target.allowedMemes = null;
                    target.requiredMemes = null;
                    target.structureMemeWeights = null;
                }
            }

            // 6. ПРИМИНЕНИЕ ИКОНКИ (Рандомизация и сохранение)
            if (mutate && factionLoadID != -1)
            {
                var mgr = Find.World?.GetComponent<MoreFactionsManager>();
                if (mgr != null)
                {
                    string techStr = target.techLevel.ToString();
                    if (MoreFactionsManager.maxIconCounts.TryGetValue(target.techLevel, out int maxCount) && maxCount > 0)
                    {
                        // --- ФИКС УНИКАЛЬНОСТИ (без настроек) ---
                        HashSet<string> usedPaths = new HashSet<string>(mgr.factionIcons.Values);
                        List<int> availableIndices = new List<int>();
                        
                        for (int i = 1; i <= maxCount; i++)
                        {
                            string testPath = $"UI/FactionIcons/{techStr}/MF_{techStr}_{i:D2}";
                            if (!usedPaths.Contains(testPath))
                            {
                                availableIndices.Add(i);
                            }
                        }

                        int idx;
                        if (availableIndices.Count > 0)
                        {
                            idx = availableIndices.RandomElement();
                        }
                        else
                        {
                            idx = Rand.RangeInclusive(1, maxCount); // Если всё занято
                        }
                        // -------------------------

                        string iconPath = $"UI/FactionIcons/{techStr}/MF_{techStr}_{idx:D2}";
                        
                        target.factionIconPath = iconPath;
                        mgr.factionIcons[factionLoadID] = iconPath;

                        if (MoreFactionsMod.settings.showDebugLogs)
                            Log.Message($"[MF] Для фракции (ID:{factionLoadID}) выбрана иконка: {iconPath} (Уникальная: {availableIndices.Count > 0})");

                        // СБРОС КЕША
                        try {
                            var field = typeof(FactionDef).GetField("factionIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (field != null) field.SetValue(target, null);
                        } catch { }
                    }
                }
            }
        }

        public static void MutateXenotypes(FactionDef target, XenotypeSet sourceSet, TechLevel tech, bool random = false)
        {
            target.xenotypeSet = new XenotypeSet();

            // Используем закэшированные поля из утилиты
            FieldInfo chancesField = MF_XenotypeUtility.XenotypeChancesField;
            FieldInfo baselinerField = MF_XenotypeUtility.BaselinerChanceField;

            if (chancesField == null) return;

            // Принудительно обнуляем шанс обычных людей для чистоты состава
            if (baselinerField != null) baselinerField.SetValue(target.xenotypeSet, 0f);

            int parentXenoCount = 0;
            if (sourceSet != null)
            {
                var sl = chancesField.GetValue(sourceSet) as System.Collections.IEnumerable;
                if (sl != null) foreach (var o in sl) parentXenoCount++;
            }

            // 1. Подготавливаем начальный список
            var entries = new List<Tuple<XenotypeDef, float>>();
            if (random)
            {
                int maxCount = Rand.RangeInclusive(1, 10);
                float prob = 1.0f;
                var pool = DefDatabase<XenotypeDef>.AllDefs.Where(x => IsXenotypeAllowed(x, tech)).InRandomOrder().ToList();
                
                if (MoreFactionsMod.settings.onlyExistingXenotypes)
                {
                    var worldXenos = MF_XenotypeUtility.GetWorldXenotypes();
                    pool = pool.Where(x => worldXenos.Contains(x)).ToList();
                }

                for (int i = 0; i < maxCount && i < pool.Count; i++)
                {
                    if (Rand.Value < prob) { entries.Add(new Tuple<XenotypeDef, float>(pool[i], 1.0f)); prob *= 0.5f; }
                    else break;
                }
            }
            else if (sourceSet != null)
            {
                var sourceList = chancesField.GetValue(sourceSet) as System.Collections.IEnumerable;
                if (sourceList != null)
                {
                    foreach (var obj in sourceList)
                    {
                        var xDef = AccessTools.Field(obj.GetType(), "xenotype").GetValue(obj) as XenotypeDef;
                        var xChance = (float)AccessTools.Field(obj.GetType(), "chance").GetValue(obj);
                        if (xDef != null) entries.Add(new Tuple<XenotypeDef, float>(xDef, xChance));
                    }
                }
            }

            // Если список пуст (например, у племени нудистов), считаем его 100% Базовыми
            if (entries.Count == 0)
            {
                entries.Add(new Tuple<XenotypeDef, float>(XenotypeDefOf.Baseliner, 1.0f));
            }

            // --- КРИТИЧЕСКИЙ ФИЛЬТР: Оставляем только те, что разрешены в настройках ---
            var filtered = entries.Where(e => IsXenotypeAllowed(e.Item1, tech)).ToList();
            
            // Если это был единственный (100%) ксенотип родителя — мы не можем его просто заменить на другой.
            // Принудительно оставляем его как костяк популяции, даже если он не проходит фильтр.
            if (filtered.Count == 0 && entries.Count == 1) 
            {
                if (MoreFactionsMod.settings.showDebugLogs) Log.Message($"[MF] Сохраняем единственный ксенотип ({entries[0].Item1.defName}), несмотря на фильтры.");
            }
            else 
            {
                entries = filtered;
            }

            // Если список всё-таки пуст - берем случайного разрешенного (или Базового если всё выключено)
            if (entries.Count == 0)
            {
                var pool = DefDatabase<XenotypeDef>.AllDefs.Where(x => IsXenotypeAllowed(x, tech)).ToList();
                if (pool.Count > 0) entries.Add(new Tuple<XenotypeDef, float>(pool.RandomElement(), 1.0f));
                else entries.Add(new Tuple<XenotypeDef, float>(XenotypeDefOf.Baseliner, 1.0f));
            }

            // Подготавливаем новый список шансов для XenotypeSet
            var newList = Activator.CreateInstance(chancesField.FieldType) as System.Collections.IList;
            var entryType = chancesField.FieldType.GetGenericArguments()[0];

            // --- ШАНС 20%: Генетическая Чистка (отключена, если у родителя был только 1 вид) ---
            XenotypeDef excludedXeno = null;
            if (entries.Count > 1 && Rand.Value < 0.20f && (random || parentXenoCount != 1))
            {
                excludedXeno = entries.OrderBy(e => e.Item2).First().Item1;
            }

            foreach (var entry in entries)
            {
                if (entry.Item1 == excludedXeno) continue;

                float drift = Rand.Range(0.5f, 1.5f);
                float newChance = entry.Item2 * drift;

                var newEntry = Activator.CreateInstance(entryType);
                AccessTools.Field(entryType, "xenotype").SetValue(newEntry, entry.Item1);
                AccessTools.Field(entryType, "chance").SetValue(newEntry, newChance);
                newList.Add(newEntry);
            }

            // --- ШАНС 20%: Доминирование одной расы ---
            if (Rand.Value < 0.20f && newList.Count > 0)
            {
                var dominantObj = newList[Rand.Range(0, newList.Count)];
                var chanceFieldVal = AccessTools.Field(dominantObj.GetType(), "chance"); 
                if (chanceFieldVal != null)
                {
                    float curr = (float)chanceFieldVal.GetValue(dominantObj);
                    chanceFieldVal.SetValue(dominantObj, curr * 2.5f);
                }
            }

            // --- ШАНС 10%: ТОТАЛЬНОЕ ДОМИНИРОВАНИЕ (превращение в монорасовую) ---
            if (Rand.Value < 0.10f && newList.Count > 0)
            {
                var dominantObj = newList[Rand.Range(0, newList.Count)];
                var chanceFieldVal = AccessTools.Field(dominantObj.GetType(), "chance"); 
                if (chanceFieldVal != null)
                {
                    float curr = (float)chanceFieldVal.GetValue(dominantObj);
                    chanceFieldVal.SetValue(dominantObj, curr * 10f);
                }
            }

            // --- ШАНС 20%: Миграция (Приток совершенно нового вида) ---
            if (Rand.Value < 0.20f)
            {
                var allXenoList = DefDatabase<XenotypeDef>.AllDefs.Where(x => IsXenotypeAllowed(x, tech)).ToList();
                
                if (MoreFactionsMod.settings.onlyExistingXenotypes)
                {
                    var worldXenos = MF_XenotypeUtility.GetWorldXenotypes();
                    allXenoList = allXenoList.Where(x => worldXenos.Contains(x)).ToList();
                }

                if (allXenoList.Count > 0)
                {
                    XenotypeDef newArrival = allXenoList.RandomElement();
                    float currentWeightSum = 0f;
                    object existingEntry = null;

                    foreach (var item in newList)
                    {
                        var fXeno = AccessTools.Field(item.GetType(), "xenotype");
                        var fChance = AccessTools.Field(item.GetType(), "chance");
                        float chanceVal = (float)fChance.GetValue(item);
                        currentWeightSum += chanceVal;
                        if (fXeno.GetValue(item) as XenotypeDef == newArrival) existingEntry = item;
                    }

                    float randomFactor = Rand.Range(0.05f, 0.25f);
                    float migrationWeight = currentWeightSum > 0f ? (currentWeightSum * randomFactor) : randomFactor;

                    if (existingEntry != null)
                    {
                        var f = AccessTools.Field(existingEntry.GetType(), "chance");
                        f.SetValue(existingEntry, (float)f.GetValue(existingEntry) + migrationWeight);
                    }
                    else 
                    {
                        var newEntry = Activator.CreateInstance(entryType);
                        AccessTools.Field(entryType, "xenotype").SetValue(newEntry, newArrival);
                        AccessTools.Field(entryType, "chance").SetValue(newEntry, migrationWeight);
                        newList.Add(newEntry);
                    }
                }
            }

            // --- ФИНАЛЬНАЯ НОРМАЛИЗАЦИЯ И ЧИСТКА ---
            float totalChance = 0f;
            foreach (var entry in newList)
            {
                var f = AccessTools.Field(entry.GetType(), "chance");
                if (f != null) totalChance += (float)f.GetValue(entry);
            }

                if (totalChance > 0f)
                {
                    var tempDict = new Dictionary<XenotypeDef, float>();
                    foreach (var entry in newList)
                    {
                        var xDef = AccessTools.Field(entry.GetType(), "xenotype").GetValue(entry) as XenotypeDef;
                        var xChance = (float)AccessTools.Field(entry.GetType(), "chance").GetValue(entry);
                        if (xDef != null)
                        {
                            if (tempDict.ContainsKey(xDef)) tempDict[xDef] += xChance;
                            else tempDict[xDef] = xChance;
                        }
                    }

                    // Очистка от мусора < 1%
                    var keys = tempDict.Keys.ToList();
                    foreach (var k in keys) if (tempDict[k] < 0.005f) tempDict.Remove(k);
                    if (tempDict.Count == 0) tempDict[XenotypeDefOf.Baseliner] = 1.0f;

                    float sumRaw = tempDict.Values.Sum();
                    var normalizedDict = new Dictionary<XenotypeDef, float>();
                    float runningSum = 0f;

                    foreach (var kvp in tempDict)
                    {
                        float norm = (float)Math.Round(kvp.Value / sumRaw, 2);
                        if (norm < 0.01f) norm = 0.01f;
                        normalizedDict[kvp.Key] = norm;
                        runningSum += norm;
                    }

                    // Убираем погрешность
                    float diff = 1.0f - runningSum;
                    if (normalizedDict.Count > 0)
                    {
                        var maxK = normalizedDict.OrderByDescending(x => x.Value).First().Key;
                        // Специально переливаем "через край"
                        normalizedDict[maxK] += diff + 0.0001f;
                    }

                    var finalValidList = Activator.CreateInstance(chancesField.FieldType) as System.Collections.IList;
                    foreach (var kvp in normalizedDict)
                    {
                        if (kvp.Value < 0.009f) continue;

                        var newEntry = Activator.CreateInstance(entryType);
                        AccessTools.Field(entryType, "xenotype").SetValue(newEntry, kvp.Key);
                        AccessTools.Field(entryType, "chance").SetValue(newEntry, kvp.Value);
                        finalValidList.Add(newEntry);
                    }
                    chancesField.SetValue(target.xenotypeSet, finalValidList);
                }
                else
                {
                    var fbEntry = Activator.CreateInstance(entryType);
                    AccessTools.Field(entryType, "xenotype").SetValue(fbEntry, XenotypeDefOf.Baseliner);
                    AccessTools.Field(entryType, "chance").SetValue(fbEntry, 1.0f);
                    var fallbackList = Activator.CreateInstance(chancesField.FieldType) as System.Collections.IList;
                    fallbackList.Add(fbEntry);
                    chancesField.SetValue(target.xenotypeSet, fallbackList);
                }
            }

        private static List<PawnGroupMaker> MergeGroupMakers(List<FactionDef> sources)
        {
            var result = new List<PawnGroupMaker>();
            var grouped = sources.SelectMany(s => s.pawnGroupMakers ?? new List<PawnGroupMaker>()).GroupBy(m => m.kindDef);

            foreach (var group in grouped)
            {
                var merged = new PawnGroupMaker { kindDef = group.Key };
                merged.commonality = group.Average(m => m.commonality);
                
                merged.options = new List<PawnGenOption>();
                merged.traders = new List<PawnGenOption>();
                merged.carriers = new List<PawnGenOption>();
                merged.guards = new List<PawnGenOption>();

                int donorCount = group.Count();
                if (donorCount == 0) continue;
                
                float targetWeightPerDonor = 100f / donorCount;

                foreach (var maker in group)
                {
                    MergeAndScale(maker.options, merged.options, targetWeightPerDonor);
                    MergeAndScale(maker.traders, merged.traders, targetWeightPerDonor);
                    MergeAndScale(maker.carriers, merged.carriers, targetWeightPerDonor);
                    MergeAndScale(maker.guards, merged.guards, targetWeightPerDonor);
                }
                
                if (merged.options.Count > 0 || merged.traders.Count > 0) result.Add(merged);
            }
            return result;
        }

        private static void MergeAndScale(List<PawnGenOption> source, List<PawnGenOption> target, float targetTotalWeight)
        {
            if (source == null || source.Count == 0 || target == null) return;

            var safeSource = source.Where(o => IsSafePawnKind(o.kind)).ToList();
            if (safeSource.Count == 0) return;

            float currentSum = safeSource.Sum(o => o.selectionWeight);
            if (currentSum <= 0) currentSum = 1f;

            float multiplier = targetTotalWeight / currentSum;

            foreach (var opt in safeSource)
            {
                target.Add(new PawnGenOption { kind = opt.kind, selectionWeight = opt.selectionWeight * multiplier });
            }
        }

        private static readonly System.Reflection.FieldInfo titleSelectOneField = typeof(PawnKindDef).GetField("titleSelectOne", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        private static bool IsSafePawnKind(PawnKindDef kind)
        {
            if (kind == null) return false;
            // Проверка на королевские титулы. Если юниту ОБЯЗАТЕЛЬНО нужен титул - он сломает нам генерацию.
            if (kind.titleRequired != null) return false;
            
            // Проверка на список возможных титулов (используем закэшированное поле)
            if (titleSelectOneField != null)
            {
                var list = titleSelectOneField.GetValue(kind) as System.Collections.IList;
                if (list != null && list.Count > 0) return false;
            }

            return true;
        }

        private static bool IsXenotypeAllowed(XenotypeDef x, TechLevel tech)
        {
            if (x == null || string.IsNullOrEmpty(x.label)) return false;
            if (x.defName.StartsWith("MF_Evolution_")) return false;
            
            // Запрет пацифистов для военных фракций
            if (x.genes != null && x.genes.Any(g => (g.disabledWorkTags & WorkTags.Violent) != 0)) return false;

            // Глобальный запрет
            if (MoreFactionsMod.settings.enabledXenotypes.TryGetValue("Global_" + x.defName, out bool globalOk) && !globalOk) return false;

            // Запрет по тех-уровню
            if (MoreFactionsMod.settings.enabledXenotypes.TryGetValue(tech.ToString() + "_" + x.defName, out bool techOk) && !techOk) return false;

            return true;
        }
    }
}
