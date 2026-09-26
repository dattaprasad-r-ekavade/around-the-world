using Ember.Physics;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using System;
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
    private const double WorkStartSeconds = 6 * 60 * 60;
    private const double WorkEndSeconds = 18 * 60 * 60;
    private const float WorkerMoveSpeed = 3.2f;
    private const float EnemySightRange = 12f;
    private const float EnemyMoveSpeed = 2.4f;

    private readonly WorldManifest _world;
    private readonly RpgSliceCellStreamer _streamer;
    private readonly WorldPersistenceSession _persistence;
    private readonly PhysicsWorld _physics;
    private readonly bool _smokeRequested;
    private readonly string _savePath;
    private NpcDailySchedule? _workerSchedule;
    private readonly MeleeAttackProfile _playerAttack = new(2.2, 12, 0.7);
    private readonly MeleeAttackProfile _enemyAttack = new(1.7, 4, 1.4);
    private readonly ActorDef _workerDefinition = CreateActorDefinition(WorkerActorId, "Scheduled Worker");
    private readonly ActorDef _merchantDefinition = CreateActorDefinition(MerchantActorId, "Market Keeper");
    private readonly ActorDef _enemyDefinition = CreateActorDefinition(EnemyActorId, "Road Raider");

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
    private EnemyCombatAiMemory _enemyMemory = new();
    private bool _initialized;
    private bool _smokeDepartedForWork;
    private bool _smokeCompleted;
    private WorldClock? _smokeClockOverride;
    private string? _lastStatus;

    public RpgSliceGameplayIntegration(WorldManifest world, RpgSliceCellStreamer streamer,
        WorldPersistenceSession persistence, PhysicsWorld physics, SaveState save, WorldClock clock,
        string savePath, bool smokeRequested)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _savePath = Path.GetFullPath(savePath);
        _smokeRequested = smokeRequested;
    }

    public bool IsInitialized => _initialized;
    public bool SmokeCompleted => _smokeCompleted;
    public string? LastStatus => _lastStatus;

    public static SaveState CreateInitialSave(double worldTimeSeconds)
    {
        var items = new ItemCatalogue();
        items.Add(new ItemDef(AppleId, "Apple", slot: null, stackable: true));
        var playerBag = new Bag();
        playerBag.Add(items.Get(AppleId)!, 1);
        var actors = new ActorRuntimeStore();
        actors = actors.Set(ActorRuntimeState.Create(PlayerWorldInstanceId,
            CreateActorDefinition(PlayerActorId, "Player")));
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
            message = nextEnemy.IsDead ? "Raider defeated" : "Hit raider";
        }
        else message = player.MeleeCooldownRemaining > 0 ? "Attack is recovering" : "No hit";
        _lastStatus = message;
        return true;
    }

    public string? GetPrompt(Guid playerCellId, Vector3 playerPosition)
    {
        if (!_initialized) return null;
        if (playerCellId == _merchantCellId && _merchantObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_merchantCellId, _merchantObject)) <= 4.5f)
            return "T buy / Y sell an apple";
        if (playerCellId == _enemyCellId && _enemyObject is not null
            && Vector3.Distance(playerPosition, GetWorldPosition(_enemyCellId, _enemyObject)) <= _playerAttack.Range)
            return "F attack the road raider";
        return null;
    }

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
            || !TryGetActive(_workCellId, out _, out _)) return false;

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
        if (_workerScheduleState.PendingDestinationCellId is { } pending)
            BeginWorkerRoute(pending);
        _initialized = true;
        _save = _save with
        {
            WorldTimeSeconds = _clock.TotalSeconds,
            NpcSchedules = _save.NpcSchedules.Set(_workerInstanceId.Value, _workerScheduleState)
        };
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
        if (entry is null)
        {
            var identity = _persistence.Spawn(_homeCellId, homeActive.Scene,
                new SceneObject(Guid.NewGuid(), "RpgSlice Merchant")
                {
                    Transform = new Transform { Position = new Vector3(20f, 1.1f, 23f), Scale = new Vector3(1f, 1.8f, 1f) }
                });
            entry = _persistence.RuntimeObjects.ExportSnapshot().Single(item => item.InstanceId == identity.InstanceId);
        }
        _merchantCellId = entry.CellId;
        _merchantSceneObjectId = entry.SceneObjectId;
        _merchantInstanceId = entry.InstanceId;
        if (TryGetActive(_merchantCellId, out _, out var active))
            _merchantObject = active!.Scene.Find(_merchantSceneObjectId);
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
        if (entry is null)
        {
            var identity = _persistence.Spawn(_homeCellId, homeActive.Scene,
                new SceneObject(Guid.NewGuid(), "RpgSlice Road Raider")
                {
                    Transform = new Transform { Position = new Vector3(26f, 1.1f, 23f), Scale = new Vector3(0.8f, 1.7f, 0.8f) }
                });
            entry = _persistence.RuntimeObjects.ExportSnapshot().Single(item => item.InstanceId == identity.InstanceId);
        }
        _enemyCellId = entry.CellId;
        _enemySceneObjectId = entry.SceneObjectId;
        _enemyInstanceId = entry.InstanceId;
        if (TryGetActive(_enemyCellId, out _, out var active))
            _enemyObject = active!.Scene.Find(_enemySceneObjectId);
        if (_enemyObject is null) throw new InvalidDataException("RpgSlice road raider is not in an active cell.");
        if (!_save.ActorStates.TryGet(_enemyInstanceId.Value, out _))
            SetActor(ActorRuntimeState.Create(_enemyInstanceId.Value, _enemyDefinition));
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
        var smokePlayer = ActorRuntimeState.Create(Guid.NewGuid(), CreateActorDefinition(PlayerActorId, "Smoke Player"));
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

    private void EnsureItemDefinition()
    {
        if (_save.ItemDefs.Get(AppleId) is null)
            _save.ItemDefs.Add(new ItemDef(AppleId, "Apple", slot: null, stackable: true));
    }

    private void EnsurePlayerActor()
    {
        if (!_save.ActorStates.TryGet(PlayerWorldInstanceId, out _))
            SetActor(ActorRuntimeState.Create(PlayerWorldInstanceId,
                CreateActorDefinition(PlayerActorId, "Player")));
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

    private static ActorDef CreateActorDefinition(ContentId<ActorContentKind> id, string name) =>
        new(id, name, stats: new ActorStats
        {
            Attributes = new ActorAttributes
            {
                Strength = 10,
                Intelligence = 8,
                Willpower = 8,
                Agility = 8,
                Speed = 8,
                Endurance = 10,
                Personality = 6,
                Luck = 5
            }
        });
}
