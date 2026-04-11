using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MoreFactions
{
    [HarmonyPatch(typeof(FactionUIUtility), "DrawFactionRow")]
    public static class MF_FactionUI_Patch
    {
        private const float RowHeight = 80f; 
        private const float BaseIconSize = 25f; 
        private const float BaseStepY = 25f; 
        private const float FirstOffsetX = 12f; 
        private const float IconGapX = 5f;
        private const int MaxRowsWithoutScaling = 2;

        // Кешируем нужные методы и поля ванильной игры
        private static readonly FieldInfo ShowAllField = AccessTools.Field(typeof(FactionUIUtility), "showAll");
        private static readonly MethodInfo GetOngoingEventsMI = AccessTools.Method(typeof(FactionUIUtility), "GetOngoingEvents");
        private static readonly MethodInfo GetRecentEventsMI = AccessTools.Method(typeof(FactionUIUtility), "GetRecentEvents");
        private static readonly MethodInfo GetRelationKindForGoodwillMI = AccessTools.Method(typeof(FactionUIUtility), "GetRelationKindForGoodwill");
        private static readonly MethodInfo GetNaturalGoodwillExplanationMI = AccessTools.Method(typeof(FactionUIUtility), "GetNaturalGoodwillExplanation");
        private static readonly MethodInfo DrawFactionIconWithTooltipMI = AccessTools.Method(typeof(FactionUIUtility), "DrawFactionIconWithTooltip");

        // КЕШ ДЛЯ ОПТИМИЗАЦИИ: обновляем раз в секунду (60 тиков)
        private static Dictionary<int, List<Faction>> enemyCache = new Dictionary<int, List<Faction>>();
        private static int lastUpdateTick = -1;

        private static World lastWorld = null;

        private static void RefreshEnemyCache(bool showAll)
        {
            // КРИТИЧЕСКИЙ СБРОС КЕША: Если мир сменился (новая загрузка), очищаем статический кеш.
            // Это исправляет проблему "призрачных" врагов из прошлого сейва.
            if (Find.World != lastWorld)
            {
                enemyCache.Clear();
                lastUpdateTick = -1;
                lastWorld = Find.World;
            }

            if (Find.TickManager.TicksGame - lastUpdateTick < 60 && lastUpdateTick != -1 && lastUpdateTick <= Find.TickManager.TicksGame) return;
            
            lastUpdateTick = Find.TickManager.TicksGame;
            enemyCache.Clear();

            var allFactions = Find.FactionManager.AllFactionsInViewOrder.ToList();
            foreach (var f in allFactions)
            {
                if (f.defeated || (f.Hidden && !showAll)) continue;

                var enemies = allFactions
                    .Where(other => other != f && other.HostileTo(f) && ((!other.IsPlayer && !other.Hidden) || showAll))
                    .ToList();
                
                enemyCache[f.loadID] = enemies;
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(Faction faction, float rowY, Rect fillRect, ref float __result)
        {
            if (MoreFactionsMod.settings != null && !MoreFactionsMod.settings.enableUIPatch) return true;
            try
            {
                bool showAll = ShowAllField?.GetValue(null) as bool? ?? false;
                float availableWidthForEnemies = fillRect.width - 300f - 40f - 70f - 54f - 16f - 120f;

                // ОБНОВЛЯЕМ КЕШ: вместо пересчета для каждой строки, считаем один раз на все окно
                RefreshEnemyCache(showAll);

                // Берём врагов из кеша (O(1) вместо O(N))
                if (!enemyCache.TryGetValue(faction.loadID, out List<Faction> enemiesList))
                {
                    enemiesList = new List<Faction>();
                }
                var enemies = enemiesList.ToArray();

                // Фоновая подложка для разделения строк
                Rect fullRowRect = new Rect(fillRect.x, rowY, fillRect.width, RowHeight);
                if (Mouse.IsOver(fullRowRect)) Widgets.DrawHighlight(fullRowRect);
                else Widgets.DrawLightHighlight(fullRowRect);

                // 1. Отрисовка названия и иконки фракции
                Rect iconRect = new Rect(15f, rowY + (RowHeight - 50f) / 2f, 50f, 50f);
                GUI.color = faction.Color;
                GUI.DrawTexture(iconRect, faction.def.FactionIcon);
                GUI.color = Color.white;

                Rect nameRect = new Rect(75f, rowY, 320f, RowHeight);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                // Цветовое оформление: Название (белый), Тип (серый), Лидер (сероватый)
                string label = $"{faction.Name.CapitalizeFirst().Colorize(Color.white)}\n" +
                               $"{faction.def.LabelCap.Resolve().Colorize(new Color(0.8f, 0.8f, 0.8f))}\n" +
                               (faction.leader != null ? $"{faction.LeaderTitle.CapitalizeFirst()}: {faction.leader.Name.ToStringShort}".Colorize(new Color(0.7f, 0.7f, 0.7f)) : "");
                
                Widgets.Label(nameRect, label);

                // Хотспот для инфокарточки
                Rect interactionRect = new Rect(0f, rowY, nameRect.xMax, RowHeight);
                if (Mouse.IsOver(interactionRect))
                {
                    TooltipHandler.TipRegion(interactionRect, new TipSignal(() => 
                        $"{faction.Name.Colorize(ColoredText.TipSectionTitleColor)}\n{faction.def.LabelCap.Resolve()}\n\n{faction.def.Description}", 
                        faction.loadID ^ 0x738AC053));
                    Widgets.DrawHighlight(interactionRect);
                }
                if (Widgets.ButtonInvisible(interactionRect))
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(faction));
                }

                // 2. Кнопка "i"
                Rect infoButtonRect = new Rect(nameRect.xMax, rowY, 40f, RowHeight);
                Widgets.InfoCardButtonCentered(infoButtonRect, faction);

                // 3. Идеология
                Rect ideologyRect = new Rect(infoButtonRect.xMax, rowY, 60f, RowHeight);
                if (ModsConfig.IdeologyActive && !Find.IdeoManager.classicMode && faction.ideos != null)
                {
                    DrawIdeologyIcons(ideologyRect, faction);
                }
                else
                {
                    ideologyRect.width = 0f;
                }

                // 4. Отношения и репутация
                Rect relationRect = new Rect(ideologyRect.xMax, rowY, 70f, RowHeight);
                if (!faction.IsPlayer)
                {
                    DrawRelationInfo(relationRect, faction);
                }

                // 5. Естественная репутация
                Rect naturalGoodwillRect = new Rect(relationRect.xMax, rowY, 54f, RowHeight);
                if (!faction.IsPlayer && faction.HasGoodwill && !faction.def.permanentEnemy)
                {
                    DrawNaturalGoodwill(naturalGoodwillRect, faction);
                }

                // 6. Отрисовка ВРАГОВ (компактная логика)
                DrawEnemyIcons(naturalGoodwillRect.xMax, rowY, availableWidthForEnemies, enemies);

                Text.Anchor = TextAnchor.UpperLeft;
                __result = RowHeight;
                return false; // Отменяем ванильный метод
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[MF] Error in DrawFactionRow patch: {ex}", 991234);
                return true; // В случае ошибки рисуем ваниль
            }
        }

        private static void DrawIdeologyIcons(Rect rect, Faction faction)
        {
            float curX = rect.x;
            float curY = rect.y;

            if (faction.ideos.PrimaryIdeo != null)
            {
                Rect primaryRect = new Rect(curX, curY + (rect.height - 40f) / 2f, 40f, 40f);
                IdeoUIUtility.DoIdeoIcon(primaryRect, faction.ideos.PrimaryIdeo, true, () => IdeoUIUtility.OpenIdeoInfo(faction.ideos.PrimaryIdeo));
                curX += primaryRect.width + 5f;
            }

            var minorIdeos = faction.ideos.IdeosMinorListForReading;
            for (int i = 0; i < minorIdeos.Count; i++)
            {
                if (curX + 22f > rect.xMax) break;
                Rect minorRect = new Rect(curX, curY + (rect.height - 22f) / 2f, 22f, 22f);
                IdeoUIUtility.DoIdeoIcon(minorRect, minorIdeos[i], true, () => IdeoUIUtility.OpenIdeoInfo(minorIdeos[i]));
                curX += minorRect.width + 5f;
            }
        }

        private static void DrawRelationInfo(Rect rect, Faction faction)
        {
            string statusLabel = faction.PlayerRelationKind.GetLabelCap();
            if (faction.defeated) statusLabel = statusLabel.Colorize(ColorLibrary.Grey);

            GUI.color = faction.PlayerRelationKind.GetColor();
            Text.Anchor = TextAnchor.MiddleCenter;

            if (faction.HasGoodwill && !faction.def.permanentEnemy)
            {
                Widgets.Label(new Rect(rect.x, rect.y - 10f, rect.width, rect.height), statusLabel);
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(rect.x, rect.y + 10f, rect.width, rect.height), faction.PlayerGoodwill.ToStringWithSign());
                Text.Font = GameFont.Small;
            }
            else
            {
                Widgets.Label(rect, statusLabel);
            }

            GenUI.ResetLabelAlign();
            GUI.color = Color.white;

            if (Mouse.IsOver(rect))
            {
                DrawRelationTooltip(rect, faction);
                Widgets.DrawHighlight(rect);
            }
        }

        private static void DrawRelationTooltip(Rect rect, Faction faction)
        {
            TaggedString tip = "";
            if (faction.def.permanentEnemy) tip = "CurrentGoodwillTip_PermanentEnemy".Translate();
            else if (faction.HasGoodwill)
            {
                tip = "Goodwill".Translate().Colorize(ColoredText.TipSectionTitleColor) + ": " + 
                      (faction.PlayerGoodwill.ToStringWithSign() + ", " + faction.PlayerRelationKind.GetLabel()).Colorize(faction.PlayerRelationKind.GetColor());
                
                string ongoing = GetOngoingEventsMI?.Invoke(null, new object[] { faction })?.ToString();
                if (!ongoing.NullOrEmpty()) tip += "\n\n" + "OngoingEvents".Translate().Colorize(ColoredText.TipSectionTitleColor) + ":\n" + ongoing;

                string recent = GetRecentEventsMI?.Invoke(null, new object[] { faction })?.ToString();
                if (!recent.NullOrEmpty()) tip += "\n\n" + "RecentEvents".Translate().Colorize(ColoredText.TipSectionTitleColor) + ":\n" + recent;
            }
            if (tip != "") TooltipHandler.TipRegion(rect, tip);
        }

        private static void DrawNaturalGoodwill(Rect rect, Faction faction)
        {
            var natGoodwill = faction.NaturalGoodwill;
            object kindObj = GetRelationKindForGoodwillMI?.Invoke(null, new object[] { natGoodwill });
            FactionRelationKind kind = (kindObj is FactionRelationKind frk) ? frk : FactionRelationKind.Neutral;
            GUI.color = kind.GetColor();
            Rect barRect = rect.ContractedBy(7f);
            barRect.y = rect.y + 30f;
            barRect.height = 20f;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.DrawRectFast(barRect, Color.black);
            Widgets.Label(barRect, natGoodwill.ToStringWithSign());
            GenUI.ResetLabelAlign();
            GUI.color = Color.white;

            if (Mouse.IsOver(rect))
            {
                TaggedString tip = "NaturalGoodwill".Translate().Colorize(ColoredText.TipSectionTitleColor) + ": " + natGoodwill.ToStringWithSign().Colorize(kind.GetColor());
                string explanation = GetNaturalGoodwillExplanationMI?.Invoke(null, new object[] { faction })?.ToString();
                if (!explanation.NullOrEmpty()) tip += "\n\n" + "AffectedBy".Translate().Colorize(ColoredText.TipSectionTitleColor) + "\n" + explanation;
                TooltipHandler.TipRegion(rect, tip);
                Widgets.DrawHighlight(rect);
            }
        }

        private static void DrawEnemyIcons(float startX, float rowY, float maxWidth, Faction[] enemies)
        {
            if (enemies.Length == 0) return;

            float iconSize = BaseIconSize;
            float stepY = BaseStepY;
            float usableWidth = Mathf.Max(iconSize, maxWidth - FirstOffsetX - 2f);
            int iconsPerRow = Mathf.Max(1, Mathf.FloorToInt((usableWidth + IconGapX) / (iconSize + IconGapX)));
            int rowsNeeded = (enemies.Length + iconsPerRow - 1) / iconsPerRow;

            // Если рядов слишком много — масштабируем иконки, чтобы влезли в высоту 80px
            if (rowsNeeded > MaxRowsWithoutScaling)
            {
                float scale = 78f / (iconSize + (rowsNeeded - 1) * stepY);
                scale = Mathf.Clamp(scale, 0.5f, 1f);
                iconSize *= scale;
                stepY *= scale;
                iconsPerRow = Mathf.Max(1, Mathf.FloorToInt((usableWidth - iconSize) / (iconSize + IconGapX)));
                rowsNeeded = (enemies.Length + iconsPerRow - 1) / iconsPerRow;
            }

            float totalHeight = (rowsNeeded == 0) ? 0f : (iconSize + (rowsNeeded - 1) * stepY);
            float startY = rowY + Mathf.Max(1f, (RowHeight - totalHeight) / 2f);

            int curRow = 0;
            int curCol = 0;

            for (int i = 0; i < enemies.Length; i++)
            {
                float x = startX + FirstOffsetX + curCol * (iconSize + IconGapX);
                float y = startY + curRow * stepY;

                DrawFactionIconWithTooltipMI?.Invoke(null, new object[] { new Rect(x, y, iconSize, iconSize), enemies[i] });

                curCol++;
                if (curCol >= iconsPerRow)
                {
                    curCol = 0;
                    curRow++;
                }
            }
        }
    }
}
