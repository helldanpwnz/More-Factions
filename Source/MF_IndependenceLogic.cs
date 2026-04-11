using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace MoreFactions
{
    public static class MF_IndependenceLogic
    {
        public static void CheckAndSpawn(MoreFactionsManager manager)
        {
            // ПРОВЕРКА ЛИМИТА: Если уже слишком много активных фракций от мода, расколы не происходят
            int currentMFCount = Find.FactionManager.AllFactions.Count(f => f.def.defName.StartsWith("MF_") && f.def.modContentPack?.PackageId.ToLower() == "helldan.morefactions" && !f.defeated);
            if (currentMFCount >= MoreFactionsMod.settings.maxFactionsCount) return;

            // Проверка шанса (взята из настроек)
            if (Rand.Value >= MoreFactionsMod.settings.independenceSpawnChance / 100f) return;
            
            // 1. Поиск кандидатов
            // Проверяем все фракции, которые не являются игроком, не скрыты и не побеждены (теперь и наши "MF_" тоже!)
            var candidates = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.def.hidden && !f.defeated)
                // Исключаем орбитальные и специальные фракции, которые не должны раскалываться
                .Where(f => f.def.defName != "TradersGuild" && f.def.defName != "Salvagers")
                .Where(f => Find.WorldObjects.Settlements.Count(s => s.Faction == f) >= MoreFactionsMod.settings.minSettlementsForIndependence)
                .ToList();

            if (!candidates.Any())
            {
                if (MoreFactionsMod.settings.showDebugLogs) Log.Message("[MF] Отмена независимости: нет подходящих фракций с нужным числом поселений.");
                return;
            }

            if (MoreFactionsMod.settings.showDebugLogs) Log.Message($"[MF] Сработал триггер НЕЗАВИСИМОСТИ!");

            Faction parentFaction = candidates.RandomElement();
            var parentSettlements = Find.WorldObjects.Settlements.Where(s => s.Faction == parentFaction).ToList();
            if (!parentSettlements.Any()) return;

            Settlement targetSettlement = parentSettlements.RandomElement();
            int tile = targetSettlement.Tile;

            // 2. Генерация новой фракции
            if (manager.activePool.Count == 0) manager.RefreshActivePool();
            // Выбираем из свободных дефов, исключая текущий деф родителя (чтобы не заменить Pirate на такой же Pirate)
            var availableDefs = manager.allHiddenFactionDefs.Except(manager.usedDefs).Where(d => d != parentFaction.def).ToList();
            if (!availableDefs.Any()) { manager.usedDefs.Clear(); availableDefs = manager.allHiddenFactionDefs.Where(d => d != parentFaction.def).ToList(); }
            if (!availableDefs.Any()) return;

            // --- СТРОГИЙ КОНТРОЛЬ ТЕХНО-УРОВНЯ ---
            // Новая фракция ОБЯЗАНА наследовать техуровень родителя. 
            // Если шаблоны этого уровня в моде кончились - раскол не происходит.
            var s = MoreFactionsMod.settings;
            // ОПРЕДЕЛЯЕМ ЦЕЛЕВОЙ ТЕХ-УРОВЕНЬ
            TechLevel targetTech = parentFaction.def.techLevel;
            float techRoll = Rand.Range(0f, 100f);

            // Шанс на прорыв работает только если текущий уровень ниже лимита в настройках
            if (techRoll < s.chanceTechAdvance && targetTech < s.maxTechLevelForIndependence)
            {
                targetTech = (TechLevel)((int)targetTech + 1);
            }
            else if (techRoll < s.chanceTechAdvance + s.chanceTechRegress && targetTech > TechLevel.Neolithic)
            {
                targetTech = (TechLevel)((int)targetTech - 1);
            }

            // Находим доступные дефы этого уровня
            var techDefs = manager.activePool.Where(d => d.techLevel == targetTech).ToList();
            
            // Если прорыв/деградация не нашли подходящего дефа (очень редко) - берем родительский уровень
            if (techDefs.Count == 0 && targetTech != parentFaction.def.techLevel)
            {
                targetTech = parentFaction.def.techLevel;
                techDefs = manager.activePool.Where(d => d.techLevel == targetTech).ToList();
            }

            if (techDefs.Count == 0)
            {
                if (s.showDebugLogs) Log.Message("[MF] Нет свободных дефов для отделения.");
                return;
            }

            FactionDef randomDef = techDefs.RandomElement();

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
                Log.Warning($"[MF] Leader generation for independence split ({randomDef.defName}) caused an error, but spawning anyway: {ex.Message}");
                newFaction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.def == randomDef && !f.defeated);
            }
            
            if (newFaction == null) return;

            randomDef.hidden = false;
            manager.usedDefs.Add(randomDef);

            // --- РАНДОМИЗАЦИЯ И НАСЛЕДОВАНИЕ ---
            string techSuffix = (targetTech > parentFaction.def.techLevel) ? "_Advance" : (targetTech < parentFaction.def.techLevel ? "_Regress" : "");
            newFaction.def.techLevel = targetTech; 

            // Если родитель - тоже MF_ фракция, берем её мутированные гены, иначе базовые
            XenotypeSet parentXenos = parentFaction.def.xenotypeSet;
            if (parentFaction.def.defName.StartsWith("MF_") && parentFaction.def.modContentPack?.PackageId.ToLower() == "helldan.morefactions" && manager.mutatedXenos.TryGetValue(parentFaction.loadID, out var saved))
            {
                parentXenos = saved.ToSet();
            }

            // Используем "готовый метод" рандомизации, передавая гены родителя для наследования
            MF_FactionRandomizer.Randomize(newFaction, parentXenos, fixedPermEnemy: isPirate);
            
            // --- ГЕНЕТИЧЕСКАЯ ЭВОЛЮЦИЯ (ЗАГЛУШКИ) ---
            if (ModsConfig.BiotechActive && MoreFactionsMod.settings.generateHybrids && Rand.Value < MoreFactionsMod.settings.hybridSpawnChance / 100f)
            {
                XenotypeDef evolutionXeno = MF_XenotypeUtility.AssignEvolutionaryXenotype(newFaction, parentXenos);
                MF_XenotypeUtility.SetFactionXenotype(newFaction, evolutionXeno, true);
                if (evolutionXeno != null)
                {
                    // Сохраняем "генетический паспорт" фракции (через обертку)
                    manager.hybridXenoDefNames[newFaction.loadID] = evolutionXeno.defName;
                    var newWrapper = new GeneListWrapper();
                    newWrapper.geneNames = evolutionXeno.genes.Select(g => g.defName).ToList();
                    manager.hybridXenoGenes[newFaction.loadID] = newWrapper;
                    manager.hybridXenoLabels[newFaction.loadID] = evolutionXeno.label;

                    // КРИТИЧЕСКИЙ ФИКС: Сохраняем новый гибридный состав
                    manager.mutatedXenos[newFaction.loadID] = MF_XenoSaveData.FromSet(newFaction.def.xenotypeSet);
                }
            }
            
            float parentRelRoll = Rand.Value * 100f;
            string relKey = "Neutral";
            int parentRelation = 0;
            if (parentRelRoll < MoreFactionsMod.settings.chanceRelationsHostile) { parentRelation = -80; relKey = "Hostile"; }
            else if (parentRelRoll < MoreFactionsMod.settings.chanceRelationsHostile + MoreFactionsMod.settings.chanceRelationsNeutral) { parentRelation = 0; relKey = "Neutral"; }
            else { parentRelation = 80; relKey = "Ally"; }

            // --- ОПРЕДЕЛЯЕМ СУДЬБУ ИДЕОЛОГИИ (Mode 1: Copy, Mode 2: Archetype, Mode 3: Random) ---
            int ideoMode = 1; 
            float rollForIdeo = Rand.Value;
            if (relKey == "Hostile") {
                if (rollForIdeo < 0.50f) ideoMode = 2; else if (rollForIdeo < 0.90f) ideoMode = 3; else ideoMode = 1;
            } else if (relKey == "Neutral") {
                if (rollForIdeo < 0.50f) ideoMode = 2; else if (rollForIdeo < 0.80f) ideoMode = 1; else ideoMode = 3;
            } else { // Ally
                if (rollForIdeo < 0.50f) ideoMode = 1; else if (rollForIdeo < 0.90f) ideoMode = 2; else ideoMode = 3;
            }

            // --- ГЕНЕРАЦИЯ ИДЕОЛОГИИ ---
            if (ModsConfig.IdeologyActive && newFaction.ideos != null)
            {
                if (ideoMode == 1)
                {
                    // Режим 1: Полная копия веры
                    newFaction.ideos.SetPrimary(parentFaction.ideos.PrimaryIdeo);
                }
                else
                {
                    // Режим 2 и 3: Новая вера
                    if (ideoMode == 3)
                    {
                        // В режиме 3 сбрасываем правила архетипа, чтобы вера была максимально случайной
                        newFaction.def.allowedMemes = null;
                        newFaction.def.requiredMemes = null;
                        newFaction.def.structureMemeWeights = null;
                    }

                    Ideo newIdeo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(newFaction.def));
                    Find.IdeoManager.Add(newIdeo);
                    newFaction.ideos.SetPrimary(newIdeo);
                }
            }

            // 3. Замена поселения
            targetSettlement.Destroy();
            
            Settlement newSettlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newSettlement.SetFaction(newFaction);
            newSettlement.Tile = tile;
            newSettlement.Name = SettlementNameGenerator.GenerateSettlementName(newSettlement);
            Find.WorldObjects.Add(newSettlement);


            // Отношения с Родителем
            int currentRel = newFaction.GoodwillWith(parentFaction);
            newFaction.TryAffectGoodwillWith(parentFaction, parentRelation - currentRel, false, false);
            parentFaction.TryAffectGoodwillWith(newFaction, parentRelation - currentRel, false, false);

            // Отношения с остальным миром
            foreach (var other in Find.FactionManager.AllFactions.Where(f => !f.IsPlayer && !f.defeated && !f.Hidden && f != newFaction && f != parentFaction))
            {
                int goodwill = Rand.Range(-100, 30);
                newFaction.TryAffectGoodwillWith(other, goodwill, false, false);
                other.TryAffectGoodwillWith(newFaction, goodwill, false, false);
            }

            // ТЕПЕРЬ ДОБАВЛЯЕМ В МИР (Чтобы не было уведомлений об изменении отношений)
            if (!Find.FactionManager.AllFactions.Contains(newFaction))
            {
                Find.FactionManager.Add(newFaction);
            }

            // Отношения с Игроком после добавления в мир
            newFaction.TryAffectGoodwillWith(Faction.OfPlayer, startingGoodwill - newFaction.GoodwillWith(Faction.OfPlayer), false, false);

            // 5. Уведомление
            string titleKey = "MF_IndependenceLetterTitle_" + relKey;
            if (!string.IsNullOrEmpty(techSuffix))
            {
                string techTitle = "MF_IndependenceLetterTitle" + techSuffix;
                if (techTitle.Translate() != techTitle) titleKey = techTitle;
            }

            // Пытаемся найти уникальный текст (Отношения + Тех-уровень)
            string textKey = "MF_IndependenceLetterText_" + relKey + techSuffix;
            if (textKey.Translate() == textKey) // Если ключа нет
            {
                textKey = "MF_IndependenceLetterText_" + relKey;
            }

            // Проверка на случай отсутствия ключа вообще
            if (titleKey.Translate() == titleKey) titleKey = "MF_IndependenceLetterTitle_Default";
            if (textKey.Translate() == textKey) textKey = "MF_IndependenceLetterText_Default";

            string title = titleKey.Translate(newFaction.Name);
            string description = textKey.Translate(newFaction.Name, parentFaction.Name, newSettlement.Name, ("MF_Relation_" + relKey).Translate());

            MF_EventManager.Fire("MF_RandomSettlement", title, description, LetterDefOf.NeutralEvent, newSettlement);
            manager.OnFactionUsed(randomDef);

            if (MoreFactionsMod.settings.showDebugLogs)
                Log.Message($"[MF] НЕЗАВИСИМОСТЬ! {newFaction.Name} отделилась от {parentFaction.Name} (Отношения: {relKey}, Тех: {targetTech})");
        }
    }
}
