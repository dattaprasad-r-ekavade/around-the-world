using Ember.Physics;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NumericsVector3 = System.Numerics.Vector3;

namespace RpgSlice;

/// <summary>Connects RPG records and decisions to live streamed RpgSlice cells and physics queries.</summary>
internal sealed class RpgSliceGameplayIntegration
{
    private static readonly Guid PlayerWorldInstanceId = Guid.Parse("f71f9da0-e2e9-4aae-bf2f-d70a06b07a01");
    private static readonly Guid HomeWorkerNodeId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a01");
    private static readonly Guid HomeBoundaryNodeId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a02");
    private static readonly Guid WorkBoundaryNodeId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a03");
    private static readonly Guid WorkDestinationNodeId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a04");
    private static readonly ContentId<ItemContentKind> AppleId = new("item.rpgslice.apple");
    private static readonly ContentId<ActorContentKind> PlayerActorId = new("actor.rpgslice.player");
    private static readonly ContentId<ActorContentKind> WorkerActorId = new("actor.rpgslice.worker");
    private static readonly ContentId<ActorContentKind> MerchantActorId = new("actor.rpgslice.merchant");
    private static readonly ContentId<ActorContentKind> EnemyActorId = new("actor.rpgslice.raider");
    private static readonly ContentId<QuestContentKind> LostDeliveryQuestId = new("quest.rpgslice.lost_delivery");
    private static readonly Guid LostDeliveryNoticeInstanceId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a14");
    private static readonly Guid LostDeliveryRaiderInstanceId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a12");
    private static readonly Guid LostDeliverySatchelInstanceId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07a13");
    private static readonly Guid NoticeInspectedEventId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07e01");
    private static readonly Guid RaiderDefeatedEventId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07e02");
    private static readonly Guid SatchelCollectedEventId = Guid.Parse("c71f9da0-e2e9-4aae-bf2f-d70a06b07e03");
    private const string NoticeStageId = "read_road_notice";
    private const string RaiderStageId = "defeat_raider";
    private const string SatchelStageId = "recover_apples";
    private const double WorkStartSeconds = 6 * 60 * 60;
    private const double WorkEndSeconds = 18 * 60 * 60;
    private const float WorkerMoveSpeed = 3.2f;
    private const float EnemySightRange = 12f;
    private const float EnemyMoveSpeed = 2.4f;

    private readonly WorldManifest _world;
    private readonly RpgSliceCellStreamer _streamer;
    private readonly WorldPersistenceSession _persistence;
    private readonly PhysicsWorld _physics;
    private readonly RpgContentSet _content;
    private readonly bool _smokeRequested;
    private readonly string _savePath;
    private NpcDailySchedule? _workerSchedule;
    private readonly MeleeAttackProfile _playerAttack = new(2.2, 12, 0.7);
    private readonly MeleeAttackProfile _enemyAttack = new(1.7, 4, 1.4);
    private readonly ActorDef _playerDefinition;
    private readonly ActorDef _workerDefinition;
    private readonly ActorDef _merchantDefinition;
    private readonly ActorDef _enemyDefinition;
    private readonly QuestDef _lostDeliveryQuest;
    private readonly DialogueTree _lostDeliveryDialogue;

    private SaveState _save;
    private WorldClock _clock;
    private CellPathGraph? _homeGraph;
    private CellPathGraph? _workGraph;
    private WorldPathNetwork? _pathNetwork;
    private Guid _homeCellId;
    private Guid _workCellId;
    private Guid _workerCellId;
    private Guid _workerSceneObjectId;
    private WorldInstanceId _workerInstanceId;
    private SceneObject? _workerObject;
    private WorldPathNodeRef _workerNode;
    private WorldPathRoute? _workerRoute;
    private int _routeLegIndex;
    private int _routeNodeIndex;
    private NpcScheduleRuntimeState? _workerScheduleState;
    private Guid _merchantCellId;
    private Guid _merchantSceneObjectId;
    private WorldInstanceId _merchantInstanceId;
    private SceneObject? _merchantObject;
    private Guid _enemyCellId;
    private Guid _enemySceneObjectId;
    private WorldInstanceId _enemyInstanceId;
    private SceneObject? _enemyObject;
    private Guid _noticeCellId;
    private SceneObject? _noticeObject;
    private SceneObject? _satchelObject;
    private EnemyCombatAiMemory _enemyMemory = new();
    private bool _initialized;
    private bool _smokeDepartedForWork;
    private bool _smokeCompleted;
    private WorldClock? _smokeClockOverride;
    private string? _lastStatus;

    public RpgSliceGameplayIntegration(WorldManifest world, RpgSliceCellStreamer streamer,
        WorldPersistenceSession persistence, PhysicsWorld physics, SaveState save, WorldClock clock,
        string savePath, RpgContentSet content, bool smokeRequested)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _savePath = Path.GetFullPath(savePath);
        _smokeRequested = smokeRequested;
        _playerDefinition = GetActor(PlayerActorId);
        _workerDefinition = GetActor(WorkerActorId);
        _merchantDefinition = GetActor(MerchantActorId);
        _enemyDefinition = GetActor(EnemyActorId);
        _lostDeliveryQuest = _content.Quests.Get(LostDeliveryQuestId)
            ?? throw new InvalidDataException($"RPG content pack does not define required quest '{LostDeliveryQuestId.Value}'.");
        _lostDeliveryDialogue = _content.Dialogues.SingleOrDefault(dialogue => dialogue.Id == _lostDeliveryQuest.StartDialogueId)
            ?? throw new InvalidDataException($"RPG quest '{LostDeliveryQuestId.Value}' has no start dialogue.");
    }

    public bool IsInitialized => _initialized;
    public bool SmokeCompleted => _smokeCompleted;
    public string? LastStatus => _lastStatus;
    public QuestStatus LostDeliveryStatus => _lostDeliveryQuest.StatusIn(_save.Flags);
    public QuestStage? LostDeliveryStage => _lostDeliveryQuest.StageIn(_save.Flags);
    public bool HasOpenDialogue => CurrentDialogueNode is not null;
    public DialogueNode? CurrentDialogueNode
    {
        get
        {
            if (_save.Dialogue.Tree != _lostDeliveryDialogue.Id || _save.Dialogue.Node is not { } nodeId)
                return null;
            return _lostDeliveryDialogue.Node(nodeId);
        }
    }
    public IReadOnlyList<DialogueOption> CurrentDialogueOptions => CurrentDialogueNode is { } node
        ? _lostDeliveryDialogue.Available(node.Id, _save.Flags)
        : Array.Empty<DialogueOption>();
    public string LostDeliveryJournal => LostDeliveryStatus switch
    {
        QuestStatus.NotStarted => "Talk to the market keeper about the missing delivery.",
        QuestStatus.Complete => "The Lost Delivery — complete.",
        _ => LostDeliveryStage?.Journal ?? "The Lost Delivery — active."
    };
    public IReadOnlyList<(Guid CellId, Guid SceneObjectId, Guid InstanceId)> LiveActorInstances => !_initialized
        ? Array.Empty<(Guid, Guid, Guid)>()
        : new[]
        {
            (_workerCellId, _workerSceneObjectId, _workerInstanceId.Value),
            (_merchantCellId, _merchantSceneObjectId, _merchantInstanceId.Value),
            (_enemyCellId, _enemySceneObjectId, _enemyInstanceId.Value)
        };

    public static SaveState CreateInitialSave(double worldTimeSeconds, RpgContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var items = new ItemCatalogue();
        foreach (var item in content.Items.All.Values) items.Add(item);
        if (items.Get(AppleId) is null)
            throw new InvalidDataException($"RPG content pack does not define required item '{AppleId.Value}'.");
        var playerBag = new Bag();
        playerBag.Add(items.Get(AppleId)!, 1);
        var actors = new ActorRuntimeStore();
        actors = actors.Set(ActorRuntimeState.Create(PlayerWorldInstanceId,
            content.Actors.Get(PlayerActorId)
                ?? throw new InvalidDataException($"RPG content pack does not define required actor '{PlayerActorId.Value}'.")));
        return new SaveState
        {
            WorldTimeSeconds = worldTimeSeconds,
            ItemDefs = items,
            Player = new PlayerRecord { Currency = 100, Bag = playerBag },
            ActorStates = actors
        };
    }

    public WorldClock Update(float elapsedSeconds, WorldClock clock, Guid playerCellId, Vector3 playerPosition)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        if (!_initialized && !TryInitialize()) return _clock;

        _save = _save with { WorldTimeSeconds = _clock.TotalSeconds };
        TickWorkerSchedule(elapsedSeconds);
        TickEnemy(elapsedSeconds, playerCellId, playerPosition);
        if (_smokeRequested)
        {
            if (!_smokeDepartedForWork && _workerScheduleState?.CurrentCellId == _workCellId
                && _workerScheduleState.PendingDestinationCellId is null && _workerRoute is null)
            {
                _smokeDepartedForWork = true;
                _smokeClockOverride = new WorldClock(WorkEndSeconds + 1);
                Console.WriteLine("RpgSlice: scheduled worker reached the work cell through its authored path; testing its evening return.");
            }
            else if (_smokeDepartedForWork && _workerScheduleState?.CurrentCellId == _homeCellId
                && _workerScheduleState.PendingDestinationCellId is null && _workerRoute is null)
            {
                _smokeCompleted = true;
                Console.WriteLine("RpgSlice: PASS scheduled NPC crossed both streamed cells and returned home without duplicating its runtime instance.");
            }
        }
        if (_smokeClockOverride is { } overrideClock)
        {
            _smokeClockOverride = null;
            _clock = overrideClock;
            return overrideClock;
        }
        return _clock;
    }

    public void WriteSave(WorldClock clock)
    {
        _save = _save with { WorldTimeSeconds = clock.TotalSeconds };
        _save.Write(_savePath);
    }

    public bool TryTradeAt(Guid playerCellId, Vector3 playerPosition, bool sell, out string message)
    {
        message = string.Empty;
        if (!_initialized || playerCellId != _merchantCellId || _merchantObject is null
            || Vector3.Distance(playerPosition, GetWorldPosition(_merchantCellId, _merchantObject)) > 4.5f)
            return false;

        if (!_save.ActorStates.TryGet(_merchantInstanceId.Value, out var merchant))
            throw new InvalidDataException("RpgSlice merchant has no saved actor state.");
        var traded = sell
            ? MerchantTrade.TrySell(_save.Player, merchant, _save.ItemDefs, AppleId, 1, 10,
                out var nextPlayer, out var nextMerchant)
            : MerchantTrade.TryBuy(_save.Player, merchant, _save.ItemDefs, AppleId, 1, 10,
                out nextPlayer, out nextMerchant);
        if (!traded)
        {
            message = sell ? "You do not have an apple to sell" : "The trade could not be completed";
            _lastStatus = message;
            return true;
        }

        _save = _save with
        {
            Player = nextPlayer,
            ActorStates = _save.ActorStates.Set(nextMerchant)
        };
        message = sell ? "Sold one apple for 10 coins" : "Bought one apple for 10 coins";
        _lastStatus = message;
        return true;
    }

    public bool TryInteractAt(Guid playerCellId, Vector3 playerPosition, out string message)
    {
        message = string.Empty;
        if (!_initialized || HasOpenDialogue) return false;

        if (playerCellId == _merchantCellId && _merchantObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_merchantCellId, _merchantObject)) <= 4.5f)
        {
            if (LostDeliveryStatus != QuestStatus.NotStarted)
            {
                message = LostDeliveryStatus == QuestStatus.Complete
                    ? "The market keeper thanks you for recovering the delivery."
                    : LostDeliveryStage?.Journal ?? "The Lost Delivery is underway.";
            }
            else
            {
                _save.Dialogue.Tree = _lostDeliveryDialogue.Id;
                _save.Dialogue.Node = _lostDeliveryDialogue.Nodes[0].Id;
                message = _lostDeliveryDialogue.Nodes[0].Text;
            }
            _lastStatus = message;
            return true;
        }

        if (playerCellId == _noticeCellId && _noticeObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_noticeCellId, _noticeObject)) <= 2.8f)
        {
            if (LostDeliveryStage?.Id == NoticeStageId)
            {
                var applied = QuestEventSystem.Apply(_content.Quests, _save.Flags, new QuestEvent
                {
                    EventId = NoticeInspectedEventId,
                    Kind = QuestEventKind.Interaction,
                    WorldInstanceId = LostDeliveryNoticeInstanceId
                });
                if (applied != 1)
                    throw new InvalidOperationException("Reading the authored delivery notice did not advance its active quest stage.");
                TryCompleteDefeatedRaiderObjective();
                message = "You read the delivery notice.";
            }
            else
            {
                message = LostDeliveryStatus == QuestStatus.NotStarted
                    ? "The notice lists a missing delivery. Speak with the market keeper."
                    : "The notice has no new information for your current objective.";
            }
            _lastStatus = message;
            return true;
        }

        if (playerCellId == _homeCellId && _satchelObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_homeCellId, _satchelObject)) <= 2.8f)
        {
            if (LostDeliveryStage?.Id != SatchelStageId)
            {
                message = "The supply satchel is still guarded by the road raider.";
                _lastStatus = message;
                return true;
            }

            if (!_save.WorldItems.TryGet(LostDeliverySatchelInstanceId, out var satchel)
                || satchel.ItemId != AppleId)
                throw new InvalidDataException("The authored supply satchel is missing its persistent apple stack.");
            if (!InventoryTransfer.TryPickup(LostDeliverySatchelInstanceId, _save.Player.Bag,
                _save.WorldItems, _save.ItemDefs, out var playerBag, out var remainingWorldItems))
            {
                message = "The supply satchel could not be collected.";
                _lastStatus = message;
                return true;
            }
            if (!TryGetActive(_homeCellId, out _, out var homeActive)
                || homeActive is null || !homeActive.Scene.Remove(_satchelObject.Id))
                throw new InvalidOperationException("The collected supply satchel was not present in its active cell.");

            _persistence.Changes.MarkDeleted(_homeCellId,
                new WorldInstanceId(LostDeliverySatchelInstanceId));
            _satchelObject = null;
            _save = _save with
            {
                Player = _save.Player with { Bag = playerBag },
                WorldItems = remainingWorldItems
            };
            var collected = QuestEventSystem.Apply(_content.Quests, _save.Flags, new QuestEvent
            {
                EventId = SatchelCollectedEventId,
                Kind = QuestEventKind.ItemCollected,
                WorldInstanceId = LostDeliverySatchelInstanceId,
                ItemId = AppleId
            });
            if (collected != 1)
                throw new InvalidOperationException("Collecting the supply satchel did not advance its active quest stage.");
            message = "Recovered the apples from the raider's satchel.";
            _lastStatus = message;
            return true;
        }

        return false;
    }

    public bool TrySelectDialogueOption(int optionIndex, out string message)
    {
        message = string.Empty;
        var node = CurrentDialogueNode;
        var options = CurrentDialogueOptions;
        if (node is null || (uint)optionIndex >= (uint)options.Count) return false;

        var dialogueContext = new DialogueContext(_save.Flags, _playerDefinition.Stats);
        _lostDeliveryDialogue.Pick(_save.Dialogue, dialogueContext, options[optionIndex]);
        if (options[optionIndex].Id == "accept")
        {
            if (!_save.Flags.GetBool(_lostDeliveryQuest.StartFlag()))
                throw new InvalidOperationException("Accepting the delivery quest did not set its start flag.");
            message = "Quest started: " + (_lostDeliveryQuest.StageIn(_save.Flags)?.Journal ?? _lostDeliveryQuest.Title);
        }
        else
        {
            message = CurrentDialogueNode?.Text ?? "Conversation ended.";
        }
        _lastStatus = message;
        return true;
    }

    public bool TryPlayerAttack(Guid playerCellId, Vector3 playerPosition, out string message)
    {
        message = string.Empty;
        if (!_initialized || playerCellId != _enemyCellId || _enemyObject is null)
            return false;
        var enemyPosition = GetWorldPosition(_enemyCellId, _enemyObject);
        var distance = Vector3.Distance(playerPosition, enemyPosition);
        if (distance > _playerAttack.Range)
        {
            message = "The raider is out of reach";
            _lastStatus = message;
            return true;
        }
        if (!_save.ActorStates.TryGet(PlayerWorldInstanceId, out var player)
            || !_save.ActorStates.TryGet(_enemyInstanceId.Value, out var enemy))
            throw new InvalidDataException("RpgSlice combatants have no saved actor state.");
        var hit = MeleeCombat.TryAttack(player, enemy, distance, _playerAttack,
            out var nextPlayer, out var nextEnemy);
        if (hit)
        {
            _save = _save with
            {
                ActorStates = _save.ActorStates.Set(nextPlayer).Set(nextEnemy)
            };
            if (!enemy.IsDead && nextEnemy.IsDead)
            {
                TryCompleteDefeatedRaiderObjective();
                message = "Raider defeated";
            }
            else message = nextEnemy.IsDead ? "The raider is already defeated" : "Hit raider";
        }
        else message = player.MeleeCooldownRemaining > 0 ? "Attack is recovering" : "No hit";
        _lastStatus = message;
        return true;
    }

    private void TryCompleteDefeatedRaiderObjective()
    {
        if (LostDeliveryStage?.Id != RaiderStageId
            || !_save.ActorStates.TryGet(LostDeliveryRaiderInstanceId, out var raider)
            || !raider.IsDead)
            return;

        var applied = QuestEventSystem.Apply(_content.Quests, _save.Flags, new QuestEvent
        {
            EventId = RaiderDefeatedEventId,
            Kind = QuestEventKind.ActorKilled,
            WorldInstanceId = LostDeliveryRaiderInstanceId,
            ActorId = EnemyActorId
        });
        if (applied != 1)
            throw new InvalidOperationException("The defeated authored raider did not advance its active quest stage.");
        _lastStatus = "The road is clear. Recover the apples from the raider's satchel.";
    }

    public string? GetPrompt(Guid playerCellId, Vector3 playerPosition)
    {
        if (!_initialized) return null;
        if (HasOpenDialogue)
        {
            var labels = CurrentDialogueOptions.Select((option, index) => $"[{index + 1}] {option.Label}");
            return string.Join("   ", labels);
        }
        if (playerCellId == _merchantCellId && _merchantObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_merchantCellId, _merchantObject)) <= 4.5f)
            return LostDeliveryStatus == QuestStatus.NotStarted
                ? "E talk to the market keeper"
                : "T buy / Y sell an apple";
        if (playerCellId == _noticeCellId && _noticeObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_noticeCellId, _noticeObject)) <= 2.8f)
            return LostDeliveryStage?.Id == NoticeStageId ? "E read the delivery notice" : "The delivery notice is here";
        if (playerCellId == _homeCellId && _satchelObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_homeCellId, _satchelObject)) <= 2.8f)
            return LostDeliveryStage?.Id == SatchelStageId
                ? "E recover the apples"
                : "The satchel is guarded by the raider";
        if (playerCellId == _enemyCellId && _enemyObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_enemyCellId, _enemyObject)) <= _playerAttack.Range)
        {
            if (!_save.ActorStates.TryGet(_enemyInstanceId.Value, out var enemyState) || !enemyState.IsDead)
                return "F attack the road raider";
            return "The road raider is defeated";
        }
        return null;
    }

    public bool TryGetQuestSmokeTarget(string targetName, out Vector3 position)
    {
        position = Vector3.Zero;
        if (!_initialized) return false;
        var target = targetName switch
        {
            "keeper" => (_merchantCellId, _merchantObject),
            "notice" => (_noticeCellId, _noticeObject),
            "raider" => (_enemyCellId, _enemyObject),
            "satchel" => (_homeCellId, _satchelObject),
            _ => (Guid.Empty, (SceneObject?)null)
        };
        if (target.Item2 is null || !TryGetActive(target.Item1, out _, out _)) return false;
        position = GetWorldPosition(target.Item1, target.Item2);
        return true;
    }

    public bool IsLostDeliveryRaiderDead => _initialized
        && _save.ActorStates.TryGet(_enemyInstanceId.Value, out var state) && state.IsDead;

    public bool IsLostDeliverySatchelPresent => _initialized && _satchelObject is not null
        && _save.WorldItems.TryGet(LostDeliverySatchelInstanceId, out _);

    public int PlayerAppleCount => _save.Player.Bag.Count(AppleId);

    private bool TryInitialize()
    {
        if (!_world.TryGetExterior(new ExteriorCellCoordinate(0, 0), out var home)
            || home is null
            || !_world.TryGetExterior(new ExteriorCellCoordinate(1, 0), out var work)
            || work is null)
        {
            if (_smokeRequested)
                throw new InvalidDataException("RpgSlice gameplay integration requires exterior cells (0, 0) and (1, 0).");
            return false;
        }
        _homeCellId = home.Id;
        _workCellId = work.Id;
        _workerSchedule = new NpcDailySchedule(_homeCellId, _workCellId, WorkStartSeconds, WorkEndSeconds);
        if (!TryGetActive(_homeCellId, out _, out var homeActive)
            || !TryGetActive(_workCellId, out _, out var workActive)) return false;

        var pathRoot = Path.Combine(_world.RootDirectory, "Paths");
        _homeGraph = CellPathGraphFile.Load(Path.Combine(pathRoot, "Exterior_0_0.paths.json"));
        _workGraph = CellPathGraphFile.Load(Path.Combine(pathRoot, "Exterior_1_0.paths.json"));
        _pathNetwork = WorldPathNetworkFile.Load(Path.Combine(pathRoot, "world-paths.json"),
            new[] { _homeGraph!, _workGraph! });
        if (_homeGraph.CellId != _homeCellId || _workGraph.CellId != _workCellId)
            throw new InvalidDataException("RpgSlice authored path graphs do not match the active world manifest cells.");

        EnsureItemDefinition();
        EnsurePlayerActor();
        var workerEntry = FindRuntimeObject("Scheduled Worker");
        if (workerEntry is null)
        {
            var identity = _persistence.Spawn(_homeCellId, homeActive!.Scene,
                new SceneObject(Guid.NewGuid(), "Scheduled Worker")
                {
                    Transform = new Transform { Position = NodePosition(_homeGraph, HomeWorkerNodeId), Scale = new Vector3(0.7f, 1.6f, 0.7f) }
                });
            workerEntry = _persistence.RuntimeObjects.ExportSnapshot()
                .Single(entry => entry.InstanceId == identity.InstanceId);
        }
        _workerCellId = workerEntry.CellId;
        _workerSceneObjectId = workerEntry.SceneObjectId;
        _workerInstanceId = workerEntry.InstanceId;
        if (!TryGetActive(_workerCellId, out _, out var workerActive)) return false;
        _workerObject = workerActive!.Scene.Find(_workerSceneObjectId)
            ?? throw new InvalidDataException("Scheduled worker runtime object is not restored into its active cell.");
        _workerNode = FindNearestNode(_workerCellId, _workerObject.Transform.Position);
        var schedule = _save.NpcSchedules;
        if (!schedule.TryGet(_workerInstanceId.Value, out _workerScheduleState!))
        {
            _workerScheduleState = new NpcScheduleRuntimeState
            {
                CurrentCellId = _workerCellId,
                LastEvaluatedSeconds = _clock.TotalSeconds
            };
        }
        if (_workerScheduleState.CurrentCellId != _workerCellId)
            _workerScheduleState = _workerScheduleState.PendingDestinationCellId.HasValue
                ? NpcScheduleSystem.RecordCellEntered(_workerScheduleState, _workerCellId)
                : _workerScheduleState with { CurrentCellId = _workerCellId };
        if (!_save.ActorStates.TryGet(_workerInstanceId.Value, out _))
            SetActor(ActorRuntimeState.Create(_workerInstanceId.Value, _workerDefinition));

        EnsureMerchant(homeActive!);
        EnsureEnemy(homeActive!);
        EnsureQuestPlacements(homeActive!, workActive!);
        if (_workerScheduleState.PendingDestinationCellId is { } pending)
            BeginWorkerRoute(pending);
        _initialized = true;
        _save = _save with
        {
            WorldTimeSeconds = _clock.TotalSeconds,
            NpcSchedules = _save.NpcSchedules.Set(_workerInstanceId.Value, _workerScheduleState)
        };
        TryCompleteDefeatedRaiderObjective();
        if (_smokeRequested)
        {
            RunMerchantSmoke();
            RunEnemySmoke();
            Console.WriteLine("RpgSlice: RPG integration smoke started (scheduled worker, merchant, and road raider).");
        }
        return true;
    }

    private void TickWorkerSchedule(float elapsedSeconds)
    {
        var state = _workerScheduleState
            ?? throw new InvalidOperationException("Scheduled worker was not initialized.");
        var decision = NpcScheduleSystem.Evaluate(_workerSchedule!, _clock, state);
        var pendingChanged = state.PendingDestinationCellId != decision.State.PendingDestinationCellId;
        _workerScheduleState = decision.State;
        if (pendingChanged && !decision.NewTravelRequestCellId.HasValue)
            _workerRoute = null;
        if (decision.NewTravelRequestCellId is { } destination)
            BeginWorkerRoute(destination);
        _save = _save with { NpcSchedules = _save.NpcSchedules.Set(_workerInstanceId.Value, _workerScheduleState) };
        if (_workerRoute is null) return;

        var route = _workerRoute;
        if (_routeLegIndex >= route.Legs.Count)
            throw new InvalidOperationException("Scheduled worker route advanced beyond its final leg.");
        var leg = route.Legs[_routeLegIndex];
        if (_routeNodeIndex < leg.NodeIds.Count)
        {
            var target = NodePosition(leg.CellId, leg.NodeIds[_routeNodeIndex]);
            var current = _workerObject!.Transform.Position;
            var offset = target - current;
            var distance = offset.Length();
            var step = WorkerMoveSpeed * elapsedSeconds;
            if (distance <= MathF.Max(step, 0.025f))
            {
                MoveWorker(target);
                _workerNode = new WorldPathNodeRef(leg.CellId, leg.NodeIds[_routeNodeIndex]);
                _routeNodeIndex++;
            }
            else if (distance > 1e-5f)
            {
                MoveWorker(current + offset * (step / distance));
            }
        }
        if (_routeNodeIndex < leg.NodeIds.Count) return;

        if (leg.TransitionToNext is { } transition)
        {
            if (!TryGetActive(transition.From.CellId, out _, out var source)
                || !TryGetActive(transition.To.CellId, out var destinationOperation, out _)) return;
            var destinationPosition = NodePosition(transition.To.CellId, transition.To.NodeId);
            var destinationTransform = CopyTransform(_workerObject!.Transform, destinationPosition);
            var transfer = WorldNpcCellTransition<RpgSliceCellStreamer.PreparedCell,
                RpgSliceCellStreamer.ActiveCell>.ReuseActiveDestination(
                    transition, _workerInstanceId, _persistence.RuntimeObjects, _persistence.Identities,
                    source!.Scene, destinationOperation!, active => active.Scene, destinationTransform);
            if (!transfer.Tick())
                throw new InvalidOperationException("Scheduled worker cell transfer failed.", transfer.Failure);
            _workerCellId = transition.To.CellId;
            _workerObject = destinationOperation!.ActiveResources!.Scene.Find(_workerSceneObjectId)
                ?? throw new InvalidOperationException("Transferred scheduled worker is missing from the destination scene.");
            _workerNode = transition.To;
            _workerScheduleState = NpcScheduleSystem.RecordCellEntered(_workerScheduleState, _workerCellId);
            _save = _save with { NpcSchedules = _save.NpcSchedules.Set(_workerInstanceId.Value, _workerScheduleState) };
            _routeLegIndex++;
            _routeNodeIndex = 1;
            return;
        }

        _workerScheduleState = NpcScheduleSystem.CompleteTravel(_workerScheduleState, leg.CellId);
        _workerNode = new WorldPathNodeRef(leg.CellId, leg.NodeIds[^1]);
        _workerRoute = null;
        _save = _save with { NpcSchedules = _save.NpcSchedules.Set(_workerInstanceId.Value, _workerScheduleState) };
    }

    private void BeginWorkerRoute(Guid destinationCellId)
    {
        var destinationNodeId = destinationCellId == _homeCellId
            ? HomeWorkerNodeId
            : destinationCellId == _workCellId
                ? WorkDestinationNodeId
                : throw new InvalidDataException($"Worker schedule references unsupported cell {destinationCellId}.");
        var route = WorldRouteSearch.FindShortestRoute(_pathNetwork!, _workerNode,
            new WorldPathNodeRef(destinationCellId, destinationNodeId), requiredClearanceRadius: 0.4f)
            ?? throw new InvalidDataException($"No authored path connects the worker to scheduled cell {destinationCellId}.");
        _workerRoute = route;
        _routeLegIndex = 0;
        _routeNodeIndex = route.Legs[0].NodeIds.Count > 1 ? 1 : route.Legs[0].NodeIds.Count;
    }

    private void EnsureMerchant(RpgSliceCellStreamer.ActiveCell homeActive)
    {
        var entry = FindRuntimeObject("RpgSlice Merchant");
        var authored = FindAuthoredActor(homeActive, "RpgSlice Merchant", MerchantActorId);
        if (entry is not null && authored is not null)
        {
            RemoveLegacyRuntimeRole(entry, homeActive, authored, _merchantDefinition);
            entry = null;
        }
        if (entry is null)
        {
            if (authored is not null)
            {
                _merchantCellId = _homeCellId;
                _merchantSceneObjectId = authored.Id;
                _merchantInstanceId = _persistence.Identities.GetOrCreate(_homeCellId, authored);
                _merchantObject = authored;
            }
            else
            {
                var identity = _persistence.Spawn(_homeCellId, homeActive.Scene,
                    new SceneObject(Guid.NewGuid(), "RpgSlice Merchant")
                    {
                        Transform = new Transform { Position = new Vector3(20f, 1.1f, 23f), Scale = new Vector3(1f, 1.8f, 1f) }
                    });
                entry = _persistence.RuntimeObjects.ExportSnapshot().Single(item => item.InstanceId == identity.InstanceId);
            }
        }
        if (entry is not null)
        {
            _merchantCellId = entry.CellId;
            _merchantSceneObjectId = entry.SceneObjectId;
            _merchantInstanceId = entry.InstanceId;
            if (TryGetActive(_merchantCellId, out _, out var active))
                _merchantObject = active!.Scene.Find(_merchantSceneObjectId);
        }
        if (_merchantObject is null) throw new InvalidDataException("RpgSlice merchant is not in an active cell.");
        if (!_save.ActorStates.TryGet(_merchantInstanceId.Value, out _))
        {
            var stock = new Bag();
            stock.Add(_save.ItemDefs.Get(AppleId)!, 4);
            SetActor(ActorRuntimeState.Create(_merchantInstanceId.Value, _merchantDefinition, stock) with { Currency = 100 });
        }
    }

    private void EnsureEnemy(RpgSliceCellStreamer.ActiveCell homeActive)
    {
        var entry = FindRuntimeObject("RpgSlice Road Raider");
        var authored = FindAuthoredActor(homeActive, "RpgSlice Road Raider", EnemyActorId);
        if (entry is not null && authored is not null)
        {
            RemoveLegacyRuntimeRole(entry, homeActive, authored, _enemyDefinition);
            entry = null;
        }
        if (entry is null)
        {
            if (authored is not null)
            {
                _enemyCellId = _homeCellId;
                _enemySceneObjectId = authored.Id;
                _enemyInstanceId = _persistence.Identities.GetOrCreate(_homeCellId, authored);
                _enemyObject = authored;
            }
            else
            {
                var identity = _persistence.Spawn(_homeCellId, homeActive.Scene,
                    new SceneObject(Guid.NewGuid(), "RpgSlice Road Raider")
                    {
                        Transform = new Transform { Position = new Vector3(26f, 1.1f, 23f), Scale = new Vector3(0.8f, 1.7f, 0.8f) }
                    });
                entry = _persistence.RuntimeObjects.ExportSnapshot().Single(item => item.InstanceId == identity.InstanceId);
            }
        }
        if (entry is not null)
        {
            _enemyCellId = entry.CellId;
            _enemySceneObjectId = entry.SceneObjectId;
            _enemyInstanceId = entry.InstanceId;
            if (TryGetActive(_enemyCellId, out _, out var active))
                _enemyObject = active!.Scene.Find(_enemySceneObjectId);
        }
        if (_enemyObject is null) throw new InvalidDataException("RpgSlice road raider is not in an active cell.");
        if (!_save.ActorStates.TryGet(_enemyInstanceId.Value, out _))
            SetActor(ActorRuntimeState.Create(_enemyInstanceId.Value, _enemyDefinition));
        if (authored is not null && _enemyInstanceId.Value != LostDeliveryRaiderInstanceId)
            throw new InvalidDataException("The authored raider instance ID does not match the Lost Delivery quest target.");
    }

    private void EnsureQuestPlacements(RpgSliceCellStreamer.ActiveCell homeActive,
        RpgSliceCellStreamer.ActiveCell workActive)
    {
        _noticeCellId = _workCellId;
        _noticeObject = workActive.Scene.Objects.SingleOrDefault(sceneObject =>
            sceneObject.WorldEntity?.InstanceId == LostDeliveryNoticeInstanceId);
        if (_noticeObject is null) return;
        if (_noticeObject.WorldEntity is not { Kind: WorldEntityKind.Item, DefinitionId: "item.rpgslice.delivery_notice" })
            throw new InvalidDataException("The east-road cell must contain the authored Lost Delivery notice placement.");
        if (_persistence.Identities.GetOrCreate(_noticeCellId, _noticeObject).Value != LostDeliveryNoticeInstanceId)
            throw new InvalidDataException("The delivery notice does not retain its authored world-instance ID.");

        var satchelIdentity = new WorldInstanceId(LostDeliverySatchelInstanceId);
        var wasCollected = _persistence.Changes.IsDeleted(_homeCellId, satchelIdentity);
        _satchelObject = homeActive.Scene.Objects.SingleOrDefault(sceneObject =>
            sceneObject.WorldEntity?.InstanceId == LostDeliverySatchelInstanceId);
        if (wasCollected)
        {
            if (_satchelObject is not null || _save.WorldItems.TryGet(LostDeliverySatchelInstanceId, out _))
                throw new InvalidDataException("The saved supply satchel deletion conflicts with the RPG world-item save.");
            return;
        }

        if (_satchelObject?.WorldEntity is not { Kind: WorldEntityKind.Item, DefinitionId: "item.rpgslice.apple" })
            throw new InvalidDataException("The market cell must contain the authored Lost Delivery supply satchel.");
        if (_persistence.Identities.GetOrCreate(_homeCellId, _satchelObject).Value != LostDeliverySatchelInstanceId)
            throw new InvalidDataException("The supply satchel does not retain its authored world-instance ID.");
        if (_save.WorldItems.TryGet(LostDeliverySatchelInstanceId, out var savedSatchel))
        {
            if (savedSatchel.ItemId != AppleId || savedSatchel.Count < 1)
                throw new InvalidDataException("The saved supply satchel does not contain its authored apple item.");
            return;
        }
        if (!_save.WorldItems.TryAdd(new WorldItemEntry(LostDeliverySatchelInstanceId, AppleId, 1),
            out var seededWorldItems))
            throw new InvalidDataException("The initial supply satchel could not be added to the RPG world-item store.");
        _save = _save with { WorldItems = seededWorldItems };
    }

    private void TickEnemy(float elapsedSeconds, Guid playerCellId, Vector3 playerPosition)
    {
        if (playerCellId != _enemyCellId || _enemyObject is null
            || !_save.ActorStates.TryGet(_enemyInstanceId.Value, out var enemy)
            || !_save.ActorStates.TryGet(PlayerWorldInstanceId, out var player)) return;
        enemy = enemy.AdvanceTime(elapsedSeconds);
        player = player.AdvanceTime(elapsedSeconds);
        var enemyPosition = GetWorldPosition(_enemyCellId, _enemyObject);
        var perception = ActorPerception.Evaluate(_physics, enemyPosition, playerPosition, EnemySightRange);
        var decision = EnemyCombatAi.Tick(_enemyMemory, enemy, player,
            ToNumerics(enemyPosition), ToNumerics(playerPosition), perception.CanSee, _enemyAttack);
        _enemyMemory = decision.Memory;
        _save = _save with { ActorStates = _save.ActorStates.Set(decision.Enemy).Set(decision.Target ?? player) };
        if (decision.DesiredMoveDirection == NumericsVector3.Zero || elapsedSeconds <= 0) return;

        var direction = new Vector3(decision.DesiredMoveDirection.X, 0f, decision.DesiredMoveDirection.Z);
        var distance = MathF.Min(EnemyMoveSpeed * elapsedSeconds, EnemyMoveSpeed * 0.05f);
        if (distance <= 0 || _physics.Raycast(enemyPosition, direction, distance + 0.35f, PhysicsCollisionLayer.World) is not null)
            return;
        var nextWorld = enemyPosition + direction * distance;
        if (!TryGetActive(_enemyCellId, out _, out var active)) return;
        var local = Vector3.Transform(nextWorld, Matrix.Invert(active!.WorldTransform));
        _persistence.SetTransform(_enemyCellId, _enemyObject,
            CopyTransform(_enemyObject.Transform, local));
    }

    private void RunMerchantSmoke()
    {
        _save.ActorStates.TryGet(_merchantInstanceId.Value, out var merchant);
        var broke = MerchantTrade.TryBuy(_save.Player with { Currency = 0 }, merchant,
            _save.ItemDefs, AppleId, 1, 10, out var unchangedPlayer, out var unchangedMerchant);
        var empty = MerchantTrade.TryBuy(_save.Player,
            merchant with { Inventory = new Bag() }, _save.ItemDefs, AppleId, 1, 10,
            out _, out _);
        if (broke || empty || unchangedPlayer.Currency != 0
            || unchangedMerchant.Currency != merchant.Currency)
            throw new InvalidOperationException("RpgSlice merchant smoke did not preserve state after invalid trades.");
        var bought = MerchantTrade.TryBuy(_save.Player, merchant, _save.ItemDefs, AppleId, 2, 10,
            out var playerAfterBuy, out var merchantAfterBuy);
        if (!bought)
            throw new InvalidOperationException("RpgSlice merchant smoke could not buy available stock.");
        var sold = MerchantTrade.TrySell(playerAfterBuy, merchantAfterBuy,
            _save.ItemDefs, AppleId, 1, 10, out var playerAfterSell, out var merchantAfterSell);
        if (!sold || playerAfterSell.Currency != _save.Player.Currency - 10
            || playerAfterSell.Bag.Count(AppleId) != _save.Player.Bag.Count(AppleId) + 1
            || merchantAfterSell.Currency != merchant.Currency + 10)
            throw new InvalidOperationException("RpgSlice merchant smoke failed to apply buy and sell results atomically.");
        _save = _save with
        {
            Player = playerAfterSell,
            ActorStates = _save.ActorStates.Set(merchantAfterSell)
        };
        var reopened = SaveState.FromJson(_save.ToJson());
        if (reopened.Player.Currency != playerAfterSell.Currency
            || !reopened.ActorStates.TryGet(_merchantInstanceId.Value, out var restoredMerchant)
            || restoredMerchant.Currency != merchantAfterSell.Currency
            || restoredMerchant.Inventory.Count(AppleId) != merchantAfterSell.Inventory.Count(AppleId))
            throw new InvalidOperationException("RpgSlice merchant inventory and currency did not survive the RPG save round trip.");
        _save = reopened;
        Console.WriteLine("RpgSlice: PASS merchant buy/sell, insufficient funds, empty stock, and RPG save round trip.");
    }

    private void RunEnemySmoke()
    {
        var origin = new Vector3(-16f, 10f, -100f);
        var target = new Vector3(16f, 10f, -100f);
        var clear = ActorPerception.Evaluate(_physics, origin, target, 40f);
        var wall = _physics.AddStaticBox(new Vector3(0f, 10f, -100f), new Vector3(1f, 3f, 3f));
        ActorPerceptionResult blocked;
        try { blocked = ActorPerception.Evaluate(_physics, origin, target, 40f); }
        finally { _physics.RemoveStatic(wall); }
        if (!clear.CanSee || blocked.CanSee)
            throw new InvalidOperationException("RpgSlice enemy smoke did not use physics line of sight correctly.");

        var smokeEnemy = ActorRuntimeState.Create(Guid.NewGuid(), _enemyDefinition);
        var smokePlayer = ActorRuntimeState.Create(Guid.NewGuid(), _playerDefinition);
        var blockedDecision = EnemyCombatAi.Tick(new EnemyCombatAiMemory(), smokeEnemy, smokePlayer,
            ToNumerics(origin), ToNumerics(target), blocked.CanSee, _enemyAttack);
        var chaseDecision = EnemyCombatAi.Tick(blockedDecision.Memory, smokeEnemy, smokePlayer,
            new NumericsVector3(8f, 10f, -100f), new NumericsVector3(0f, 10f, -100f), true, _enemyAttack);
        var attackDecision = EnemyCombatAi.Tick(chaseDecision.Memory, smokeEnemy, smokePlayer,
            new NumericsVector3(1f, 10f, -100f), new NumericsVector3(0f, 10f, -100f), true, _enemyAttack);
        var deadEnemy = attackDecision.Enemy with { CurrentHealth = 0, IsDead = true };
        var deadDecision = EnemyCombatAi.Tick(attackDecision.Memory, deadEnemy, attackDecision.Target,
            new NumericsVector3(1f, 10f, -100f), new NumericsVector3(0f, 10f, -100f), true, _enemyAttack);
        if (blockedDecision.Memory.State != EnemyCombatAiState.Idle
            || chaseDecision.Memory.State != EnemyCombatAiState.Chasing
            || chaseDecision.DesiredMoveDirection.LengthSquared() < 0.99f
            || !attackDecision.AttackLanded || attackDecision.Target?.CurrentHealth >= smokePlayer.CurrentHealth
            || deadDecision.Memory.State != EnemyCombatAiState.Dead || deadDecision.AttackLanded)
            throw new InvalidOperationException("RpgSlice enemy smoke did not cover blocked sight, chase, attack, and death.");
        Console.WriteLine("RpgSlice: PASS physics sight obstruction and Ember.Rpg idle/chase/attack/dead decisions.");
    }

    private bool TryGetActive(Guid cellId,
        out WorldCellLoadOperation<RpgSliceCellStreamer.PreparedCell, RpgSliceCellStreamer.ActiveCell>? operation,
        out RpgSliceCellStreamer.ActiveCell? active) =>
        _streamer.TryGetActiveCell(cellId, out operation, out active);

    private WorldRuntimeObjectEntry? FindRuntimeObject(string name) =>
        _persistence.RuntimeObjects.ExportSnapshot().FirstOrDefault(entry => entry.SceneObject.Name == name);

    private SceneObject? FindAuthoredActor(RpgSliceCellStreamer.ActiveCell active, string name,
        ContentId<ActorContentKind> definitionId)
    {
        var namedObjects = active.Scene.Objects
            .Where(item => item.Name == name && item.WorldEntity is not null).ToArray();
        if (namedObjects.Length > 1)
            throw new InvalidDataException($"Cell {_homeCellId} contains multiple actors named '{name}'.");
        if (namedObjects.Length == 0 || namedObjects[0].WorldEntity is null) return null;
        var placement = namedObjects[0].WorldEntity!;
        if (placement.Kind != WorldEntityKind.Actor || placement.DefinitionId != definitionId.Value)
            throw new InvalidDataException($"Authored actor '{name}' must reference '{definitionId.Value}'.");
        return namedObjects[0];
    }

    private void RemoveLegacyRuntimeRole(WorldRuntimeObjectEntry entry,
        RpgSliceCellStreamer.ActiveCell active, SceneObject authored, ActorDef definition)
    {
        if (entry.CellId != _homeCellId || active.Scene.Find(entry.SceneObjectId) is null
            || !active.Scene.Remove(entry.SceneObjectId)
            || !_persistence.RuntimeObjects.Remove(entry.CellId, entry.InstanceId, _persistence.Identities))
            throw new InvalidDataException($"Could not replace legacy runtime actor '{entry.SceneObject.Name}' with its authored placement.");

        var authoredInstanceId = authored.WorldEntity!.InstanceId;
        if (_save.ActorStates.TryGet(entry.InstanceId.Value, out var previousState))
        {
            var entries = _save.ActorStates.Entries
                .Where(state => state.WorldInstanceId != entry.InstanceId.Value
                    && state.WorldInstanceId != authoredInstanceId)
                .ToList();
            entries.Add(previousState with { WorldInstanceId = authoredInstanceId, ActorId = definition.Id });
            _save = _save with { ActorStates = new ActorRuntimeStore { Entries = entries } };
        }
    }

    private void EnsureItemDefinition()
    {
        if (_save.ItemDefs.Get(AppleId) is null)
            _save.ItemDefs.Add(new ItemDef(AppleId, "Apple", slot: null, stackable: true));
    }

    private void EnsurePlayerActor()
    {
        if (!_save.ActorStates.TryGet(PlayerWorldInstanceId, out _))
            SetActor(ActorRuntimeState.Create(PlayerWorldInstanceId, _playerDefinition));
    }

    private void SetActor(ActorRuntimeState actor) =>
        _save = _save with { ActorStates = _save.ActorStates.Set(actor) };

    private WorldPathNodeRef FindNearestNode(Guid cellId, Vector3 position)
    {
        var graph = GetGraph(cellId);
        var nearest = graph.Nodes
            .OrderBy(node => Vector3.DistanceSquared(position,
                new Vector3(node.Position.X, node.Position.Y, node.Position.Z)))
            .FirstOrDefault()
            ?? throw new InvalidDataException($"Path graph for cell {cellId} has no nodes.");
        return new WorldPathNodeRef(cellId, nearest.Id);
    }

    private CellPathGraph GetGraph(Guid cellId) => cellId == _homeCellId
        ? _homeGraph!
        : cellId == _workCellId
            ? _workGraph!
            : throw new InvalidDataException($"No RpgSlice integration path graph exists for cell {cellId}.");

    private Vector3 NodePosition(Guid cellId, Guid nodeId)
    {
        var graph = GetGraph(cellId);
        var node = graph.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId)
            ?? throw new InvalidDataException($"Path node {nodeId} is missing in cell {cellId}.");
        return new Vector3(node.Position.X, node.Position.Y, node.Position.Z);
    }

    private Vector3 NodePosition(CellPathGraph graph, Guid nodeId)
    {
        var node = graph.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId)
            ?? throw new InvalidDataException($"Path node {nodeId} is missing in cell {graph.CellId}.");
        return new Vector3(node.Position.X, node.Position.Y, node.Position.Z);
    }

    private Vector3 GetWorldPosition(Guid cellId, SceneObject sceneObject)
    {
        if (!TryGetActive(cellId, out _, out var active))
            throw new InvalidOperationException($"RpgSlice actor cell {cellId} is not active.");
        var world = active!.Scene.GetWorldMatrix(sceneObject.Id) * active.WorldTransform;
        return new Vector3(world.M41, world.M42, world.M43);
    }

    private void MoveWorker(Vector3 position)
    {
        if (!TryGetActive(_workerCellId, out _, out var active)) return;
        _workerObject = active!.Scene.Find(_workerSceneObjectId)
            ?? throw new InvalidOperationException("Scheduled worker left its active scene without a cell transition.");
        _persistence.SetTransform(_workerCellId, _workerObject,
            CopyTransform(_workerObject.Transform, position));
    }

    private static Transform CopyTransform(Transform original, Vector3 position) => new()
    {
        Position = position,
        Rotation = original.Rotation,
        Scale = original.Scale
    };

    private static NumericsVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);

    private ActorDef GetActor(ContentId<ActorContentKind> id) => _content.Actors.Get(id)
        ?? throw new InvalidDataException($"RPG content pack does not define required actor '{id.Value}'.");
}
