using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

public sealed class ActorContentKind { }
public sealed class ItemContentKind { }
public sealed class FactionContentKind { }
public sealed class DialogueContentKind { }
public sealed class QuestContentKind { }
public sealed class SpellContentKind { }

/// <summary>A content key whose kind is part of its compile-time type.</summary>
[JsonConverter(typeof(ContentIdJsonConverterFactory))]
public readonly record struct ContentId<TKind>
{
    public string Value { get; }

    public ContentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A content id cannot be empty.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator string(ContentId<TKind> id) => id.Value;
}

/// <summary>Reads and writes typed content keys as the same plain strings used by content JSON.</summary>
public sealed class ContentIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ContentId<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var kind = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(
            typeof(ContentIdJsonConverter<>).MakeGenericType(kind), nonPublic: true)!;
    }

    private sealed class ContentIdJsonConverter<TKind> : JsonConverter<ContentId<TKind>>
    {
        public override ContentId<TKind> Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("A content id must be a string.");
            try { return new ContentId<TKind>(reader.GetString()!); }
            catch (ArgumentException exception) { throw new JsonException(exception.Message, exception); }
        }

        public override void Write(Utf8JsonWriter writer, ContentId<TKind> value,
            JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }
}

public sealed record ContentDiagnostic(string SourceRecord, string MissingTarget)
{
    public override string ToString() => $"{SourceRecord} references missing {MissingTarget}.";
}

/// <summary>Typed ID sets used by content validation to catch broken links before play.</summary>
public sealed class ContentRegistry
{
    private readonly System.Collections.Generic.Dictionary<Type, System.Collections.Generic.HashSet<string>> _ids = new();

    public bool Register<TKind>(ContentId<TKind> id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException($"A registered {typeof(TKind).Name} id cannot be empty.", nameof(id));
        if (!_ids.TryGetValue(typeof(TKind), out var ids))
        {
            ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            _ids.Add(typeof(TKind), ids);
        }
        return ids.Add(id.Value);
    }

    public bool Contains<TKind>(ContentId<TKind> id) =>
        _ids.TryGetValue(typeof(TKind), out var ids) && ids.Contains(id.Value);

    public ContentDiagnostic? CheckReference<TKind>(string sourceRecord, ContentId<TKind> target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRecord);
        return Contains(target) ? null : new ContentDiagnostic(sourceRecord, $"{typeof(TKind).Name} '{target.Value}'");
    }
}
