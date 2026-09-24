using System;
using System.Collections.Generic;
using System.Linq;
using Ember.Scene;

namespace Ember.World;

/// <summary>Applies explicit per-instance reset rules at a stable cell unload/reload boundary.</summary>
public static class WorldCellResetService
{
    public static int ResetCell(Guid cellId, SceneGraph authoredBaseline,
        WorldInstanceIdentityMap identities, WorldCellChangeStore changes,
        WorldRuntimeObjectStore runtimeObjects)
    {
        ArgumentNullException.ThrowIfNull(authoredBaseline);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(runtimeObjects);
        if (cellId == Guid.Empty)
            throw new ArgumentException("World cell ID cannot be empty.", nameof(cellId));

        var resettable = new HashSet<WorldInstanceId>();
        foreach (var sceneObject in authoredBaseline.Objects)
        {
            if (sceneObject.ResetPolicy != WorldInstanceResetPolicy.ResetOnCellReset) continue;
            resettable.Add(identities.GetOrCreate(cellId, sceneObject));
        }

        var resettableRuntime = runtimeObjects.ExportSnapshot()
            .Where(entry => entry.CellId == cellId
                && entry.SceneObject.ResetPolicy == WorldInstanceResetPolicy.ResetOnCellReset)
            .ToArray();
        foreach (var entry in resettableRuntime) resettable.Add(entry.InstanceId);

        foreach (var instanceId in resettable) changes.Remove(cellId, instanceId);
        foreach (var entry in resettableRuntime)
            runtimeObjects.Remove(cellId, entry.InstanceId, identities);
        return resettable.Count;
    }
}
