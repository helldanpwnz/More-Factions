# Gemini AI Guidelines for RimWorld Modding

This document provides strict instructions for AI agents working on this RimWorld mod repository. Read and apply these rules before generating any code.

[CRITICAL] МИНИМАЛЬНЫЕ ПРАВКИ: Запрещено переписывать блоки кода более 1-3 строк, если задачу можно решить изменением одной строки или добавлением точечного условия. Любая попытка рефакторинга, переименования переменных или изменения структуры метода ради "чистоты кода" без прямой просьбы пользователя — считается грубым нарушением. Изменяй только то, что непосредственно ломает логику или необходимо для новой фичи.

[CRITICAL] NO UNSOLICITED REFACTORING: Никогда не меняй константы, таймеры (Tick), шансы (Rand.Value) или структуру методов, если это не является ПРЯМОЙ темой запроса. Даже если код выглядит как «временная заглушка» или «неэффективный тест», ИГНОРИРУЙ это. Любое изменение кода за пределами конкретной исправляемой ошибки или запрашиваемой фичи считается ГРУБОЙ ОШИБКОЙ.

## 1. Core Directives
* **Strict Obedience:** Answer exactly what is asked. Do not add unsolicited features, extra mechanics, or "bonus" logic unless explicitly requested.
* **No Premature Optimization:** Do not optimize existing code unless asked. Write clean, readable code first. Only optimize if there is a known performance bottleneck.
* **Simplicity First:** Always choose the simplest, most direct solution. Avoid over-engineering. Do not build complex custom frameworks if a vanilla RimWorld approach (or a simple class extension) can achieve the same result.

ALL final code changes MUST be applied directly to the files using your internal tools. Never output code blocks in chat unless I explicitly ask for a preview or explanation.

## 2. RimWorld Specifics & Performance
* **Target Version:** RimWorld 1.6. (Use the RimWorld 1.5 API as your baseline, but ensure all code is strictly forward-compatible and avoids deprecated methods).
* **Zero TPS Impact:** Code must have minimal to zero impact on game performance (Ticks Per Second).
* **Tick Management:** Never use `Tick()` if `TickRare()` (every 250 ticks) or `TickLong()` (every 2000 ticks) is sufficient. 
* **Background Simulations:** When managing off-map entities, persistent data, or global systems (like custom settlements, deep character tracking, or lineage/dynasty systems), cache results heavily. 
* **Save Bloat:** Keep `ExposeData` clean. Only save the absolute minimum data required to reconstruct the state. Use `Scribe_References` for existing pawns/factions and `Scribe_Values` for simple types.

## 3. Mod Compatibility & Harmony
* **Light Touch:** Rely on XML patching and standard OOP inheritance (`WorldComponent`, `GameComponent`, `MapComponent`) before reaching for C# Harmony patches.
* **Harmony Best Practices:** * Use `Prefix` for cleanly aborting or overriding vanilla logic.
    * Use `Postfix` for adding non-intrusive logic.
    * **AVOID `Transpilers`** unless explicitly instructed, as they are fragile and heavily hurt compatibility with other mods.

## 4. Output Rules
* Provide only the code that needs to be changed or added. Do not output entire unmodified files just to change one line.
* Keep explanations brief and technical.