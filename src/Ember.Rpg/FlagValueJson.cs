using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

/// <summary>
/// Reads and writes a <see cref="FlagValue"/> as its plain JSON value — <c>true</c>, <c>120</c>,
/// <c>"oak_hall"</c> — with the kind carried by the token itself. Shared by the flag store and
/// by anything else that holds flag values, such as an option's "sets" in a dialogue tree.
/// </summary>
internal sealed class FlagValueJson : JsonConverter<FlagValue>
{
    internal static readonly FlagValueJson Instance = new();

    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(FlagValue);

    public override FlagValue Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => FlagValue.From(true),
        JsonTokenType.False => FlagValue.From(false),
        JsonTokenType.Number => FlagValue.From(reader.GetDouble()),
        JsonTokenType.String => FlagValue.From(reader.GetString() ?? string.Empty),
        JsonTokenType.Null => throw new JsonException("A flag value cannot be null."),
        _ => throw new JsonException($"Unexpected token {reader.TokenType} for a flag value.")
    };

    public override void Write(Utf8JsonWriter writer, FlagValue value,
        JsonSerializerOptions options)
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
