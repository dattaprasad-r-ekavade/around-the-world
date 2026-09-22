using System;

namespace Ember.Scene;

/// <summary>A stable scene object identity and its local authored state.</summary>
public sealed class SceneObject
{
    public SceneObject(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("Scene object ID cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scene object name is required.", nameof(name));

        Id = id;
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public Transform Transform { get; set; } = new();
    public Guid? ParentId { get; internal set; }
}
