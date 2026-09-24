using System;

namespace Ember.Rpg;

/// <summary>Atomic buy/sell rules for one player and one merchant inventory.</summary>
public static class MerchantTrade
{
    public static bool TryBuy(PlayerRecord player, ActorRuntimeState merchant,
        ItemCatalogue definitions, ContentId<ItemContentKind> itemId, int count, long unitPrice,
        out PlayerRecord updatedPlayer, out ActorRuntimeState updatedMerchant)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(merchant);
        ArgumentNullException.ThrowIfNull(definitions);
        player.Validate();
        merchant.Validate();
        updatedPlayer = player;
        updatedMerchant = merchant;
        if (!TryGetPrice(count, unitPrice, out var total) || player.Currency < total)
            return false;
        long merchantCurrency;
        try { merchantCurrency = checked(merchant.Currency + total); }
        catch (OverflowException) { return false; }

        if (!InventoryTransfer.TryMove(merchant.Inventory, player.Bag, definitions, itemId, count,
            out var merchantBag, out var playerBag)) return false;

        updatedPlayer = player with { Bag = playerBag, Currency = player.Currency - total };
        updatedMerchant = merchant with
        {
            Inventory = merchantBag,
            Currency = merchantCurrency
        };
        return true;
    }

    public static bool TrySell(PlayerRecord player, ActorRuntimeState merchant,
        ItemCatalogue definitions, ContentId<ItemContentKind> itemId, int count, long unitPrice,
        out PlayerRecord updatedPlayer, out ActorRuntimeState updatedMerchant)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(merchant);
        ArgumentNullException.ThrowIfNull(definitions);
        player.Validate();
        merchant.Validate();
        updatedPlayer = player;
        updatedMerchant = merchant;
        if (!TryGetPrice(count, unitPrice, out var total) || merchant.Currency < total)
            return false;
        long playerCurrency;
        try { playerCurrency = checked(player.Currency + total); }
        catch (OverflowException) { return false; }

        if (!InventoryTransfer.TryMove(player.Bag, merchant.Inventory, definitions, itemId, count,
            out var playerBag, out var merchantBag)) return false;

        updatedPlayer = player with { Bag = playerBag, Currency = playerCurrency };
        updatedMerchant = merchant with
        {
            Inventory = merchantBag,
            Currency = merchant.Currency - total
        };
        return true;
    }

    private static bool TryGetPrice(int count, long unitPrice, out long total)
    {
        total = 0;
        if (count <= 0 || unitPrice <= 0) return false;
        try { total = checked((long)count * unitPrice); }
        catch (OverflowException) { return false; }
        return true;
    }
}
