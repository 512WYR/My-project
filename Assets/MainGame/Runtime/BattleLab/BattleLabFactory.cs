using System;
using System.Collections.Generic;
using System.Linq;
using Ruin.Core;
using UnityEngine;

namespace Ruin.Runtime
{
    public enum BattleLabPreset
    {
        Dummy,          // One invulnerable-ish training dummy at rank 1.
        EmptyEnemies,   // No enemy, so the battle settles as an immediate victory.
        EmptyHeroes,    // No hero, so the battle settles as an immediate defeat.
        Normal,
        Elite,
        Boss,
        AllMonsters,
        FullEquipment   // Every accessory, consumable and a fitted prosthetic on every enabled hero.
    }

    // Builds ActorSpecs and a RunState from a BattleLabConfig. The catalog is read-only: every spec handed
    // to the engine is a JSON deep copy, because SkillSpec instances inside CharacterDefinition are asset
    // references and editing them in place would corrupt the catalog for every later run.
    public static class BattleLabFactory
    {
        // ---- cloning ----

        public static ActorSpec Clone(ActorSpec source)
        {
            if (source == null) return null;
            return CloneJson(source);
        }

        public static SkillSpec Clone(SkillSpec source)
        {
            if (source == null) return null;
            var copy = CloneJson(source);
            copy.Id = copy.Id ?? "";
            copy.Name = copy.Name ?? "";
            copy.Description = copy.Description ?? "";
            if (copy.Rider != null && copy.Rider.Kind == RiderKind.None) copy.Rider = null;
            return copy;
        }

        public static CombatRules CloneRules(CombatRules source)
        {
            var copy = source == null ? new CombatRules() : CloneJson(source);
            BattleLabConfig.NormalizeRules(copy);
            return copy;
        }

        private static T CloneJson<T>(T source) where T : class
        {
            string json = JsonUtility.ToJson(source);
            var copy = JsonUtility.FromJson<T>(json);
            if (copy == null) throw new InvalidOperationException("无法克隆 " + typeof(T).Name + "。");
            return copy;
        }

        // ---- catalog lookup ----

        public static CharacterDefinition FindHero(GameCatalog catalog, string id)
        {
            if (catalog == null || catalog.Heroes == null || string.IsNullOrEmpty(id)) return null;
            return catalog.Heroes.FirstOrDefault(x => x && x.Id == id);
        }

        public static CharacterDefinition FindEnemy(GameCatalog catalog, string id)
        {
            if (catalog == null || string.IsNullOrEmpty(id)) return null;
            var fromMonsters = catalog.Monsters == null ? null : catalog.Monsters.FirstOrDefault(x => x && x.Id == id);
            if (fromMonsters) return fromMonsters;
            return catalog.Enemies == null ? null : catalog.Enemies.FirstOrDefault(x => x && x.Id == id);
        }

        // Monsters first: they are the playable enemy roster. Legacy dummies stay available at the end.
        public static List<CharacterDefinition> EnemySources(GameCatalog catalog)
        {
            var result = new List<CharacterDefinition>();
            if (catalog == null) return result;
            AddSources(result, catalog.Monsters);
            AddSources(result, catalog.Enemies);
            return result;
        }

        private static void AddSources(List<CharacterDefinition> target, CharacterDefinition[] source)
        {
            if (source == null) return;
            foreach (var item in source)
                if (item && !target.Any(x => x.Id == item.Id))
                    target.Add(item);
        }

        // ---- defaults and source selection ----

        public static BattleLabConfig CreateDefault(GameCatalog catalog)
        {
            var cfg = new BattleLabConfig();
            if (catalog != null)
                cfg.Rules = CloneRules(catalog.Rules());
            cfg.Normalize();

            if (catalog != null && catalog.Heroes != null)
            {
                for (int i = 0; i < BattleLabConfig.SlotCount; i++)
                {
                    var slot = cfg.Heroes[i];
                    var source = i < catalog.Heroes.Length ? catalog.Heroes[i] : null;
                    if (source)
                    {
                        ApplyHeroSource(slot, source);
                        slot.Enabled = true;
                    }
                    else
                    {
                        slot.Enabled = false;
                    }
                }
            }

            var enemies = EnemySources(catalog);
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                var slot = cfg.Enemies[i];
                bool enabled = i < 2 && i < enemies.Count;
                slot.Enabled = enabled;
                if (!enabled) continue;
                ApplyEnemySource(slot, enemies[i]);
                slot.StartRank = i;
            }

            cfg.Normalize();
            return cfg;
        }

        public static void ApplyHeroSource(BattleLabHeroConfig cfg, CharacterDefinition def)
        {
            if (cfg == null || !def) return;
            cfg.SourceId = def.Id;
            cfg.Spec = Clone(def.ToSpec());
            cfg.Spec.Id = string.IsNullOrEmpty(def.Id) ? "hero" : def.Id;
        }

        public static void ApplyEnemySource(BattleLabEnemyConfig cfg, CharacterDefinition def)
        {
            if (cfg == null || !def) return;
            cfg.SourceId = def.Id;
            cfg.Spec = Clone(def.ToSpec());
            cfg.Spec.StartRank = -1;
        }

        public static ActorSpec DummySpec()
        {
            return new ActorSpec
            {
                Id = "lab_dummy", Name = "木桩", Subtitle = "不会行动的训练靶",
                MaxHp = 200, Speed = 1, Attack = 0, Accuracy = 0, CritChance = 0, Dodge = 0, Defense = 0,
                Pattern = EnemyPattern.Cleaver, Behavior = EnemyBehavior.Simple,
                RankWidth = 1, Immobile = true, ActionInterval = 1
            };
        }

        public static void ApplyPreset(BattleLabConfig cfg, GameCatalog catalog, BattleLabPreset preset)
        {
            if (cfg == null) return;
            cfg.Normalize();
            switch (preset)
            {
                case BattleLabPreset.Dummy:
                    DisableAllEnemies(cfg);
                    cfg.Enemies[0].Enabled = true;
                    cfg.Enemies[0].SourceId = "lab_dummy";
                    cfg.Enemies[0].Spec = DummySpec();
                    cfg.Enemies[0].StartRank = 0;
                    break;
                case BattleLabPreset.EmptyEnemies:
                    DisableAllEnemies(cfg);
                    break;
                case BattleLabPreset.EmptyHeroes:
                    foreach (var hero in cfg.Heroes) hero.Enabled = false;
                    break;
                case BattleLabPreset.Normal:
                    ApplyLineup(cfg, FirstLineup(catalog, 0));
                    break;
                case BattleLabPreset.Elite:
                    ApplyLineup(cfg, FirstLineup(catalog, 1));
                    break;
                case BattleLabPreset.Boss:
                    ApplyLineup(cfg, FirstLineup(catalog, 2));
                    break;
                case BattleLabPreset.AllMonsters:
                    ApplyAllMonsters(cfg, catalog);
                    break;
                case BattleLabPreset.FullEquipment:
                    ApplyFullEquipment(cfg);
                    break;
            }
            cfg.Normalize();
        }

        private static void DisableAllEnemies(BattleLabConfig cfg)
        {
            foreach (var enemy in cfg.Enemies)
            {
                enemy.Enabled = false;
                enemy.StartRank = -1;
            }
        }

        private static EnemyLineup FirstLineup(GameCatalog catalog, int kind)
        {
            if (catalog == null) return null;
            EnemyLineup[] table = kind == 1 ? catalog.EliteEncounters : kind == 2 ? catalog.BossEncounters : catalog.NormalEncounters;
            return table == null ? null : table.FirstOrDefault(x => x != null && x.Slots != null && x.Slots.Any(s => s));
        }

        private static void ApplyLineup(BattleLabConfig cfg, EnemyLineup lineup)
        {
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                var slot = cfg.Enemies[i];
                var source = lineup != null && lineup.Slots != null && i < lineup.Slots.Length ? lineup.Slots[i] : null;
                slot.Enabled = source;
                slot.StartRank = source ? i : -1;
                if (source) ApplyEnemySource(slot, source);
            }
        }

        private static void ApplyAllMonsters(BattleLabConfig cfg, GameCatalog catalog)
        {
            DisableAllEnemies(cfg);
            if (catalog == null || catalog.Monsters == null) return;
            int rank = 0;
            int slot = 0;
            foreach (var monster in catalog.Monsters)
            {
                if (!monster) continue;
                if (slot >= BattleLabConfig.SlotCount || rank >= 4) break;
                var spec = monster.ToSpec();
                int width = Math.Max(1, Math.Min(4, spec.Width));
                if (rank + width > 4) continue;
                var target = cfg.Enemies[slot];
                target.Enabled = true;
                ApplyEnemySource(target, monster);
                target.StartRank = rank;
                rank += width;
                slot++;
            }
        }

        private static void ApplyFullEquipment(BattleLabConfig cfg)
        {
            bool any = false;
            foreach (var hero in cfg.Heroes)
            {
                if (!hero.Enabled) continue;
                any = true;
                hero.ForceAccessories = true;
                hero.AccessoryIds = AccessorySystem.Defaults.Where(a => a != null).Select(a => a.Id).ToList();
                hero.ConsumableCounts = Enumerable.Repeat(1, ConsumableSystem.Defaults.Length).ToArray();
                hero.Limbs = new[] { LabLimbState.Intact, LabLimbState.Intact, LabLimbState.Severed, LabLimbState.Severed };
                hero.ProstheticIds = new[] { "", "", "prosthetic_arm", "prosthetic_leg" };
                hero.Sanity = 100;
            }
            // The preset is still useful with an empty team; turn one slot on so the button always does
            // something visible.
            if (!any)
            {
                cfg.Heroes[0].Enabled = true;
                cfg.Heroes[0].ForceAccessories = true;
                cfg.Heroes[0].AccessoryIds = AccessorySystem.Defaults.Where(a => a != null).Select(a => a.Id).ToList();
            }
        }

        // ---- validation ----

        public static List<string> Validate(BattleLabConfig cfg, GameCatalog catalog)
        {
            var errors = new List<string>();
            if (cfg == null) { errors.Add("配置为空。"); return errors; }
            cfg.Normalize();
            if (catalog == null) errors.Add("找不到 GameCatalog，无法解析角色。");
            if (!cfg.TryGetSeed(out _, out string seedError)) errors.Add(seedError);

            ValidateHeroes(cfg, errors);
            ValidateEnemies(cfg, errors);

            var rules = ResolveRules(cfg, catalog);
            ValidateHeroEquipment(cfg, rules, errors);
            return errors;
        }

        private static void ValidateHeroes(BattleLabConfig cfg, List<string> errors)
        {
            var explicitRanks = new HashSet<int>();
            foreach (var hero in cfg.Heroes)
            {
                if (!hero.Enabled) continue;
                string name = hero.Spec == null || string.IsNullOrEmpty(hero.Spec.Name) ? "英雄槽" : hero.Spec.Name;
                if (hero.Spec == null) { errors.Add(name + " 没有角色定义。"); continue; }
                if (hero.Spec.MaxHp <= 0) errors.Add(name + " 的最大生命必须大于 0。");
                if (hero.Spec.Width != 1) errors.Add(name + " 的 RankWidth 必须为 1（英雄不支持多站位）。");
                if (hero.Rank >= 0)
                {
                    if (hero.Rank > 3) errors.Add(name + " 的指定站位超出 1～4 号位。");
                    else if (!explicitRanks.Add(hero.Rank)) errors.Add(name + " 与其他英雄指定了同一站位。");
                }
                if (hero.Spec.HasInvalidSkillEnums())
                    errors.Add(name + " 有技能使用了非法的枚举值（Effect/Target/Targeting/Area）。");
            }
            // Auto ranks are resolved after explicit ones; more than four enabled heroes could not fit, but
            // the fixed four slots make that impossible today. Still check for completeness.
            var used = new HashSet<int>(explicitRanks);
            int auto = 0;
            foreach (var hero in cfg.Heroes.Where(h => h.Enabled && h.Rank < 0))
            {
                while (auto < 4 && used.Contains(auto)) auto++;
                if (auto >= 4) { errors.Add("启用英雄的站位超过 4 个。"); break; }
                used.Add(auto);
            }
        }

        private static void ValidateEnemies(BattleLabConfig cfg, List<string> errors)
        {
            var occupied = new bool[4];
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                var enemy = cfg.Enemies[i];
                if (!enemy.Enabled) continue;
                if (enemy.Spec == null) { errors.Add("敌人槽 " + (i + 1) + " 没有角色定义。"); continue; }
                string name = string.IsNullOrEmpty(enemy.Spec.Name) ? "敌人槽 " + (i + 1) : enemy.Spec.Name;
                int start = enemy.StartRank >= 0 ? enemy.StartRank : i;
                int width = enemy.Spec.Width;
                if (start < 0 || start >= 4) { errors.Add(name + " 的起始站位必须在 1～4 号位。"); continue; }
                if (start + width > 4) { errors.Add(name + " 占用 " + width + " 个站位，超出 4 号位。"); continue; }
                for (int rank = start; rank < start + width; rank++)
                {
                    if (occupied[rank]) { errors.Add(name + " 与另一名敌人在 " + (rank + 1) + " 号位重叠。"); break; }
                    occupied[rank] = true;
                }
                if (!Enum.IsDefined(typeof(EnemyBehavior), enemy.Spec.Behavior))
                    errors.Add(name + " 的 Behavior 枚举值无效。");
                if (!Enum.IsDefined(typeof(EnemyPattern), enemy.Spec.Pattern))
                    errors.Add(name + " 的 Pattern 枚举值无效。");
                if (enemy.Spec.HasInvalidSkillEnums())
                    errors.Add(name + " 有技能使用了非法的枚举值（Effect/Target/Targeting/Area）。");
            }
        }

        private static void ValidateHeroEquipment(BattleLabConfig cfg, CombatRules rules, List<string> errors)
        {
            foreach (var hero in cfg.Heroes)
            {
                if (!hero.Enabled || hero.Spec == null) continue;
                string name = string.IsNullOrEmpty(hero.Spec.Name) ? "英雄" : hero.Spec.Name;
                var seen = new HashSet<string>();
                foreach (var id in hero.AccessoryIds)
                {
                    var item = AccessorySystem.Find(id);
                    if (item == null) { errors.Add(name + " 的饰品 " + id + " 不存在。"); continue; }
                    if (!seen.Add(id)) errors.Add(name + " 重复配置了「" + item.Name + "」。");
                    if (!hero.ForceAccessories)
                    {
                        if (!string.IsNullOrEmpty(item.RestrictSilhouette) && item.RestrictSilhouette != hero.Spec.Silhouette)
                            errors.Add(name + " 不能装备「" + item.Name + "」（" + AccessorySystem.RestrictionText(item) + "）。");
                    }
                }
                for (int part = 0; part < 4; part++)
                {
                    string prostheticId = hero.ProstheticIds[part];
                    if (string.IsNullOrEmpty(prostheticId)) continue;
                    var prosthetic = LimbSystem.Prosthetic(prostheticId, rules == null ? null : rules.Limbs);
                    if (prosthetic == null) { errors.Add(name + " 的义肢 " + prostheticId + " 不存在。"); continue; }
                    if (prosthetic.Part != (LimbPart)part)
                        errors.Add(name + " 的「" + prosthetic.Name + "」只能装在" + LimbSystem.PartName(prosthetic.Part) + "。");
                    if (hero.Limbs[part] != LabLimbState.Severed)
                        errors.Add(name + " 的" + LimbSystem.PartName((LimbPart)part) + "未断肢，不能安装「" + prosthetic.Name + "」。");
                }
            }
        }

        // ---- rules and run construction ----

        public static CombatRules ResolveRules(BattleLabConfig cfg, GameCatalog catalog)
        {
            CombatRules rules;
            if (cfg == null) rules = new CombatRules();
            else if (cfg.UseCatalogRules && catalog != null)
                rules = CloneRules(catalog.Rules());
            else
                rules = CloneRules(cfg.Rules);
            BattleLabConfig.NormalizeRules(rules);
            return rules;
        }

        public static bool TryBuildRun(BattleLabConfig cfg, GameCatalog catalog, out RunState run, out List<string> errors)
        {
            run = null;
            errors = Validate(cfg, catalog);
            if (errors.Count > 0) return false;
            if (!cfg.TryGetSeed(out uint seed, out string seedError))
            {
                errors.Add(seedError);
                return false;
            }

            var rules = ResolveRules(cfg, catalog);
            var heroSpecs = BuildHeroSpecs(cfg);
            run = RunState.Create(heroSpecs.ToArray(), seed, cfg.PlayerSanity, rules);
            run.PlayerSanity = SanityProbability.Clamp(cfg.PlayerSanity, 0, 100);

            ApplyHeroRanks(run, cfg);
            ApplyHeroSetup(run, cfg, rules, errors);
            if (errors.Count > 0)
            {
                run = null;
                return false;
            }
            return true;
        }

        public static List<ActorSpec> BuildHeroSpecs(BattleLabConfig cfg)
        {
            var result = new List<ActorSpec>();
            var usedIds = new HashSet<string>();
            foreach (var slot in cfg.Heroes)
            {
                if (!slot.Enabled) continue;
                var spec = Clone(slot.Spec);
                if (spec == null) continue;
                string baseId = string.IsNullOrEmpty(spec.Id) ? "hero_" + result.Count : spec.Id;
                string candidate = baseId;
                int suffix = 2;
                while (usedIds.Contains(candidate)) candidate = baseId + "_" + suffix++;
                spec.Id = candidate;
                if (usedIds.Add(candidate)) result.Add(spec);
            }
            return result;
        }

        public static List<ActorSpec> BuildEnemySpecs(BattleLabConfig cfg, out List<string> errors)
        {
            errors = new List<string>();
            var result = new List<ActorSpec>();
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                var slot = cfg.Enemies[i];
                if (!slot.Enabled) continue;
                var spec = Clone(slot.Spec);
                if (spec == null) continue;
                spec.StartRank = slot.StartRank >= 0 ? slot.StartRank : i;
                if (spec.StartRank < 0 || spec.StartRank >= 4) { errors.Add(spec.Name + " 的起始站位非法。"); continue; }
                if (spec.StartRank + spec.Width > 4) { errors.Add(spec.Name + " 的站位超出 4 号位。"); continue; }
                result.Add(spec);
            }
            return result;
        }

        private static void ApplyHeroRanks(RunState run, BattleLabConfig cfg)
        {
            var ranks = ComputeHeroRanks(cfg);
            int heroIndex = 0;
            for (int i = 0; i < BattleLabConfig.SlotCount; i++)
            {
                if (!cfg.Heroes[i].Enabled) continue;
                if (heroIndex >= run.Heroes.Count || heroIndex >= ranks.Length) break;
                run.Heroes[heroIndex].Rank = ranks[heroIndex];
                heroIndex++;
            }
        }

        private static int[] ComputeHeroRanks(BattleLabConfig cfg)
        {
            var enabled = cfg.Heroes.Where(h => h.Enabled).ToList();
            var ranks = new int[enabled.Count];
            var used = new HashSet<int>();
            for (int i = 0; i < enabled.Count; i++)
            {
                int rank = enabled[i].Rank;
                ranks[i] = rank >= 0 && rank < 4 && used.Add(rank) ? rank : -1;
            }
            int next = 0;
            for (int i = 0; i < ranks.Length; i++)
            {
                if (ranks[i] >= 0) continue;
                while (next < 4 && used.Contains(next)) next++;
                if (next >= 4) { ranks[i] = 0; continue; }
                ranks[i] = next;
                used.Add(next);
            }
            return ranks;
        }

        // Applies the out-of-battle setup that RunState.Create cannot express: per-hero sanity, limbs,
        // prosthetics, accessories and consumable stock.
        public static void ApplyHeroSetup(RunState run, BattleLabConfig cfg, CombatRules rules, List<string> errors)
        {
            if (run == null || cfg == null) return;
            int index = 0;
            for (int slot = 0; slot < BattleLabConfig.SlotCount; slot++)
            {
                var heroCfg = cfg.Heroes[slot];
                if (!heroCfg.Enabled) continue;
                if (index >= run.Heroes.Count) break;
                var hero = run.Heroes[index++];
                if (hero == null || hero.Definition == null) continue;

                hero.Sanity = SanityProbability.Clamp(heroCfg.Sanity, 0, 100);
                hero.SanitySkillsOnly = heroCfg.SanitySkillsOnly;
                hero.SanityLockPending = heroCfg.SanityLockPending;

                ApplyLimbs(hero, heroCfg, rules, errors);
                ApplyAccessories(run, hero, heroCfg, errors);
                AddConsumables(run, heroCfg);

                if (cfg.FullHealOnStart) hero.Hp = hero.Definition.MaxHp;
            }
        }

        private static void ApplyLimbs(ActorState hero, BattleLabHeroConfig cfg, CombatRules rules, List<string> errors)
        {
            for (int part = 0; part < 4; part++)
            {
                var limb = LimbSystem.Get(hero, (LimbPart)part);
                if (limb == null) continue;
                switch (cfg.Limbs[part])
                {
                    case LabLimbState.Severed:
                        limb.Hp = 0;
                        limb.Prosthetic = null;
                        break;
                    case LabLimbState.Injured:
                        limb.Hp = Math.Max(1, limb.MaxHp / 2);
                        limb.Prosthetic = null;
                        break;
                    default:
                        limb.Hp = limb.MaxHp;
                        limb.Prosthetic = null;
                        break;
                }
                string prostheticId = cfg.ProstheticIds[part];
                if (string.IsNullOrEmpty(prostheticId)) continue;
                if (cfg.Limbs[part] != LabLimbState.Severed)
                {
                    errors.Add(hero.Name + " 的" + LimbSystem.PartName((LimbPart)part) + "未断肢，无法安装义肢。");
                    continue;
                }
                var item = LimbSystem.Prosthetic(prostheticId, rules == null ? null : rules.Limbs);
                if (item == null)
                {
                    errors.Add(hero.Name + " 的义肢 " + prostheticId + " 不存在。");
                    continue;
                }
                if (!LimbSystem.Install(hero, item))
                    errors.Add(hero.Name + " 无法安装「" + item.Name + "」。");
            }
        }

        private static void ApplyAccessories(RunState run, ActorState hero, BattleLabHeroConfig cfg, List<string> errors)
        {
            foreach (var id in cfg.AccessoryIds)
            {
                var item = AccessorySystem.Find(id);
                if (item == null)
                {
                    errors.Add(hero.Name + " 的饰品 " + id + " 不存在。");
                    continue;
                }
                if (cfg.ForceAccessories)
                {
                    AccessorySystem.Attach(hero, item);
                    continue;
                }
                run.AccessoryStock.Add(AccessorySystem.Clone(item));
                if (!AccessorySystem.Equip(run, hero, run.AccessoryStock.Count - 1, out string note))
                    errors.Add(note);
            }
        }

        private static void AddConsumables(RunState run, BattleLabHeroConfig cfg)
        {
            for (int i = 0; i < ConsumableSystem.Defaults.Length && i < cfg.ConsumableCounts.Length; i++)
            {
                int count = Math.Max(0, cfg.ConsumableCounts[i]);
                for (int n = 0; n < count; n++)
                    run.Consumables.Add(ConsumableSystem.Clone(ConsumableSystem.Defaults[i]));
            }
        }

        // A compact, display-friendly list of every config problem. The UI shows the first few; the build
        // path refuses on any entry.
        public static string DescribeErrors(List<string> errors, int limit = 6)
        {
            if (errors == null || errors.Count == 0) return "配置通过校验。";
            var lines = errors.Take(Math.Max(1, limit)).ToList();
            string text = string.Join("\n", lines);
            if (errors.Count > lines.Count) text += "\n…还有 " + (errors.Count - lines.Count) + " 条。";
            return text;
        }

        // A cheap sanity check used by the config page: every skill must have a valid effect and targeting
        // so an invalid enum from imported JSON cannot throw inside the engine.
        private static bool HasInvalidSkillEnums(this ActorSpec spec)
        {
            if (spec == null) return false;
            return AllSkills(spec).Any(s => s == null
                || !Enum.IsDefined(typeof(SkillEffect), s.Effect)
                || !Enum.IsDefined(typeof(TargetSide), s.Target)
                || !Enum.IsDefined(typeof(SkillTargeting), s.Targeting)
                || !Enum.IsDefined(typeof(SkillArea), s.Area));
        }

        private static IEnumerable<SkillSpec> AllSkills(ActorSpec spec)
        {
            return (spec.BasicSkills ?? ActorSpec.EmptySkills)
                .Concat(spec.PrimalSkills ?? ActorSpec.EmptySkills)
                .Concat(spec.TechnicalSkills ?? ActorSpec.EmptySkills);
        }
    }
}
