using Ember.Scene;
using Ember.Render;
using Ember.Authoring;
using Ember.Project;
using Ember.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace Ember.Editor;

internal sealed partial class CharacterStudioEditorUi
{
    private readonly WorldPanel _worldPanel;

    private void DrawWorldAuthoringPanel(SceneGraph scene) => _worldPanel.DrawWorldAuthoringPanel(scene);
    private void DrawWorldCellPanel(SceneGraph scene) => _worldPanel.DrawWorldCellPanel(scene);
    private void LoadWorldManifest() => _worldPanel.LoadWorldManifest();
    private void CancelRecoveryReview() => _worldPanel.CancelRecoveryReview();
    private static void DeleteRecoveryStaging(AuthoredProjectRecoveryStaging staging, string? recoveryDirectory) =>
        WorldPanel.DeleteRecoveryStaging(staging, recoveryDirectory);
    private void PollRecoveryReview() => _worldPanel.PollRecoveryReview();
    private void MaybeAutosaveAuthoredProject(SceneGraph scene, float elapsedSeconds) => _worldPanel.MaybeAutosaveAuthoredProject(scene, elapsedSeconds);
    public void RefreshAfterRecovery() => _worldPanel.RefreshAfterRecovery();
    private WorldCellDefinition? FindCurrentWorldCell() => _worldPanel.FindCurrentWorldCell();
}
