using System;

namespace Ember.Rpg;

/// <summary>
/// Prepares inventory/world-item transfers on copies. Inputs change only when the caller
/// accepts both returned values, so a failed pickup or drop cannot duplicate or destroy data.
/// Pass <c>WorldInstanceId.Value</c> from Ember.Engine without making RPG depend on the engine.
/// </summary>
public static class InventoryTransfer
{
    public static bool TryPickup(Guid worldInstanceId, Bag inventory, WorldItemStore worldItems,
        ItemCatalogue definitions, out Bag updatedInventory, out WorldItemStore updatedWorldItems)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(worldItems);
        ArgumentNullException.ThrowIfNull(definitions);
        updatedInventory = inventory;
        updatedWorldItems = worldItems;
        if (!worldItems.TryGet(worldInstanceId, out var worldItem)
            || !definitions.TryGet(worldItem.ItemId, out var definition)
            || !worldItems.TryRemove(worldInstanceId, out var remainingWorldItems))
            return false;

        var nextInventory = inventory.Copy();
        try { nextInventory.Add(definition, worldItem.Count); }
        catch (ArgumentException) { return false; }
        catch (OverflowException) { return false; }
        updatedInventory = nextInventory;
        updatedWorldItems = remainingWorldItems;
        return true;
    }

    public static bool TryDrop(Guid newWorldInstanceId, Bag inventory, WorldItemStore worldItems,
        ItemCatalogue definitions, ContentId<ItemContentKind> itemId, int count,
        out Bag updatedInventory, out WorldItemStore updatedWorldItems)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(worldItems);
        ArgumentNullException.ThrowIfNull(definitions);
        updatedInventory = inventory;
        updatedWorldItems = worldItems;
        if (newWorldInstanceId == Guid.Empty || count < 1 || !inventory.Has(itemId, count)
            || !definitions.TryGet(itemId, out _)
            || !worldItems.TryAdd(new WorldItemEntry(newWorldInstanceId, itemId, count), out var nextWorldItems))
            return false;

        var nextInventory = inventory.Copy();
        if (!nextInventory.Remove(itemId, count)) return false;
        updatedInventory = nextInventory;
        updatedWorldItems = nextWorldItems;
        return true;
    }
}
