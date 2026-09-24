using System;
using System.Collections.Generic;
using System.Linq;

namespace Ember.Rpg;

/// <summary>Ownership-aware pickup with once-only witnessed reputation consequences.</summary>
public static class TheftSystem
{
    public static bool TryTake(Guid theftEventId, ActorRuntimeState taker, Guid worldItemInstanceId,
        WorldItemStore worldItems, ItemCatalogue definitions, bool witnessed, int reputationPenalty,
        out ActorRuntimeState updatedTaker, out WorldItemStore updatedWorldItems, out bool wasTheft)
    {
        ArgumentNullException.ThrowIfNull(taker);
        ArgumentNullException.ThrowIfNull(worldItems);
        ArgumentNullException.ThrowIfNull(definitions);
        taker.Validate();
        updatedTaker = taker;
        updatedWorldItems = worldItems;
        wasTheft = false;
        if (theftEventId == Guid.Empty || reputationPenalty > 0
            || !worldItems.TryGet(worldItemInstanceId, out var worldItem))
            return false;

        var ownerFaction = worldItem.OwnerFactionId;
        var isTheft = ownerFaction is { } factionId && !FactionSystem.IsMember(taker, factionId);
        if (isTheft && taker.ProcessedTheftEventIds.Contains(theftEventId)) return false;
        if (!InventoryTransfer.TryPickup(worldItemInstanceId, taker.Inventory, worldItems, definitions,
            out var inventory, out var remainingWorldItems))
            return false;

        var updated = taker with { Inventory = inventory };
        if (isTheft)
        {
            var processedIds = new List<Guid>(taker.ProcessedTheftEventIds) { theftEventId };
            updated = updated with { ProcessedTheftEventIds = processedIds };
            if (witnessed)
            {
                try { updated = FactionSystem.AdjustReputation(updated, ownerFaction!.Value, reputationPenalty); }
                catch (OverflowException) { return false; }
            }
        }

        updated.Validate();
        updatedTaker = updated;
        updatedWorldItems = remainingWorldItems;
        wasTheft = isTheft;
        return true;
    }
}
