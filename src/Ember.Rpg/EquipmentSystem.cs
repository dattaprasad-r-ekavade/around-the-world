using System;
using System.Collections.Generic;

namespace Ember.Rpg;

public sealed record EquipmentStatBonus(ActorAttribute Attribute, double Amount)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Attribute)) throw new ArgumentOutOfRangeException(nameof(Attribute));
        if (!double.IsFinite(Amount)) throw new ArgumentOutOfRangeException(nameof(Amount));
    }
}

/// <summary>Data a renderer can turn into a GltfBoneAttachment for the equipped prop.</summary>
public sealed record EquippedBoneAttachment(
    string Slot, ContentId<ItemContentKind> ItemId, string BoneName);

/// <summary>Atomic player equipment updates and derived equipment effects.</summary>
public static class EquipmentSystem
{
    public static bool TryEquip(PlayerRecord player, ItemCatalogue definitions,
        ContentId<ItemContentKind> itemId, out PlayerRecord updated)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(definitions);
        updated = player;
        if (!definitions.TryGet(itemId, out var incoming)
            || string.IsNullOrWhiteSpace(incoming.Slot)
            || !player.Bag.Has(itemId))
            return false;

        var oldItemId = player.Equip.Get(incoming.Slot);
        if (oldItemId == itemId.Value) return false;
        var bag = player.Bag.Copy();
        if (oldItemId.Length > 0)
        {
            if (!definitions.TryGet(oldItemId, out var outgoing)) return false;
            try { bag.Add(outgoing); }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }
        if (!bag.Remove(itemId)) return false;

        var equip = player.Equip.Copy();
        equip.Set(incoming.Slot, itemId.Value);
        updated = player with { Bag = bag, Equip = equip };
        return true;
    }

    public static bool TryUnequip(PlayerRecord player, ItemCatalogue definitions,
        string slot, out PlayerRecord updated)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        updated = player;
        var equippedId = player.Equip.Get(slot);
        if (equippedId.Length == 0 || !definitions.TryGet(equippedId, out var item)) return false;

        var bag = player.Bag.Copy();
        try { bag.Add(item); }
        catch (ArgumentException) { return false; }
        catch (OverflowException) { return false; }
        var equip = player.Equip.Copy();
        equip.Remove(slot);
        updated = player with { Bag = bag, Equip = equip };
        return true;
    }

    /// <summary>Equipment bonuses apply before temporary effects and never change base attributes.</summary>
    public static ActorStats EffectiveStats(PlayerRecord player, ItemCatalogue definitions)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(definitions);
        var bonuses = new Dictionary<ActorAttribute, double>();
        foreach (var (slot, itemIdText) in player.Equip.All)
        {
            if (!definitions.TryGet(itemIdText, out var item))
                throw new InvalidOperationException($"Equipment slot '{slot}' references missing item '{itemIdText}'.");
            foreach (var bonus in item.StatBonuses)
            {
                bonus.Validate();
                bonuses[bonus.Attribute] = bonuses.GetValueOrDefault(bonus.Attribute) + bonus.Amount;
            }
        }
        var attributes = player.Stats.Attributes;
        foreach (var (attribute, amount) in bonuses)
            attributes = attributes.With(attribute, Math.Max(0, attributes.Get(attribute) + amount));
        return player.Stats with { Attributes = player.Modifiers.ApplyTo(attributes) };
    }

    public static IReadOnlyList<EquippedBoneAttachment> Attachments(PlayerRecord player,
        ItemCatalogue definitions)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(definitions);
        var attachments = new List<EquippedBoneAttachment>();
        foreach (var (slot, itemIdText) in player.Equip.All)
        {
            if (!definitions.TryGet(itemIdText, out var item))
                throw new InvalidOperationException($"Equipment slot '{slot}' references missing item '{itemIdText}'.");
            if (!string.IsNullOrWhiteSpace(item.AttachmentBone))
                attachments.Add(new EquippedBoneAttachment(slot,
                    new ContentId<ItemContentKind>(itemIdText), item.AttachmentBone));
        }
        return attachments;
    }
}
