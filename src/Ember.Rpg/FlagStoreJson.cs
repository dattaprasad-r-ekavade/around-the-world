using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

/// <summary>
/// Writes a <see cref="FlagStore"/> as a flat JSON object — <c>{"gate_open": true, "gold":
/// 120}</c> — not as a wrapper around an internal dictionary, so a hand-edited save reads the
/// way it looks and writes back the same. Values are read and written by
/// <see cref="FlagValueJson"/>.
/// </summary>
internal sealed class FlagStoreJson : JsonConverter<FlagStore>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(FlagStore);

    public override FlagStore Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A flag store must be a JSON object.");

        var store = new FlagStore();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a flag name inside a flag store.");

            var name = reader.GetString() ?? throw new JsonException("A flag name cannot be null.");
            reader.Read();
            store.Set(name, FlagValueJson.Instance.Read(ref reader, typeof(FlagValue), options));
        }

        return store;
    }

    public override void Write(Utf8JsonWriter writer, FlagStore value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value.All)
        {
            writer.WritePropertyName(pair.Key);
            FlagValueJson.Instance.Write(writer, pair.Value, options);
        }
        writer.WriteEndObject();
    }
}
