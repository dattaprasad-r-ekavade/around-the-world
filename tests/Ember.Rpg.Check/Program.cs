using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Ember.Rpg;

namespace Ember.Rpg.Check;

/// <summary>
/// Save, load, and compare against the original. Exits 0 only when they are equal — so it
/// can sit in a build step the day someone changes the format.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var original = Original();
        var problems = new List<string>();
        var path = Path.Combine(Path.GetTempPath(), $"ember-rpg-check-{Guid.NewGuid():N}.json");

        try
        {
            PlayDialogue(original, problems);
            QuestRoundTrip(original, path, problems);
            ContentValidationChecks(problems);
            QuestObjectiveAuthoringChecks(problems);
            ActorStatFormulaChecks(problems);
            SkillUseProgressionChecks(problems);
            ModifierRuleChecks(problems);
            ContainerPersistenceChecks(problems);
            InventoryTransferChecks(problems);
            EquipmentChecks(problems);
            MeleeAndActorPersistenceChecks(problems);
            EnemyCombatAiChecks(problems);
            WorldClockAndScheduleChecks(problems);
            NpcScheduleSaveChecks(problems);
            SpellAndFactionChecks(problems);
            QuestEventChecks(problems);
            MerchantTradeChecks(problems);
            TheftChecks(problems);

            original.Write(path);
            var loaded = SaveState.Read(path);

            Compare(original, loaded, problems);

            if (loaded.Flags.GetBool("met_elder") != true)
                problems.Add("GetBool(met_elder) should be true after a load");
            if (loaded.Flags.GetNumber("gold") != 120d)
                problems.Add("GetNumber(gold) should be 120 after a load");
            if (loaded.Flags.GetText("place") != "oak_hall")
                problems.Add("GetText(place) should be 'oak_hall' after a load");
            if (loaded.Entities[0].Field("name") != "Rowan")
                problems.Add("elder_01 field 'name' should be 'Rowan' after a load");

            // The inventory test: an item was added before the save; it must still be there,
            // still stackable, still named, and still worn after the load.
            if (loaded.Player.Bag.Count("potion_heal") != 3)
                problems.Add($"bag should hold 3 potion_heal after a load, holds {loaded.Player.Bag.Count("potion_heal")}");
            if (loaded.Player.Bag.Count("sword_iron") != 0)
                problems.Add("the equipped sword should no longer also be in the bag after a load");
            if (loaded.Player.Equip.Get("mainhand") != "sword_iron")
                problems.Add("mainhand should still hold sword_iron after a load");
            if (loaded.ItemDefs.Get("potion_heal") is not { Name: "Healing Potion", Stackable: true, Slot: null })
                problems.Add("potion_heal definition should survive the load unchanged");

            // The dialogue test, after the round trip: still on the node the pick advanced
            // to, and the flag the pick wrote is still set.
            if (loaded.Dialogue.Tree?.Value != "elder")
                problems.Add($"dialogue should still be in the 'elder' tree after a load, is '{loaded.Dialogue.Tree}'");
            if (!loaded.Dialogue.TakenChoiceIds.Contains("elder:greet:1"))
                problems.Add("the executed dialogue choice should remain recorded after a load");
            if (loaded.Dialogue.Node != "gate")
                problems.Add($"dialogue should still be at 'gate' after a load, is '{loaded.Dialogue.Node}'");
            if (!loaded.Flags.GetBool("gate_topic"))
                problems.Add("gate_topic should still be set after a load");
            if (loaded.Player.Stats.Attributes.Strength != 10 || loaded.Player.Stats.Skills.GetValueOrDefault("Blade") != 34)
                problems.Add("player base stats and skills should survive a load");
            if (loaded.Player.Modifiers.Active.Count != 2 || loaded.Player.EffectiveStats.MaximumHealth != 31)
                problems.Add("stacked timed modifiers and their derived health should survive a load");
            if (EquipmentSystem.EffectiveStats(loaded.Player, loaded.ItemDefs).MaximumHealth != 34
                || EquipmentSystem.Attachments(loaded.Player, loaded.ItemDefs) is not { Count: 1 } attachments
                || attachments[0] != new EquippedBoneAttachment("mainhand",
                    new ContentId<ItemContentKind>("sword_iron"), "RightHand"))
                problems.Add("equipped stat bonuses and the bone attachment reference should survive a save/load");

            if (problems.Count == 0)
            {
                Console.WriteLine("[OK] save then load equals original");
                return 0;
            }

            foreach (var problem in problems) Console.Error.WriteLine("  " + problem);
            Console.Error.WriteLine("[FAIL] save then load does not equal the original");
            return 1;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void ContentValidationChecks(List<string> problems)
    {
        var loaded = RpgContentJson.FromJson(ValidContentJson);
        if (loaded.Actors.Get(new ContentId<ActorContentKind>("actor.elder")) is null
            || loaded.Items.Get(new ContentId<ItemContentKind>("item.potion")) is null
            || loaded.Factions.Get(new ContentId<FactionContentKind>("faction.mages")) is null
            || !loaded.Quests.TryGet(new ContentId<QuestContentKind>("quest.relic"), out _)
            || !loaded.Spells.TryGet(new ContentId<SpellContentKind>("spell.focus"), out _)
            || !loaded.SkillProgression.TryGet("combat.melee.hit", out _)
            || loaded.Dialogues.Count != 1)
            problems.Add("the content pack should register typed actor, item, faction, dialogue, and quest IDs");

        var save = new SaveState
        {
            Entities = new[]
            {
                EntityRecord.Create("elder_instance", "npc") with
                {
                    ActorId = new ContentId<ActorContentKind>("actor.elder")
                }
            },
            ItemDefs = loaded.Items,
            Dialogue = new DialogueProgress { Tree = new ContentId<DialogueContentKind>("dialogue.elder") }
        };
        save.Player.Bag.Add(loaded.Items.Get(new ContentId<ItemContentKind>("item.potion"))!);
        save.Player.Equip.Set("quick", "item.potion");
        if (loaded.ValidateSaveReferences(save).Count != 0)
            problems.Add("registered actor, inventory, equipment, and dialogue references should validate in a save");
        var missingActorSave = save with
        {
            Entities = new[]
            {
                EntityRecord.Create("elder_instance", "npc") with
                {
                    ActorId = new ContentId<ActorContentKind>("actor.missing")
                }
            }
        };
        if (loaded.ValidateSaveReferences(missingActorSave) is not { Count: 1 } missing
            || !missing[0].ToString().Contains("entity 'elder_instance'.ActorId", StringComparison.Ordinal)
            || !missing[0].ToString().Contains("actor.missing", StringComparison.Ordinal))
            problems.Add("a saved entity link should report its source instance and missing actor ID");

        var brokenJson = ValidContentJson
            .Replace("\"FactionId\": \"faction.mages\"", "\"FactionId\": \"faction.missing\"", StringComparison.Ordinal)
            .Replace("\"SpeakerActorId\": \"actor.elder\"", "\"SpeakerActorId\": \"actor.missing\"", StringComparison.Ordinal)
            .Replace("\"Next\": \"greet\"", "\"Next\": \"node.missing\"", StringComparison.Ordinal)
            .Replace("\"StartDialogueId\": \"dialogue.elder\"", "\"StartDialogueId\": \"dialogue.missing\"", StringComparison.Ordinal)
            .Replace("\"TargetActorId\": \"actor.elder\"", "\"TargetActorId\": \"actor.missing\"", StringComparison.Ordinal)
            .Replace("\"RequiredItemId\": \"item.potion\"", "\"RequiredItemId\": \"item.missing\"", StringComparison.Ordinal);
        try
        {
            RpgContentJson.FromJson(brokenJson);
            problems.Add("broken RPG content references should fail validation");
        }
        catch (InvalidDataException exception)
        {
            foreach (var expected in new[]
            {
                "actor 'actor.elder'.FactionId", "faction.missing",
                "dialogue 'dialogue.elder' node 'greet'.SpeakerActorId", "actor.missing",
                "node 'node.missing'", "quest 'quest.relic'.StartDialogueId", "dialogue.missing",
                "RequiresFactions", "faction.missing",
                "stage 'fetch'.TargetActorId", "stage 'fetch'.RequiredItemId", "item.missing"
            })
                if (!exception.Message.Contains(expected, StringComparison.Ordinal))
                    problems.Add($"content validation diagnostic should identify '{expected}'");
        }
    }

    private static void QuestObjectiveAuthoringChecks(List<string> problems)
    {
        var content = RpgContentJson.FromJson(ValidContentJson);
        var questId = new ContentId<QuestContentKind>("quest.panel");
        var targetActorId = new ContentId<ActorContentKind>("actor.elder");
        var targetInstanceId = Guid.Parse("9d1b12c8-6e25-4f4c-a7fd-294fd4ce0b21");
        var quest = new QuestDef
        {
            Id = questId,
            Title = "Clear the old vault",
            StartDialogueId = new ContentId<DialogueContentKind>("dialogue.elder"),
            Stages = new[]
            {
                new QuestStage
                {
                    Id = "defeat-guardian",
                    Journal = "Defeat the vault guardian.",
                    CompleteOn = QuestEventKind.ActorKilled,
                    TargetActorId = targetActorId,
                    TargetWorldInstanceId = targetInstanceId
                }
            }
        };
        content.Quests.Add(quest);

        var missingTargetContent = RpgContentJson.FromJson(RpgContentJson.ToJson(content));
        missingTargetContent.Quests.Add(quest with
        {
            Stages = new[] { quest.Stages[0] with { TargetActorId = new ContentId<ActorContentKind>("actor.missing") } }
        });
        if (!missingTargetContent.Validate().Any(diagnostic =>
            diagnostic.ToString().Contains("quest 'quest.panel' stage 'defeat-guardian'.TargetActorId", StringComparison.Ordinal)
            && diagnostic.ToString().Contains("actor.missing", StringComparison.Ordinal)))
            problems.Add("quest objective validation should identify a missing target actor ID and its owning stage");

        var reopened = RpgContentJson.FromJson(RpgContentJson.ToJson(content));
        if (reopened.Validate().Count != 0 || !reopened.Quests.TryGet(questId, out var loadedQuest)
            || loadedQuest.Stages.Single().TargetActorId != targetActorId
            || loadedQuest.Stages.Single().TargetWorldInstanceId != targetInstanceId)
        {
            problems.Add("a valid event objective and its typed target references should save and reopen");
            return;
        }

        var flags = new FlagStore();
        loadedQuest.Start(flags);
        var applied = QuestEventSystem.Apply(reopened.Quests, flags, new QuestEvent
        {
            EventId = Guid.NewGuid(),
            Kind = QuestEventKind.ActorKilled,
            WorldInstanceId = targetInstanceId,
            ActorId = targetActorId
        });
        if (applied != 1 || loadedQuest.StatusIn(flags) != QuestStatus.Complete)
            problems.Add("an authored event objective should complete when its validated target event is applied");
    }

    private static void ActorStatFormulaChecks(List<string> problems)
    {
        var stats = ExampleStats();
        if (stats.MaximumHealth != 26 || stats.MaximumMagicka != 45 || stats.MaximumStamina != 20)
            problems.Add($"default formulas should derive 26 health, 45 magicka, and 20 stamina (got {stats.MaximumHealth}, {stats.MaximumMagicka}, {stats.MaximumStamina})");

        var stronger = stats.WithAttribute(ActorAttribute.Strength, 12);
        if (stronger.MaximumHealth != 28 || stats.MaximumHealth != 26 || stats.Attributes.Strength != 10)
            problems.Add("changing strength should recalculate derived health without changing the original base stats");

        var actors = new ActorCatalogue();
        actors.Add(new ActorDef(new ContentId<ActorContentKind>("actor.fixture"), "Fixture", stats: stats));
        if (actors.Count != 1) problems.Add("ActorCatalogue should register a validated actor stats fixture");
    }

    private static void SkillUseProgressionChecks(List<string> problems)
    {
        var rules = RpgContentJson.FromJson(ValidContentJson).SkillProgression;
        var stats = ExampleStats();
        if (SkillUseSystem.TryRecordUse(stats, "combat.unknown", rules, out var ignored, out _)
            || ignored.Skills.GetValueOrDefault("Blade") != 34
            || ignored.SkillUseProgress.Count != 0)
            problems.Add("an unconfigured action should not advance any skill or use progress");

        if (!SkillUseSystem.TryRecordUse(stats, "combat.melee.hit", rules, out var firstUse, out var firstRankUp)
            || firstRankUp || firstUse.Skills.GetValueOrDefault("Blade") != 34
            || firstUse.SkillUseProgress.GetValueOrDefault("Blade") != 1
            || firstUse.Skills.ContainsKey("Alchemy"))
            problems.Add("one qualifying action should record progress only for the configured skill");

        var saved = SaveState.FromJson(new SaveState { Player = new PlayerRecord { Stats = firstUse } }.ToJson());
        var progressed = saved.Player.Stats;
        var rankedUp = false;
        for (var use = 0; use < 3; use++)
        {
            if (!SkillUseSystem.TryRecordUse(progressed, "combat.melee.hit", rules,
                out progressed, out var thisRankedUp))
            {
                problems.Add("a configured skill-use action should be accepted");
                return;
            }
            rankedUp |= thisRankedUp;
        }

        if (!rankedUp || progressed.Skills.GetValueOrDefault("Blade") != 36
            || progressed.SkillUseProgress.GetValueOrDefault("Blade") != 0
            || progressed.Skills.ContainsKey("Alchemy"))
            problems.Add("qualifying uses should advance only Blade after the saved partial progress reaches its threshold");
    }

    private static void ModifierRuleChecks(List<string> problems)
    {
        var stacked = new ActorStatModifiers()
            .Apply(new ActorStatModifier("boost-a", "potion:strength", ActorAttribute.Strength, 2, 10))
            .Apply(new ActorStatModifier("boost-b", "potion:strength", ActorAttribute.Strength, 3, 20));
        if (stacked.Active.Count != 2 || stacked.TotalFor(ActorAttribute.Strength) != 5)
            problems.Add("Stack should keep distinct applications and add their values");

        var replaced = stacked.Apply(new ActorStatModifier("replacement", "potion:strength",
            ActorAttribute.Strength, 4, 8, ModifierStackingRule.ReplaceSameSource));
        if (replaced.Active.Count != 1 || replaced.TotalFor(ActorAttribute.Strength) != 4)
            problems.Add("ReplaceSameSource should replace all matching source effects on that attribute");

        var refreshed = replaced.Apply(new ActorStatModifier("refresh", "potion:strength",
            ActorAttribute.Strength, 99, 20, ModifierStackingRule.RefreshDurationSameSource));
        if (refreshed.Active.Count != 1 || refreshed.TotalFor(ActorAttribute.Strength) != 4
            || refreshed.Active[0].RemainingSeconds != 20)
            problems.Add("RefreshDurationSameSource should preserve strength and extend the remaining duration");

        var afterTenSeconds = stacked.Advance(10);
        var afterTwentySeconds = afterTenSeconds.Advance(10);
        if (afterTenSeconds.Active.Count != 1 || afterTenSeconds.Active[0].Id != "boost-b"
            || afterTwentySeconds.Active.Count != 0 || stacked.Active.Count != 2)
            problems.Add("Advance should expire timed effects at their deadline without mutating the prior modifier set");

        var baseStats = ExampleStats();
        var effective = baseStats.WithModifiers(replaced);
        if (effective.MaximumHealth != 30 || baseStats.MaximumHealth != 26 || baseStats.Attributes.Strength != 10)
            problems.Add("an active modifier should recalculate derived stats without permanently changing base attributes");
    }

    private static void ContainerPersistenceChecks(List<string> problems)
    {
        var itemId = new ContentId<ItemContentKind>("item.apple");
        var definitions = new ItemCatalogue();
        definitions.Add(new ItemDef(itemId, "Apple", null, Stackable: true));
        var chestId = Guid.Parse("b41ee246-9991-4d38-b3a1-124ee936e1a7");
        var authoredContents = new Bag();
        authoredContents.Add(definitions.Get(itemId)!, 3);
        var containers = new ContainerInventoryStore().SetContents(chestId, authoredContents);
        authoredContents.Clear();

        var lootedContents = containers.GetContents(chestId)!;
        if (!lootedContents.Remove(itemId, 3))
            problems.Add("the fixture should be able to loot all items from a container");
        containers = containers.SetContents(chestId, lootedContents);

        var saved = new SaveState { ItemDefs = definitions, ContainerInventories = containers };
        var loaded = SaveState.FromJson(saved.ToJson());
        var restored = loaded.ContainerInventories.GetContents(chestId);
        if (restored is null || restored.Count(itemId) != 0 || loaded.ContainerInventories.Count != 1)
            problems.Add("an emptied container should remain empty under the same world instance after save/reload");
        if (containers.GetContents(chestId)?.Count(itemId) != 0)
            problems.Add("reading or changing a container bag should not mutate its stored snapshot");
    }

    private static void InventoryTransferChecks(List<string> problems)
    {
        var itemId = new ContentId<ItemContentKind>("item.apple");
        var unknownId = new ContentId<ItemContentKind>("item.unknown");
        var definitions = new ItemCatalogue();
        definitions.Add(new ItemDef(itemId, "Apple", null, Stackable: true));
        var inventory = new Bag();
        inventory.Add(definitions.Get(itemId)!, 2);
        var sourceWorldId = Guid.NewGuid();
        var worldItems = new WorldItemStore();
        if (!worldItems.TryAdd(new WorldItemEntry(sourceWorldId, unknownId, 1), out worldItems))
            problems.Add("the invalid-definition transfer fixture should enter the test world");
        if (InventoryTransfer.TryPickup(sourceWorldId, inventory, worldItems, definitions,
            out var failedInventory, out var failedWorld)
            || inventory.Count(itemId) != 2 || failedInventory.Count(itemId) != 2
            || !worldItems.TryGet(sourceWorldId, out _) || !failedWorld.TryGet(sourceWorldId, out _))
            problems.Add("a pickup with a missing item definition should preserve both source and destination");

        if (!new WorldItemStore().TryAdd(new WorldItemEntry(sourceWorldId, itemId, 3), out worldItems))
        {
            problems.Add("the valid world-item fixture should enter the test world");
            return;
        }
        if (!InventoryTransfer.TryPickup(sourceWorldId, inventory, worldItems, definitions,
            out var pickedInventory, out var pickedWorld)
            || pickedInventory.Count(itemId) != 5 || pickedWorld.TryGet(sourceWorldId, out _)
            || inventory.Count(itemId) != 2 || !worldItems.TryGet(sourceWorldId, out _))
            problems.Add("a successful pickup should move the whole stack once and leave the inputs unchanged");

        var droppedId = Guid.NewGuid();
        if (InventoryTransfer.TryDrop(droppedId, pickedInventory, pickedWorld, definitions, itemId, 6,
            out var failedDropInventory, out var failedDropWorld)
            || failedDropInventory.Count(itemId) != 5 || failedDropWorld.Count != 0)
            problems.Add("an insufficient-inventory drop should preserve both sides");
        if (!InventoryTransfer.TryDrop(droppedId, pickedInventory, pickedWorld, definitions, itemId, 2,
            out var droppedInventory, out var droppedWorld)
            || droppedInventory.Count(itemId) != 3 || !droppedWorld.TryGet(droppedId, out var dropped)
            || dropped.Count != 2)
            problems.Add("a successful drop should remove exactly the requested count and create one world instance");
        if (InventoryTransfer.TryDrop(droppedId, droppedInventory, droppedWorld, definitions, itemId, 1,
            out var duplicateInventory, out var duplicateWorld)
            || duplicateInventory.Count(itemId) != 3 || duplicateWorld.Count != 1)
            problems.Add("a duplicate world instance ID should reject the drop without changing either side");

        var saveRoundTrip = SaveState.FromJson(new SaveState { ItemDefs = definitions, WorldItems = droppedWorld }.ToJson());
        if (!saveRoundTrip.WorldItems.TryGet(droppedId, out var restored) || restored.Count != 2)
            problems.Add("dropped world-item identity and count should survive a save/load");
    }

    private static void EquipmentChecks(List<string> problems)
    {
        var ironId = new ContentId<ItemContentKind>("item.iron");
        var steelId = new ContentId<ItemContentKind>("item.steel");
        var definitions = new ItemCatalogue();
        definitions.Add(new ItemDef(ironId, "Iron Sword", "mainhand", Stackable: false)
        {
            StatBonuses = new[] { new EquipmentStatBonus(ActorAttribute.Strength, 3) },
            AttachmentBone = "RightHand"
        });
        definitions.Add(new ItemDef(steelId, "Steel Sword", "mainhand", Stackable: false)
        {
            StatBonuses = new[] { new EquipmentStatBonus(ActorAttribute.Strength, 1) },
            AttachmentBone = "RightHand"
        });
        var bag = new Bag();
        bag.Add(definitions.Get(ironId)!);
        bag.Add(definitions.Get(steelId)!);
        var player = new PlayerRecord { Bag = bag, Stats = ExampleStats() };

        if (!EquipmentSystem.TryEquip(player, definitions, ironId, out var ironEquipped)
            || ironEquipped.Bag.Count(ironId) != 0 || ironEquipped.Equip.Get("mainhand") != ironId.Value
            || EquipmentSystem.EffectiveStats(ironEquipped, definitions).MaximumHealth != 29
            || EquipmentSystem.Attachments(ironEquipped, definitions).Count != 1)
            problems.Add("equipping should consume one bag item and apply its stat and bone-attachment data once");

        if (!EquipmentSystem.TryEquip(ironEquipped, definitions, steelId, out var steelEquipped)
            || steelEquipped.Bag.Count(ironId) != 1 || steelEquipped.Bag.Count(steelId) != 0
            || EquipmentSystem.EffectiveStats(steelEquipped, definitions).MaximumHealth != 27
            || EquipmentSystem.Attachments(steelEquipped, definitions)[0].ItemId != steelId)
            problems.Add("replacing equipment should return the old item and replace its stat/attachment effects once");

        if (!EquipmentSystem.TryUnequip(steelEquipped, definitions, "mainhand", out var unequipped)
            || unequipped.Bag.Count(steelId) != 1 || unequipped.Equip.Count != 0
            || EquipmentSystem.EffectiveStats(unequipped, definitions).MaximumHealth != 26
            || EquipmentSystem.Attachments(unequipped, definitions).Count != 0)
            problems.Add("unequipping should return the item and remove its stat and attachment effects");

        if (EquipmentSystem.TryEquip(unequipped, definitions, new ContentId<ItemContentKind>("missing"), out var unchanged)
            || unchanged.Bag.Count(steelId) != 1 || unchanged.Equip.Count != 0)
            problems.Add("a failed equip should preserve inventory and slots");
    }

    private static void MeleeAndActorPersistenceChecks(List<string> problems)
    {
        var attackerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var actorContentId = new ContentId<ActorContentKind>("actor.guard");
        var actorDef = new ActorDef(actorContentId, "Guard", stats: ExampleStats());
        var attacker = ActorRuntimeState.Create(attackerId, actorDef);
        var targetBag = new Bag();
        targetBag.Add(new ItemDef("item.loot", "Loot", null, Stackable: true), 3);
        var target = new ActorRuntimeState(targetId, actorContentId, 10, targetBag);
        var profile = new MeleeAttackProfile(range: 2, damage: 6, cooldownSeconds: 1);

        if (!MeleeCombat.TryAttack(attacker, target, 1, profile, out var attacking, out var hit)
            || hit.CurrentHealth != 4 || hit.IsDead || attacking.MeleeCooldownRemaining != 1)
            problems.Add("a valid melee attack should apply damage once and start the attacker's cooldown");
        if (MeleeCombat.TryAttack(attacking, hit, 1, profile, out var blockedAttacker, out var blockedTarget)
            || blockedAttacker.MeleeCooldownRemaining != 1 || blockedTarget.CurrentHealth != 4)
            problems.Add("a second attack during cooldown should change neither actor");

        var ready = MeleeCombat.AdvanceCooldown(attacking, 1);
        if (MeleeCombat.TryAttack(ready, hit, 3, profile, out _, out _))
            problems.Add("an out-of-range target should not take damage");
        if (!MeleeCombat.TryAttack(ready, hit, 1, profile, out var finalAttacker, out var deadTarget)
            || deadTarget.CurrentHealth != 0 || !deadTarget.IsDead)
            problems.Add("a later valid hit should set actor health to zero and persist a dead state");

        var store = new ActorRuntimeStore().Set(finalAttacker).Set(deadTarget);
        var defs = new ItemCatalogue();
        defs.Add(new ItemDef("item.loot", "Loot", null, Stackable: true));
        var saved = SaveState.FromJson(new SaveState { ItemDefs = defs, ActorStates = store }.ToJson());
        if (!saved.ActorStates.TryGet(targetId, out var restored)
            || !restored.IsDead || restored.CurrentHealth != 0 || restored.Inventory.Count("item.loot") != 3
            || !saved.ActorStates.TryGet(attackerId, out var restoredAttacker)
            || restoredAttacker.MeleeCooldownRemaining != 1)
            problems.Add("actor death, carried inventory, world identity, and cooldown should survive save/load");
    }

    private static void EnemyCombatAiChecks(List<string> problems)
    {
        var actorId = new ContentId<ActorContentKind>("actor.guard");
        var enemy = new ActorRuntimeState(Guid.NewGuid(), actorId, 12);
        var target = new ActorRuntimeState(Guid.NewGuid(), actorId, 10);
        var profile = new MeleeAttackProfile(range: 1.5, damage: 3, cooldownSeconds: 1);
        var idle = new EnemyCombatAiMemory();

        var chase = EnemyCombatAi.Tick(idle, enemy, target,
            new Vector3(0, 1, 0), new Vector3(5, 1, 0), canSeeTarget: true, attackProfile: profile);
        if (chase.Memory.State != EnemyCombatAiState.Chasing
            || chase.Memory.TargetWorldInstanceId != target.WorldInstanceId
            || chase.DesiredMoveDirection != Vector3.UnitX || chase.AttackLanded)
            problems.Add("a visible distant target should make an enemy pursue in the target's direction");

        var attack = EnemyCombatAi.Tick(chase.Memory, chase.Enemy, chase.Target,
            new Vector3(3, 1, 0), new Vector3(4, 1, 0), canSeeTarget: true, attackProfile: profile);
        if (attack.Memory.State != EnemyCombatAiState.Attacking || !attack.AttackLanded
            || attack.DesiredMoveDirection != Vector3.Zero || attack.Target?.CurrentHealth != 7
            || attack.Enemy.MeleeCooldownRemaining != 1)
            problems.Add("an enemy that reaches a visible target should use the existing melee action and stop moving");

        var cooldown = EnemyCombatAi.Tick(attack.Memory, attack.Enemy, attack.Target,
            new Vector3(3, 1, 0), new Vector3(4, 1, 0), canSeeTarget: true, attackProfile: profile);
        if (cooldown.AttackLanded || cooldown.Target?.CurrentHealth != 7)
            problems.Add("an enemy should not deal a second hit during its melee cooldown");

        var lost = EnemyCombatAi.Tick(cooldown.Memory, cooldown.Enemy, cooldown.Target,
            new Vector3(3, 1, 0), new Vector3(4, 1, 0), canSeeTarget: false, attackProfile: profile);
        if (lost.Memory.State != EnemyCombatAiState.Idle || lost.Memory.TargetWorldInstanceId.HasValue
            || lost.DesiredMoveDirection != Vector3.Zero || lost.AttackLanded)
            problems.Add("an enemy should clear its target and stop when line of sight is lost");

        var deadEnemy = attack.Enemy with { CurrentHealth = 0, IsDead = true };
        var dead = EnemyCombatAi.Tick(attack.Memory, deadEnemy, attack.Target,
            new Vector3(3, 1, 0), new Vector3(4, 1, 0), canSeeTarget: true, attackProfile: profile);
        if (dead.Memory.State != EnemyCombatAiState.Dead || dead.DesiredMoveDirection != Vector3.Zero
            || dead.AttackLanded || dead.Enemy != deadEnemy)
            problems.Add("a dead enemy should stop moving and attacking");
    }

    private static void WorldClockAndScheduleChecks(List<string> problems)
    {
        var homeCellId = Guid.NewGuid();
        var workCellId = Guid.NewGuid();
        var schedule = new NpcDailySchedule(homeCellId, workCellId,
            WorkStartSeconds: 6 * 60 * 60, WorkEndSeconds: 18 * 60 * 60);
        var clock = new WorldClock(5 * 60 * 60);
        var state = new NpcScheduleRuntimeState
        {
            CurrentCellId = homeCellId,
            LastEvaluatedSeconds = clock.TotalSeconds
        };
        var actor = new ActorRuntimeState(Guid.NewGuid(), new ContentId<ActorContentKind>("actor.worker"), 20,
            meleeCooldownRemaining: 1000, currentMagicka: 15, spellCooldownRemaining: 500);

        var beforeWork = NpcScheduleSystem.Evaluate(schedule, clock, state);
        var sunrise = clock.Advance(60 * 60);
        var startWork = NpcScheduleSystem.Evaluate(schedule, sunrise, beforeWork.State);
        var duplicate = NpcScheduleSystem.Evaluate(schedule, sunrise, startWork.State);
        if (beforeWork.NewTravelRequestCellId.HasValue
            || startWork.DesiredCellId != workCellId || startWork.NewTravelRequestCellId != workCellId
            || duplicate.NewTravelRequestCellId.HasValue
            || duplicate.State.PendingDestinationCellId != workCellId)
            problems.Add("daily schedule boundaries should select the right cell and issue one pending travel request");

        var missedDeparture = NpcScheduleSystem.Evaluate(schedule,
            new WorldClock(18 * 60 * 60), startWork.State);
        if (missedDeparture.DesiredCellId != homeCellId || missedDeparture.NewTravelRequestCellId.HasValue
            || missedDeparture.State.PendingDestinationCellId.HasValue
            || missedDeparture.State.CurrentCellId != homeCellId)
            problems.Add("a schedule change before departure should cancel the stale pending route");

        var enteredWorkCell = NpcScheduleSystem.RecordCellEntered(startWork.State, workCellId);
        var changedMidTrip = NpcScheduleSystem.Evaluate(schedule,
            new WorldClock(18 * 60 * 60), enteredWorkCell);
        if (changedMidTrip.DesiredCellId != homeCellId || changedMidTrip.NewTravelRequestCellId != homeCellId
            || changedMidTrip.State.CurrentCellId != workCellId
            || changedMidTrip.State.PendingDestinationCellId != homeCellId)
            problems.Add("a schedule change mid-route should replace the pending destination from the actor's current cell");

        state = NpcScheduleSystem.CompleteTravel(duplicate.State, workCellId);
        clock = sunrise.Advance(12 * 60 * 60);
        var returnHome = NpcScheduleSystem.Evaluate(schedule, clock, state);
        var duplicateReturn = NpcScheduleSystem.Evaluate(schedule, clock, returnHome.State);
        if (returnHome.DesiredCellId != homeCellId || returnHome.NewTravelRequestCellId != homeCellId
            || duplicateReturn.NewTravelRequestCellId.HasValue)
            problems.Add("the evening schedule boundary should request one return-home trip");

        state = NpcScheduleSystem.CompleteTravel(duplicateReturn.State, homeCellId);
        var afterLongDormancy = clock.Advance(100 * WorldClock.SecondsPerDay + 13 * 60 * 60);
        var caughtUp = NpcScheduleSystem.CatchUpDormant(schedule, afterLongDormancy, state, actor);
        if (caughtUp.DesiredCellId != workCellId || caughtUp.State.CurrentCellId != workCellId
            || caughtUp.State.PendingDestinationCellId.HasValue
            || caughtUp.Actor.MeleeCooldownRemaining != 0 || caughtUp.Actor.SpellCooldownRemaining != 0
            || caughtUp.BoundariesCollapsed != 201 || caughtUp.WorkItemsProcessed != 1)
            problems.Add("dormant schedule catch-up should materialize the current work cell and advance timers in bounded work");
    }

    private static void NpcScheduleSaveChecks(List<string> problems)
    {
        var npcInstanceId = Guid.Parse("8da48350-0472-4f88-a542-f408f7dc06a1");
        var homeCellId = Guid.Parse("c4a17e31-ae23-4804-9aa8-d69b6a1ce101");
        var workCellId = Guid.Parse("c4a17e31-ae23-4804-9aa8-d69b6a1ce102");
        var scheduleState = new NpcScheduleRuntimeState
        {
            CurrentCellId = workCellId,
            PendingDestinationCellId = homeCellId,
            LastEvaluatedSeconds = 65_000
        };
        var save = new SaveState
        {
            WorldTimeSeconds = 65_000,
            NpcSchedules = new NpcScheduleStore().Set(npcInstanceId, scheduleState)
        };
        var loaded = SaveState.FromJson(save.ToJson());
        if (loaded.WorldTimeSeconds != save.WorldTimeSeconds
            || !loaded.NpcSchedules.TryGet(npcInstanceId, out var restored)
            || restored != scheduleState)
            problems.Add("world time and pending NPC schedule destinations should survive an RPG save round trip");

        var migrated = SaveState.FromJson("{\"Version\":1}");
        if (migrated.Version != SaveState.CurrentVersion || migrated.WorldTimeSeconds != 0
            || migrated.NpcSchedules.Entries.Count != 0)
            problems.Add("version 1 RPG saves should migrate to version 2 with an empty clock and schedule state");

        try
        {
            SaveState.FromJson("{\"Version\":2,\"UnknownField\":true}");
            problems.Add("RPG saves should reject unmapped fields instead of silently ignoring them");
        }
        catch (System.Text.Json.JsonException)
        {
        }
    }

    private static void SpellAndFactionChecks(List<string> problems)
    {
        var factionId = new ContentId<FactionContentKind>("faction.mages");
        var actorId = new ContentId<ActorContentKind>("actor.mage");
        var actorDef = new ActorDef(actorId, "Mage", stats: ExampleStats());
        var caster = ActorRuntimeState.Create(Guid.NewGuid(), actorDef);
        var target = ActorRuntimeState.Create(Guid.NewGuid(), actorDef);
        var spell = new TargetedSpellDef
        {
            Id = new ContentId<SpellContentKind>("spell.focus"),
            Range = 5,
            MagickaCost = 12,
            CooldownSeconds = 2,
            Attribute = ActorAttribute.Strength,
            Magnitude = 4,
            DurationSeconds = 4,
            StackingRule = ModifierStackingRule.ReplaceSameSource
        };
        var castId = Guid.NewGuid();
        if (TargetedSpellSystem.TryCast(castId, caster, target, 6, spell, out var farCaster, out var farTarget)
            || farCaster.CurrentMagicka != caster.CurrentMagicka || farTarget.Modifiers.Active.Count != 0)
            problems.Add("an out-of-range spell should consume no magicka and apply no effect");
        if (TargetedSpellSystem.TryCast(castId, caster with { CurrentMagicka = 2 }, target, 2, spell,
            out var poorCaster, out var poorTarget)
            || poorCaster.CurrentMagicka != 2 || poorTarget.Modifiers.Active.Count != 0)
            problems.Add("a spell without enough magicka should change neither actor");
        if (!TargetedSpellSystem.TryCast(castId, caster, target, 2, spell, out var castCaster, out var castTarget)
            || castCaster.CurrentMagicka != 33 || castCaster.SpellCooldownRemaining != 2
            || castTarget.Modifiers.TotalFor(ActorAttribute.Strength) != 4
            || castTarget.EffectiveStats(actorDef.Stats).MaximumHealth != 30)
            problems.Add("a valid spell should spend its cost once and apply a timed stat effect to the target");
        if (TargetedSpellSystem.TryCast(castId, castCaster.AdvanceTime(2), castTarget, 2, spell,
            out var replayCaster, out var replayTarget)
            || replayCaster.CurrentMagicka != castCaster.CurrentMagicka
            || replayTarget.Modifiers.TotalFor(ActorAttribute.Strength) != 4)
            problems.Add("replaying a saved cast ID should not spend resources or apply its effect twice");
        if (castTarget.AdvanceTime(4).Modifiers.Active.Count != 0)
            problems.Add("a targeted spell modifier should expire on the simulation clock");

        var spellSave = SaveState.FromJson(new SaveState
        {
            ActorStates = new ActorRuntimeStore().Set(castCaster).Set(castTarget)
        }.ToJson());
        if (!spellSave.ActorStates.TryGet(castCaster.WorldInstanceId, out var savedCaster)
            || !spellSave.ActorStates.TryGet(castTarget.WorldInstanceId, out var savedTarget)
            || savedTarget.Modifiers.Active.Count != 1
            || TargetedSpellSystem.TryCast(castId, savedCaster.AdvanceTime(2), savedTarget, 2, spell,
                out var replayedCaster, out _)
            || replayedCaster.CurrentMagicka != savedCaster.CurrentMagicka)
            problems.Add("spell cost, cooldown, timed effect, and one-time cast ID should survive actor save/load");

        var joined = FactionSystem.SetMembership(caster, factionId, true);
        joined = FactionSystem.AdjustReputation(joined, factionId, 18);
        var independent = FactionSystem.SetMembership(target, factionId, false);
        if (!FactionSystem.IsMember(joined, factionId) || FactionSystem.Reputation(joined, factionId) != 18
            || FactionSystem.IsMember(independent, factionId) || FactionSystem.Reputation(independent, factionId) != 0
            || caster.Factions.Count != 0)
            problems.Add("membership and reputation changes should be per-actor immutable state");

        var factionSave = SaveState.FromJson(new SaveState
        {
            ActorStates = new ActorRuntimeStore().Set(joined).Set(independent)
        }.ToJson());
        if (!factionSave.ActorStates.TryGet(joined.WorldInstanceId, out var restored)
            || !FactionSystem.IsMember(restored, factionId) || FactionSystem.Reputation(restored, factionId) != 18)
            problems.Add("actor faction membership and reputation should survive save/load");
    }

    private static void QuestEventChecks(List<string> problems)
    {
        var questId = new ContentId<QuestContentKind>("quest.events");
        var actorId = new ContentId<ActorContentKind>("actor.guard");
        var itemId = new ContentId<ItemContentKind>("item.relic");
        var talkTarget = Guid.NewGuid();
        var deadActorInstance = Guid.NewGuid();
        var relicInstance = Guid.NewGuid();
        var quest = new QuestDef
        {
            Id = questId,
            Title = "Stable Event Quest",
            Stages = new[]
            {
                new QuestStage { Id = "talk", CompleteOn = QuestEventKind.Interaction, TargetWorldInstanceId = talkTarget },
                new QuestStage
                {
                    Id = "kill", CompleteOn = QuestEventKind.ActorKilled,
                    TargetWorldInstanceId = deadActorInstance, TargetActorId = actorId
                },
                new QuestStage
                {
                    Id = "collect", CompleteOn = QuestEventKind.ItemCollected,
                    TargetWorldInstanceId = relicInstance, RequiredItemId = itemId
                }
            }
        };
        var quests = new QuestCatalogue();
        quests.Add(quest);
        var flags = new FlagStore();
        quest.Start(flags);

        var wrongInteraction = new QuestEvent
        {
            EventId = Guid.NewGuid(), Kind = QuestEventKind.Interaction, WorldInstanceId = Guid.NewGuid()
        };
        if (QuestEventSystem.Apply(quests, flags, wrongInteraction) != 0
            || quest.StageIn(flags)?.Id != "talk")
            problems.Add("an interaction at a different stable world instance should not advance the quest");

        var interaction = new QuestEvent
        {
            EventId = Guid.NewGuid(), Kind = QuestEventKind.Interaction, WorldInstanceId = talkTarget
        };
        if (QuestEventSystem.Apply(quests, flags, interaction) != 1
            || QuestEventSystem.Apply(quests, flags, interaction) != 0
            || quest.StageIn(flags)?.Id != "kill")
            problems.Add("a stable interaction event should advance once and ignore a replay of the same event ID");

        var deadActor = new ActorRuntimeState(deadActorInstance, actorId, currentHealth: 0);
        var deadActorSave = SaveState.FromJson(new SaveState
        {
            ActorStates = new ActorRuntimeStore().Set(deadActor),
            Flags = flags
        }.ToJson());
        if (!deadActorSave.ActorStates.TryGet(deadActorInstance, out var restoredDead) || !restoredDead.IsDead)
            problems.Add("the quest target fixture should remain dead in saved actor state after its cell unloads");

        var killed = new QuestEvent
        {
            EventId = Guid.NewGuid(), Kind = QuestEventKind.ActorKilled,
            WorldInstanceId = deadActorInstance, ActorId = actorId
        };
        if (QuestEventSystem.Apply(quests, deadActorSave.Flags, killed) != 1
            || quest.StageIn(deadActorSave.Flags)?.Id != "collect")
            problems.Add("a stable actor-killed event should resolve a dead target without a loaded cell object");

        var progressedSave = SaveState.FromJson(new SaveState { Flags = deadActorSave.Flags }.ToJson());
        var collected = new QuestEvent
        {
            EventId = Guid.NewGuid(), Kind = QuestEventKind.ItemCollected,
            WorldInstanceId = relicInstance, ItemId = itemId
        };
        if (QuestEventSystem.Apply(quests, progressedSave.Flags, collected) != 1
            || quest.StatusIn(progressedSave.Flags) != QuestStatus.Complete)
            problems.Add("a stable item-collected event should finish the saved quest after its target cell unloads");
    }

    private static void MerchantTradeChecks(List<string> problems)
    {
        var itemId = new ContentId<ItemContentKind>("item.apple");
        var definitions = new ItemCatalogue();
        definitions.Add(new ItemDef(itemId, "Apple", null, Stackable: true));
        var actorDef = new ActorDef(new ContentId<ActorContentKind>("actor.merchant"), "Merchant", stats: ExampleStats());
        var stock = new Bag();
        stock.Add(definitions.Get(itemId)!, 5);
        var player = new PlayerRecord { Currency = 100 };
        var merchant = ActorRuntimeState.Create(Guid.NewGuid(), actorDef, stock) with { Currency = 500 };

        if (MerchantTrade.TryBuy(player with { Currency = 10 }, merchant, definitions, itemId, 2, 10,
            out var poorPlayer, out var richStock)
            || poorPlayer.Currency != 10 || richStock.Inventory.Count(itemId) != 5)
            problems.Add("a buy without enough currency should preserve both inventories and balances");
        if (MerchantTrade.TryBuy(player, merchant with { Inventory = new Bag() }, definitions, itemId, 1, 10,
            out var noStockPlayer, out var noStockMerchant)
            || noStockPlayer.Currency != 100 || noStockMerchant.Currency != 500)
            problems.Add("a buy without merchant stock should preserve both parties");
        if (!MerchantTrade.TryBuy(player, merchant, definitions, itemId, 2, 10,
            out var boughtPlayer, out var paidMerchant)
            || boughtPlayer.Currency != 80 || boughtPlayer.Bag.Count(itemId) != 2
            || paidMerchant.Currency != 520 || paidMerchant.Inventory.Count(itemId) != 3
            || player.Currency != 100 || merchant.Inventory.Count(itemId) != 5)
            problems.Add("a valid buy should atomically transfer items and currency without mutating its inputs");

        if (MerchantTrade.TrySell(boughtPlayer, paidMerchant with { Currency = 1 }, definitions, itemId, 1, 10,
            out var poorMerchantPlayer, out var poorMerchant)
            || poorMerchantPlayer.Currency != 80 || poorMerchant.Currency != 1)
            problems.Add("a merchant unable to pay for a sale should change neither side");
        if (!MerchantTrade.TrySell(boughtPlayer, paidMerchant, definitions, itemId, 1, 10,
            out var soldPlayer, out var soldMerchant)
            || soldPlayer.Currency != 90 || soldPlayer.Bag.Count(itemId) != 1
            || soldMerchant.Currency != 510 || soldMerchant.Inventory.Count(itemId) != 4)
            problems.Add("a valid sale should atomically transfer items and currency");

        var overflowMerchant = merchant with { Currency = long.MaxValue };
        if (MerchantTrade.TryBuy(player, overflowMerchant, definitions, itemId, 1, 1,
            out var overflowPlayer, out var unchangedMerchant)
            || overflowPlayer.Currency != player.Currency || unchangedMerchant.Currency != long.MaxValue
            || unchangedMerchant.Inventory.Count(itemId) != 5)
            problems.Add("a merchant balance overflow should reject a buy without changing either party");

        var roundTrip = SaveState.FromJson(new SaveState
        {
            Player = boughtPlayer,
            ItemDefs = definitions,
            ActorStates = new ActorRuntimeStore().Set(paidMerchant)
        }.ToJson());
        if (roundTrip.Player.Currency != 80
            || !roundTrip.ActorStates.TryGet(merchant.WorldInstanceId, out var restoredMerchant)
            || restoredMerchant.Currency != 520)
            problems.Add("player and merchant balances should survive save/load");
    }

    private static void TheftChecks(List<string> problems)
    {
        var factionId = new ContentId<FactionContentKind>("faction.mages");
        var actorId = new ContentId<ActorContentKind>("actor.elder");
        var itemId = new ContentId<ItemContentKind>("item.apple");
        var definitions = new ItemCatalogue();
        definitions.Add(new ItemDef(itemId, "Apple", null, Stackable: true));
        var actorDef = new ActorDef(actorId, "Thief", stats: ExampleStats());
        var thief = ActorRuntimeState.Create(Guid.NewGuid(), actorDef);
        var firstItemId = Guid.NewGuid();
        var firstEventId = Guid.NewGuid();
        var firstWorld = new WorldItemStore();
        if (!firstWorld.TryAdd(new WorldItemEntry(firstItemId, itemId, 1) { OwnerFactionId = factionId }, out firstWorld))
        {
            problems.Add("an owned world item should be valid");
            return;
        }

        if (!TheftSystem.TryTake(firstEventId, thief, firstItemId, firstWorld, definitions,
            witnessed: true, reputationPenalty: -5, out var witnessedThief, out var emptiedWorld, out var witnessedTheft)
            || !witnessedTheft || witnessedThief.Inventory.Count(itemId) != 1
            || FactionSystem.Reputation(witnessedThief, factionId) != -5
            || emptiedWorld.TryGet(firstItemId, out _))
            problems.Add("a witnessed faction theft should transfer the item and apply one reputation penalty");
        if (TheftSystem.TryTake(firstEventId, witnessedThief, firstItemId, firstWorld, definitions,
            witnessed: true, reputationPenalty: -5, out var replayTaker, out _, out _)
            || FactionSystem.Reputation(replayTaker, factionId) != -5
            || replayTaker.Inventory.Count(itemId) != 1)
            problems.Add("replaying a witnessed theft event should not transfer again or double the penalty");

        var secondItemId = Guid.NewGuid();
        var unwitnessedEventId = Guid.NewGuid();
        var secondWorld = new WorldItemStore();
        secondWorld.TryAdd(new WorldItemEntry(secondItemId, itemId, 1) { OwnerFactionId = factionId }, out secondWorld);
        if (!TheftSystem.TryTake(unwitnessedEventId, thief, secondItemId, secondWorld, definitions,
            witnessed: false, reputationPenalty: -5, out var unwitnessedThief, out _, out var unwitnessedTheft)
            || !unwitnessedTheft || FactionSystem.Reputation(unwitnessedThief, factionId) != 0
            || !unwitnessedThief.ProcessedTheftEventIds.Contains(unwitnessedEventId))
            problems.Add("an unwitnessed theft should transfer the item without reputation loss and still record its event");
        if (TheftSystem.TryTake(unwitnessedEventId, unwitnessedThief, secondItemId, secondWorld, definitions,
            witnessed: true, reputationPenalty: -5, out var replayedUnwitnessed, out _, out _)
            || FactionSystem.Reputation(replayedUnwitnessed, factionId) != 0)
            problems.Add("replaying an unwitnessed event as witnessed should not apply a delayed reputation penalty");

        var member = FactionSystem.SetMembership(thief, factionId, true);
        var lawfulItemId = Guid.NewGuid();
        var lawfulWorld = new WorldItemStore();
        lawfulWorld.TryAdd(new WorldItemEntry(lawfulItemId, itemId, 1) { OwnerFactionId = factionId }, out lawfulWorld);
        if (!TheftSystem.TryTake(Guid.NewGuid(), member, lawfulItemId, lawfulWorld, definitions,
            witnessed: true, reputationPenalty: -5, out var memberTaker, out _, out var lawfulTheft)
            || lawfulTheft || FactionSystem.Reputation(memberTaker, factionId) != 0
            || memberTaker.ProcessedTheftEventIds.Count != 0)
            problems.Add("a faction member should take faction property without theft penalties");

        var save = SaveState.FromJson(new SaveState
        {
            ItemDefs = definitions,
            ActorStates = new ActorRuntimeStore().Set(witnessedThief),
            WorldItems = new WorldItemStore().TryAdd(
                new WorldItemEntry(Guid.NewGuid(), itemId, 1) { OwnerFactionId = factionId }, out var ownedItems)
                ? ownedItems : new WorldItemStore()
        }.ToJson());
        var pack = RpgContentJson.FromJson(ValidContentJson);
        if (!save.ActorStates.TryGet(witnessedThief.WorldInstanceId, out var restoredThief))
            problems.Add("a saved thief actor should be restored");
        else if (!restoredThief.ProcessedTheftEventIds.Contains(firstEventId)
            || FactionSystem.Reputation(restoredThief, factionId) != -5
            || pack.ValidateSaveReferences(save).Count != 0)
            problems.Add("theft replay records, faction standing, and world-item ownership should survive a valid save/load");
        else if (TheftSystem.TryTake(firstEventId, restoredThief, firstItemId, firstWorld, definitions,
            witnessed: true, reputationPenalty: -5, out var replayedAfterLoad, out _, out _)
            || replayedAfterLoad.Inventory.Count(itemId) != 1
            || FactionSystem.Reputation(replayedAfterLoad, factionId) != -5)
            problems.Add("a witnessed theft event replayed after loading should remain idempotent");
    }

    /// <summary>
    /// The dialogue test: load a conversation from JSON, then — given the flags the state
    /// already carries — take the gated option and let it advance the node and write its
    /// flag. The state is saved after this, so the load half of the test is Main's.
    /// </summary>
    private static void PlayDialogue(SaveState state, List<string> problems)
    {
        var tree = DialogueTree.FromJson(ElderDialogue);
        var member = new FactionStanding(new ContentId<FactionContentKind>("faction.mages"), true, 18);
        var context = new DialogueContext(state.Flags, ExampleStats(), new[] { member });
        state.Dialogue.Tree = tree.Id;
        state.Dialogue.Node = "greet";

        if (tree.Available("greet", new DialogueContext(new FlagStore(), ExampleStats())).Count != 1)
            problems.Add("with no flags set, only the ungated option should be offered");

        var open = tree.Available("greet", context);
        if (open.Count != 2)
        {
            problems.Add($"with the required stats, flags, and faction standing, both options should be offered (got {open.Count})");
            return;
        }
        var nonMember = new DialogueContext(state.Flags, ExampleStats(),
            new[] { member with { IsMember = false } });
        if (tree.Available("greet", nonMember).Count != 1)
            problems.Add("the faction-gated choice should be hidden from a non-member");

        // open[1] is the gated option: Next = "gate", Sets = { gate_topic: true }.
        tree.Pick(state.Dialogue, context, open[1]);
        tree.Pick(state.Dialogue, context, open[1]);

        if (state.Dialogue.Node != "gate")
            problems.Add($"picking the gated option should advance to 'gate', not '{state.Dialogue.Node}'");
        if (!state.Flags.GetBool("gate_topic"))
            problems.Add("picking the gated option should write the gate_topic flag");
        if (state.Dialogue.TakenChoiceIds.Count != 1)
            problems.Add("picking the same dialogue choice twice should execute it only once");
    }

    /// <summary>
    /// The quest test: load a quest from JSON, start it, walk its stages as the flags
    /// arrive, and prove a save taken mid-quest comes back at the same stage — then
    /// finish it and prove a save taken after completion still reads Complete.
    /// </summary>
    private static void QuestRoundTrip(SaveState state, string path, List<string> problems)
    {
        var catalogue = QuestCatalogue.FromJson(ElderQuest);
        if (catalogue.Count != 1 || !catalogue.TryGet("relic", out var quest))
        {
            problems.Add("the quest document should load one quest with id 'relic'");
            return;
        }

        if (quest.StatusIn(state.Flags) != QuestStatus.NotStarted)
            problems.Add("relic should be NotStarted before Start");

        quest.Start(state.Flags);
        if (quest.StatusIn(state.Flags) != QuestStatus.Active)
            problems.Add("relic should be Active after Start");
        if (quest.StageIn(state.Flags)?.Id != "fetch")
            problems.Add("the first stage should be 'fetch'");

        // Mid-quest save: stage one is still open, stage two has not begun.
        state.Write(path);
        var mid = SaveState.Read(path);
        var midStage = quest.StageIn(mid.Flags);
        if (midStage?.Id != "fetch")
            problems.Add($"a mid-quest load should still be on stage 'fetch', is '{midStage?.Id}'");

        // Arrive at the first stage's completion flag — the stage advances with no
        // quest write, because progress is pure derivation.
        mid.Flags.Set("relic_taken", true);
        if (quest.StageIn(mid.Flags)?.Id != "deliver")
            problems.Add("with relic_taken set, the stage should advance to 'deliver'");

        // Finish the quest.
        mid.Flags.Set("relic_delivered", true);
        if (quest.StatusIn(mid.Flags) != QuestStatus.Complete)
            problems.Add("relic should be Complete once both stages are done");
        if (quest.StageIn(mid.Flags) is not null)
            problems.Add("StageIn should be null when the quest is Complete");

        // Completed-quest save still reads Complete after a round trip.
        mid.Write(path);
        var done = SaveState.Read(path);
        if (quest.StatusIn(done.Flags) != QuestStatus.Complete)
            problems.Add("a completed quest should still be Complete after a load");
        if (!done.Flags.GetBool("quest.relic.started"))
            problems.Add("the start flag should survive the load");
    }

    private static SaveState Original()
    {
        var state = new SaveState
        {
            WorldTimeSeconds = 43_200,
            NpcSchedules = new NpcScheduleStore().Set(
                Guid.Parse("8da48350-0472-4f88-a542-f408f7dc06a1"),
                new NpcScheduleRuntimeState
                {
                    CurrentCellId = Guid.Parse("c4a17e31-ae23-4804-9aa8-d69b6a1ce101"),
                    PendingDestinationCellId = Guid.Parse("c4a17e31-ae23-4804-9aa8-d69b6a1ce102"),
                    LastEvaluatedSeconds = 43_100
                }),
            Entities = new[]
            {
                EntityRecord.Create("elder_01", "npc")
                    .WithField("name", "Rowan")
                    .WithField("hp", "12"),
                EntityRecord.Create("gate_01", "prop")
                    .WithField("state", "shut")
            },
            Flags = Flags(),
            Player = new PlayerRecord
            {
                Stats = ExampleStats(),
                Modifiers = new ActorStatModifiers()
                    .Apply(new ActorStatModifier("strength-a", "potion:strength", ActorAttribute.Strength, 2, 30))
                    .Apply(new ActorStatModifier("strength-b", "potion:strength", ActorAttribute.Strength, 3, 45))
            }
        };

        state.ItemDefs.Add(new ItemDef("potion_heal", "Healing Potion", Slot: null, Stackable: true));
        state.ItemDefs.Add(new ItemDef("sword_iron", "Iron Sword", Slot: "mainhand", Stackable: false)
        {
            StatBonuses = new[] { new EquipmentStatBonus(ActorAttribute.Strength, 3) },
            AttachmentBone = "RightHand",
            MeleeAttack = new MeleeAttackProfile(1.5, 7, 0.8)
        });

        // Add, then the save happens around it — this is the sequence the check exists for.
        state.Player.Bag.Add(state.ItemDefs.Get("potion_heal")!, 3);
        state.Player.Bag.Add(state.ItemDefs.Get("sword_iron")!);
        if (!EquipmentSystem.TryEquip(state.Player, state.ItemDefs,
            new ContentId<ItemContentKind>("sword_iron"), out var player))
            throw new InvalidOperationException("The save fixture could not equip the iron sword.");
        state = state with { Player = player };

        return state;
    }

    private static ActorStats ExampleStats() => new()
    {
        Attributes = new ActorAttributes
        {
            Strength = 10,
            Intelligence = 20,
            Willpower = 5,
            Agility = 6,
            Endurance = 8
        },
        Skills = new Dictionary<string, int> { ["Blade"] = 34 }
    };

    private const string ValidContentJson = """
    {
      "Actors": [
        {
          "Id": "actor.elder",
          "Name": "Rowan",
          "FactionId": "faction.mages",
          "Stats": {
            "Attributes": { "Strength": 10, "Intelligence": 20, "Willpower": 5, "Agility": 6, "Endurance": 8 },
            "Skills": { "Blade": 34 }
          }
        }
      ],
      "Items": [ { "Id": "item.potion", "Name": "Healing Potion", "Stackable": true } ],
      "Factions": [ { "Id": "faction.mages", "Name": "Mages" } ],
      "Spells": [
        {
          "Id": "spell.focus",
          "Range": 5,
          "MagickaCost": 12,
          "CooldownSeconds": 2,
          "Attribute": "Strength",
          "Magnitude": 4,
          "DurationSeconds": 4,
          "StackingRule": "ReplaceSameSource"
        }
      ],
      "SkillUseRules": [
        { "ActionId": "combat.melee.hit", "SkillName": "Blade", "UsesPerRank": 2 }
      ],
      "Dialogues": [
        {
          "Id": "dialogue.elder",
          "Nodes": [
            {
              "Id": "greet",
              "Speaker": "Rowan",
              "SpeakerActorId": "actor.elder",
              "Text": "Well met, wanderer.",
              "Options": [
                {
                  "Label": "Continue",
                  "Next": "greet",
                  "RequiresStats": [ { "Attribute": "Strength", "MinimumValue": 10 } ],
                  "RequiresFactions": [ { "FactionId": "faction.mages", "MinimumReputation": 10, "RequiresMembership": true } ]
                }
              ]
            }
          ]
        }
      ],
      "Quests": [
        {
          "Id": "quest.relic",
          "Title": "Find the Relic",
          "StartDialogueId": "dialogue.elder",
          "Stages": [ { "Id": "fetch", "TargetActorId": "actor.elder", "RequiredItemId": "item.potion" } ]
        }
      ]
    }
    """;

    /// <summary>A quest loaded from JSON by the check: two stages, flag-gated completion.</summary>
    private const string ElderQuest = """
    {
      "Quests":
      [
        {
          "Id": "relic",
          "Title": "The Elder's Relic",
          "Stages":
          [
            {
              "Id": "fetch",
              "Journal": "Recover the relic from the old vault.",
              "DoneWhen": [ { "Flag": "relic_taken", "Bool": true } ]
            },
            {
              "Id": "deliver",
              "Journal": "Bring the relic back to the elder.",
              "DoneWhen": [ { "Flag": "relic_delivered", "Bool": true } ]
            }
          ]
        }
      ]
    }
    """;

    /// <summary>A conversation loaded from JSON by the check: one gated option, one flag written.</summary>
    private const string ElderDialogue = """
    {
      "Id": "elder",
      "Nodes":
      [
        {
          "Id": "greet",
          "Speaker": "Rowan",
          "Text": "Well met, wanderer.",
          "Options":
          [
            { "Label": "Who are you?", "Next": "who" },
            {
              "Label": "Ask about the gate.",
              "Next": "gate",
              "Requires": [ { "Flag": "met_elder", "Bool": true } ],
              "RequiresStats": [ { "Attribute": "Strength", "MinimumValue": 10 } ],
              "RequiresFactions": [ { "FactionId": "faction.mages", "MinimumReputation": 10, "RequiresMembership": true } ],
              "Sets": { "gate_topic": true }
            }
          ]
        },
        {
          "Id": "who",
          "Speaker": "Rowan",
          "Text": "Rowan, keeper of this hall.",
          "Options": [ { "Label": "Farewell.", "Next": null } ]
        },
        {
          "Id": "gate",
          "Speaker": "Rowan",
          "Text": "Shut since the winter. Why do you ask?",
          "Options": [ { "Label": "Farewell.", "Next": null } ]
        }
      ]
    }
    """;

    private static FlagStore Flags()
    {
        var flags = new FlagStore();
        flags.Set("met_elder", true);
        flags.Set("gate_locked", false);
        flags.Set("gold", 120);
        flags.Set("moon", 0.5);
        flags.Set("place", "oak_hall");
        return flags;
    }

    private static void Compare(SaveState expected, SaveState actual, List<string> problems)
    {
        if (expected.Version != actual.Version)
            problems.Add($"Version {expected.Version} != {actual.Version}");
        if (expected.WorldTimeSeconds != actual.WorldTimeSeconds)
            problems.Add($"World time {expected.WorldTimeSeconds} != {actual.WorldTimeSeconds}");
        if (expected.NpcSchedules.Entries.Count != actual.NpcSchedules.Entries.Count)
            problems.Add($"NPC schedule count {expected.NpcSchedules.Entries.Count} != {actual.NpcSchedules.Entries.Count}");
        foreach (var entry in expected.NpcSchedules.Entries)
        {
            if (!actual.NpcSchedules.TryGet(entry.WorldInstanceId, out var actualSchedule))
                problems.Add($"NPC schedule for {entry.WorldInstanceId} is missing");
            else if (actualSchedule != entry.State)
                problems.Add($"NPC schedule for {entry.WorldInstanceId} differs after save/load");
        }

        if (expected.Entities.Count != actual.Entities.Count)
        {
            problems.Add($"Entity count {expected.Entities.Count} != {actual.Entities.Count}");
            return;
        }

        for (var i = 0; i < expected.Entities.Count; i++)
        {
            var want = expected.Entities[i];
            var got = actual.Entities[i];

            if (want.Id != got.Id) problems.Add($"Entity {i}: id {want.Id} != {got.Id}");
            if (want.Kind != got.Kind) problems.Add($"Entity {want.Id}: kind {want.Kind} != {got.Kind}");

            if (want.Fields.Count != got.Fields.Count)
            {
                problems.Add($"Entity {want.Id}: {got.Fields.Count} fields, expected {want.Fields.Count}");
                continue;
            }

            foreach (var (name, value) in want.Fields)
            {
                if (!got.Fields.TryGetValue(name, out var other))
                    problems.Add($"Entity {want.Id}: field '{name}' is missing");
                else if (other != value)
                    problems.Add($"Entity {want.Id}: field '{name}' '{value}' != '{other}'");
            }
        }

        if (expected.Flags.Count != actual.Flags.Count)
            problems.Add($"Flag count {expected.Flags.Count} != {actual.Flags.Count}");

        foreach (var (name, value) in expected.Flags.All)
        {
            if (!actual.Flags.TryGet(name, out var got))
                problems.Add($"Flag '{name}' is missing");
            else if (!value.Equals(got))
                problems.Add($"Flag '{name}': {value} != {got}");
        }

        // Item definitions.
        if (expected.ItemDefs.Count != actual.ItemDefs.Count)
            problems.Add($"ItemDef count {expected.ItemDefs.Count} != {actual.ItemDefs.Count}");

        foreach (var (id, def) in expected.ItemDefs.All)
        {
            if (!actual.ItemDefs.TryGet(id, out var got))
                problems.Add($"ItemDef '{id}' is missing");
            else if (!ItemDefinitionsEqual(def, got))
                problems.Add($"ItemDef '{id}': {def} != {got}");
        }

        // The bag, entry for entry — order and counts included.
        var wantBag = expected.Player.Bag.Entries;
        var gotBag = actual.Player.Bag.Entries;
        if (wantBag.Count != gotBag.Count)
        {
            problems.Add($"Bag has {gotBag.Count} entries, expected {wantBag.Count}");
        }
        else
        {
            for (var i = 0; i < wantBag.Count; i++)
            {
                if (wantBag[i].ItemId != gotBag[i].ItemId)
                    problems.Add($"Bag entry {i}: {wantBag[i].ItemId} != {gotBag[i].ItemId}");
                else if (wantBag[i].Count != gotBag[i].Count)
                    problems.Add($"Bag entry {i} ({wantBag[i].ItemId}): count {wantBag[i].Count} != {gotBag[i].Count}");
            }
        }

        // What is worn.
        if (expected.Player.Equip.Count != actual.Player.Equip.Count)
            problems.Add($"Equip has {actual.Player.Equip.Count} slots, expected {expected.Player.Equip.Count}");

        foreach (var (slot, itemId) in expected.Player.Equip.All)
        {
            if (!actual.Player.Equip.Has(slot))
                problems.Add($"Equip slot '{slot}' is missing");
            else if (actual.Player.Equip.Get(slot) != itemId)
                problems.Add($"Equip slot '{slot}': {itemId} != {actual.Player.Equip.Get(slot)}");
        }

        // Where the conversation has got to.
        if (expected.Dialogue.Tree != actual.Dialogue.Tree)
            problems.Add($"Dialogue tree '{expected.Dialogue.Tree}' != '{actual.Dialogue.Tree}'");
        if (expected.Dialogue.Node != actual.Dialogue.Node)
            problems.Add($"Dialogue node '{expected.Dialogue.Node}' != '{actual.Dialogue.Node}'");
    }

    private static bool ItemDefinitionsEqual(ItemDef left, ItemDef right)
    {
        if (left.Id != right.Id || left.Name != right.Name || left.Slot != right.Slot
            || left.Stackable != right.Stackable || left.AttachmentBone != right.AttachmentBone
            || left.MeleeAttack != right.MeleeAttack || left.StatBonuses.Count != right.StatBonuses.Count)
            return false;
        for (var i = 0; i < left.StatBonuses.Count; i++)
            if (left.StatBonuses[i] != right.StatBonuses[i]) return false;
        return true;
    }
}
