using System;
using Ember.Scene;

namespace Ember.Authoring;

/// <summary>Undoable, explicit update of one expanded template instance to a newer revision.</summary>
public sealed class UpdateSceneTemplateCommand : ISceneCommand
{
    private readonly Guid _instanceWrapperId;
    private readonly SceneTemplateSnapshot _template;
    private SceneTemplateInstanceState? _before;
    private SceneTemplateInstanceState? _after;

    public UpdateSceneTemplateCommand(Guid instanceWrapperId, SceneTemplateSnapshot template)
    {
        if (instanceWrapperId == Guid.Empty)
            throw new ArgumentException("Template instance wrapper ID cannot be empty.", nameof(instanceWrapperId));
        _instanceWrapperId = instanceWrapperId;
        _template = template ?? throw new ArgumentNullException(nameof(template));
    }

    public void Apply(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_after is not null)
        {
            SceneTemplateInstanceSystem.RestoreState(scene, _after);
            return;
        }

        _before = SceneTemplateInstanceSystem.CaptureState(scene, _instanceWrapperId);
        try
        {
            SceneTemplateInstanceSystem.Update(scene, _instanceWrapperId, _template);
            _after = SceneTemplateInstanceSystem.CaptureState(scene, _instanceWrapperId);
        }
        catch
        {
            SceneTemplateInstanceSystem.RestoreState(scene, _before);
            throw;
        }
    }

    public void Revert(SceneGraph scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_before is null || _after is null)
            throw new InvalidOperationException("Cannot undo a template update that was not applied.");
        SceneTemplateInstanceSystem.RestoreState(scene, _before);
    }
}
