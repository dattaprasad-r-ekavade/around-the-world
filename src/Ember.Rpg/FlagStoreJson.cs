using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

/// <summary>
/// Writes a <see cref="FlagStore"/> as a flat JSON object — <c>{"gate_open": true, "gold":
/// 120}</c> — not as a wrapper around an internal dictionary, so a hand-edited save reads the
/// way it looks and writes back the same.
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
            store.Set(name, ReadValue(ref reader));
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
            WriteValue(writer, pair.Value);
        }
        writer.WriteEndObject();
    }

    private static FlagValue ReadValue(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.True => FlagValue.From(true),
        JsonTokenType.False => FlagValue.From(false),
        JsonTokenType.Number => FlagValue.From(reader.GetDouble()),
        JsonTokenType.String => FlagValue.From(reader.GetString() ?? string.Empty),
        JsonTokenType.Null => throw new JsonException("A flag value cannot be null."),
        _ => throw new JsonException($"Unexpected token {reader.TokenType} for a flag value.")
    };

    private static void WriteValue(Utf8JsonWriter writer, FlagValue value)
    {
        switch (value.Kind)
        {
            case FlagKind.Boolean:
                writer.WriteBooleanValue(value.AsBool());
                break;

            case FlagKind.Number:
                // Whole numbers are written as integers, so a hand-edited save shows 120
                // rather than 120.0.
                var number = value.AsNumber();
                if (number % 1 == 0 && Math.Abs(number) < 1e15) writer.WriteNumberValue((long)number);
                else writer.WriteNumberValue(number);
                break;

            case FlagKind.Text:
                writer.WriteStringValue(value.AsText());
                break;

            default:
                throw new JsonException($"Unknown flag kind {value.Kind}.");
        }
    }
}
