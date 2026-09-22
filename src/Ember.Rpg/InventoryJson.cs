using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

/// <summary>
/// Writes an <see cref="ItemCatalogue"/> as a flat JSON array of definitions, so the save
/// reads as a list of items rather than a wrapper around a dictionary.
/// </summary>
internal sealed class ItemCatalogueJson : JsonConverter<ItemCatalogue>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ItemCatalogue);

    public override ItemCatalogue Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("An item catalogue must be a JSON array.");

        var catalogue = new ItemCatalogue();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            catalogue.Add(JsonSerializer.Deserialize<ItemDef>(ref reader, options)
                ?? throw new JsonException("An item definition cannot be null."));
        return catalogue;
    }

    public override void Write(Utf8JsonWriter writer, ItemCatalogue value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var def in value.All.Values) JsonSerializer.Serialize(writer, def, options);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Writes a <see cref="Bag"/> as a flat JSON array of entries —
/// <c>[{"ItemId": "potion_heal", "Count": 3}]</c>.
/// </summary>
internal sealed class BagJson : JsonConverter<Bag>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Bag);

    public override Bag Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("A bag must be a JSON array.");

        var bag = new Bag();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var entry = JsonSerializer.Deserialize<BagEntry>(ref reader, options)
                ?? throw new JsonException("A bag entry cannot be null.");
            Restore(bag, entry);
        }
        return bag;
    }

    public override void Write(Utf8JsonWriter writer, Bag value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var entry in value.Entries) JsonSerializer.Serialize(writer, entry, options);
        writer.WriteEndArray();
    }

    // Loading is not an add: entries come back exactly as written, in order, with their
    // counts — the stackable/merge policy only ever applies to something the game did.
    private static void Restore(Bag bag, BagEntry entry)
    {
        if (entry.Count < 1)
            throw new JsonException($"Bag entry '{entry.ItemId}' has a count of {entry.Count}.");
        bag.Restore(entry);
    }
}

/// <summary>
/// Writes <see cref="EquipSlots"/> as a flat JSON object — <c>{"mainhand": "sword_iron"}</c>.
/// </summary>
internal sealed class EquipSlotsJson : JsonConverter<EquipSlots>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(EquipSlots);

    public override EquipSlots Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("An equip slot store must be a JSON object.");

        var slots = new EquipSlots();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a slot name inside an equip slot store.");

            var slot = reader.GetString() ?? throw new JsonException("A slot name cannot be null.");
            reader.Read();
            slots.Set(slot, reader.GetString() ?? throw new JsonException("An equipped item id cannot be null."));
        }
        return slots;
    }

    public override void Write(Utf8JsonWriter writer, EquipSlots value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value.All) writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
    }
}
