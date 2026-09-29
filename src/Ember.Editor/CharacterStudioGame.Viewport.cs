using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ember.Authoring;
using Ember.Assets;
using Ember.Audio;
using Ember;
using Ember.Input;
using Ember.Project;
using Ember.Physics;
using Ember.Scene;
using Ember.Sequence;
using Ember.Render;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpGLTF.Schema2;

namespace Ember.Editor;

public sealed partial class CharacterStudioGame
{
    private void ProcessViewportSelection(MouseState mouse, Vector2 logicalMouse, bool uiCapturesMouse)
    {
        var pointerDown = mouse.LeftButton == ButtonState.Pressed;
        if (pointerDown && !_viewportPointerWasDown)
        {
            _viewportClickOrigin = logicalMouse;
            var beganTransform = _playSession is null && !_sequencePreviewEnabled
                && _sequenceExportJob?.IsRunning != true && !uiCapturesMouse
                && (_editorUi?.IsSceneViewportPoint(logicalMouse) ?? false)
                && BeginViewportTransformDrag(logicalMouse);
            _viewportClickPending = !beganTransform && _playSession is null && !_sequencePreviewEnabled
                && !uiCapturesMouse && (_editorUi?.IsSceneViewportPoint(logicalMouse) ?? false);
        }
        else if (pointerDown && _viewportTransformDrag is not null)
        {
            UpdateViewportTransformDrag(logicalMouse);
        }
        else if (!pointerDown && _viewportPointerWasDown)
        {
            if (_viewportTransformDrag is not null)
            {
                UpdateViewportTransformDrag(logicalMouse);
                FinishViewportTransformDrag();
            }
            else
            {
                var isClick = _viewportClickPending
                    && Vector2.DistanceSquared(_viewportClickOrigin, logicalMouse) <= 16f
                    && !uiCapturesMouse
                    && (_editorUi?.IsSceneViewportPoint(logicalMouse) ?? false);
                if (isClick) SelectSceneObjectAt(logicalMouse);
            }
            _viewportClickPending = false;
        }

        _viewportPointerWasDown = pointerDown;
    }

    private bool BeginViewportTransformDrag(Vector2 logicalMouse)
    {
        if (_editorUi is not { IsHomeVisible: false } editorUi
            || editorUi.SelectedObjectId is not { } objectId)
            return false;
        var mode = editorUi.IsMoveToolSelected ? TransformGizmoMode.Move
            : editorUi.IsTurnToolSelected ? TransformGizmoMode.Turn : TransformGizmoMode.Size;
        var scene = _sceneData;
        var item = scene.Find(objectId);
        if (item is null) return false;

        editorUi.CompletePendingEdit(scene);
        var before = CopyTransform(item.Transform);
        var origin = scene.GetWorldMatrix(objectId).Translation;
        var parentWorld = item.ParentId is { } parentId
            ? scene.GetWorldMatrix(parentId)
            : Matrix.Identity;
        var hasParent = item.ParentId is not null;
        var objectAxes = ViewportTransformGizmoMath.CreateObjectAxisDirections(before.Rotation, parentWorld);

        if (mode == TransformGizmoMode.Turn)
        {
            var rings = ViewportTransformGizmoMath.CreateRotationRings(origin,
                _camera.Position, _camera.View, _camera.Projection, LogicalWidth, LogicalHeight,
                objectAxes);
            if (!ViewportTransformGizmoMath.TryPickRotationRing(rings, logicalMouse, out var ring))
                return false;
            _viewportTransformDrag = new ViewportTransformDrag(mode, objectId, before,
                origin, default, ring, logicalMouse, logicalMouse, 0f, parentWorld, hasParent);
            _viewportClickPending = false;
            return true;
        }

        var handles = mode == TransformGizmoMode.Move
            ? ViewportTransformGizmoMath.CreateHandles(origin, _camera.Position, _camera.View,
                _camera.Projection, LogicalWidth, LogicalHeight)
            : ViewportTransformGizmoMath.CreateAxisHandles(origin, _camera.Position, _camera.View,
                _camera.Projection, LogicalWidth, LogicalHeight, objectAxes);
        if (!ViewportTransformGizmoMath.TryPick(handles, logicalMouse, out var handle)) return false;
        if (mode == TransformGizmoMode.Move && hasParent
            && !ViewportTransformGizmoMath.TryConvertWorldPositionToParentSpace(origin,
                parentWorld, out _))
            return false;

        _viewportTransformDrag = new ViewportTransformDrag(mode, objectId, before,
            origin, handle, null, logicalMouse, logicalMouse, 0f, parentWorld, hasParent);
        _viewportClickPending = false;
        return true;
    }

    private void UpdateViewportTransformDrag(Vector2 logicalMouse)
    {
        if (_viewportTransformDrag is not { } drag
            || _sceneData.Find(drag.ObjectId) is not { } item)
        {
            _viewportTransformDrag = null;
            return;
        }

        var updated = CopyTransform(drag.Before);
        switch (drag.Mode)
        {
            case TransformGizmoMode.Move:
            {
                var delta = ViewportTransformGizmoMath.CalculateMoveDelta(drag.AxisHandle,
                    drag.PointerStart, logicalMouse);
                var worldPosition = drag.StartWorldPosition + delta;
                if (_editorUi is { SnapMoveToGrid: true } editorUi)
                    worldPosition = ViewportTransformGizmoMath.SnapWorldPosition(worldPosition,
                        editorUi.MoveGridStep);
                var localPosition = worldPosition;
                if (drag.HasParent && !ViewportTransformGizmoMath.TryConvertWorldPositionToParentSpace(
                    worldPosition, drag.ParentWorld, out localPosition))
                    return;
                updated.Position = localPosition;
                break;
            }
            case TransformGizmoMode.Turn when drag.RotationRing is { } ring:
            {
                var increment = ViewportTransformGizmoMath.CalculateRotationPointerDelta(ring,
                    drag.LastPointer, logicalMouse);
                var accumulated = drag.AccumulatedAngle + increment;
                var angle = _editorUi is { SnapTurnToStep: true } editorUi
                    ? ViewportTransformGizmoMath.SnapAngle(accumulated, editorUi.TurnSnapDegrees)
                    : accumulated;
                var localAxis = ViewportTransformGizmoMath.LocalAxis(ring.Axis);
                var deltaRotation = Quaternion.CreateFromAxisAngle(localAxis, angle);
                updated.Rotation = Quaternion.Normalize(drag.Before.Rotation * deltaRotation);
                _viewportTransformDrag = drag with
                {
                    LastPointer = logicalMouse,
                    AccumulatedAngle = accumulated
                };
                break;
            }
            case TransformGizmoMode.Size:
            {
                var factor = ViewportTransformGizmoMath.CalculateScaleFactor(drag.AxisHandle,
                    drag.PointerStart, logicalMouse);
                if (_editorUi is { SnapSizeToStep: true } editorUi)
                    factor = ViewportTransformGizmoMath.SnapScaleFactor(factor,
                        editorUi.SizeSnapStep);
                var scale = updated.Scale;
                switch (drag.AxisHandle.Axis)
                {
                    case TransformGizmoAxis.X: scale.X = ScaleValue(scale.X, factor); break;
                    case TransformGizmoAxis.Y: scale.Y = ScaleValue(scale.Y, factor); break;
                    case TransformGizmoAxis.Z: scale.Z = ScaleValue(scale.Z, factor); break;
                }
                updated.Scale = scale;
                break;
            }
        }
        item.Transform = updated;
    }

    private void FinishViewportTransformDrag()
    {
        if (_viewportTransformDrag is not { } drag) return;
        _viewportTransformDrag = null;
        if (_sceneData.Find(drag.ObjectId) is not { } item) return;
        _editorUi?.CommitViewportTransform(_sceneData, drag.ObjectId,
            drag.Before, item.Transform);
    }

    private IReadOnlyList<Vector3> GetObjectAxisDirections(SceneObject item)
    {
        var parentWorld = item.ParentId is { } parentId
            ? _sceneData.GetWorldMatrix(parentId)
            : Matrix.Identity;
        return ViewportTransformGizmoMath.CreateObjectAxisDirections(
            item.Transform.Rotation, parentWorld);
    }

    private void DrawViewportTransformGizmo()
    {
        if (_playSession is not null || _sequencePreviewEnabled
            || _sequenceExportJob?.IsRunning == true
            || _editorUi is not { IsHomeVisible: false } editorUi
            || editorUi.SelectedObjectId is not { } objectId
            || _sceneData.Find(objectId) is not { } item)
            return;
        var mode = editorUi.IsMoveToolSelected ? TransformGizmoMode.Move
            : editorUi.IsTurnToolSelected ? TransformGizmoMode.Turn : TransformGizmoMode.Size;
        var origin = _sceneData.GetWorldMatrix(objectId).Translation;
        var objectAxes = GetObjectAxisDirections(item);
        var pointer = LogicalMouse(_input.CurrentMouse);
        var vertices = new List<VertexPositionColor>(256);

        if (mode == TransformGizmoMode.Turn)
        {
            var rings = ViewportTransformGizmoMath.CreateRotationRings(origin,
                _camera.Position, _camera.View, _camera.Projection, LogicalWidth, LogicalHeight,
                objectAxes);
            var hasHover = ViewportTransformGizmoMath.TryPickRotationRing(rings, pointer, out var hovered);
            foreach (var ring in rings)
            {
                var active = _viewportTransformDrag is { Mode: TransformGizmoMode.Turn } drag
                    && drag.RotationRing?.Axis == ring.Axis;
                var color = active || (hasHover && hovered.Axis == ring.Axis)
                    ? Color.Yellow : GetGizmoAxisColor(ring.Axis);
                for (var index = 0; index < ring.WorldPoints.Count - 1; index++)
                    AddGizmoLine(vertices, ring.WorldPoints[index], ring.WorldPoints[index + 1], color);
            }
        }
        else
        {
            var handles = mode == TransformGizmoMode.Move
                ? ViewportTransformGizmoMath.CreateHandles(origin, _camera.Position,
                    _camera.View, _camera.Projection, LogicalWidth, LogicalHeight)
                : ViewportTransformGizmoMath.CreateAxisHandles(origin, _camera.Position,
                    _camera.View, _camera.Projection, LogicalWidth, LogicalHeight, objectAxes);
            var hasHover = ViewportTransformGizmoMath.TryPick(handles, pointer, out var hovered);
            foreach (var handle in handles)
            {
                var active = _viewportTransformDrag is { } drag
                    && drag.Mode == mode && drag.AxisHandle.Axis == handle.Axis;
                var color = active || (hasHover && hovered.Axis == handle.Axis)
                    ? Color.Yellow : GetGizmoAxisColor(handle.Axis);
                var end = origin + handle.WorldDirection * handle.WorldLength;
                AddGizmoLine(vertices, origin, end, color);
                var viewDirection = _camera.Position - origin;
                if (viewDirection.LengthSquared() < 0.000001f) continue;
                viewDirection.Normalize();
                var side = Vector3.Cross(handle.WorldDirection, viewDirection);
                if (side.LengthSquared() < 0.000001f) continue;
                side.Normalize();
                var arrowLength = handle.WorldLength * 0.14f;
                AddGizmoLine(vertices, end,
                    end - handle.WorldDirection * arrowLength + side * arrowLength * 0.55f, color);
                AddGizmoLine(vertices, end,
                    end - handle.WorldDirection * arrowLength - side * arrowLength * 0.55f, color);
            }
        }

        DrawGizmoVertices(vertices);
    }

    private void DrawGizmoVertices(List<VertexPositionColor> vertices)
    {
        if (vertices.Count == 0) return;
        var device = GraphicsDevice;
        var previousDepth = device.DepthStencilState;
        var previousBlend = device.BlendState;
        var previousRasterizer = device.RasterizerState;
        try
        {
            device.DepthStencilState = DepthStencilState.None;
            device.BlendState = BlendState.Opaque;
            device.RasterizerState = RasterizerState.CullNone;
            var effect = _moveGizmoEffect;
            if (effect is null) return;
            effect.World = Matrix.Identity;
            effect.View = _camera.View;
            effect.Projection = _camera.Projection;
            var drawVertices = vertices.ToArray();
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(Microsoft.Xna.Framework.Graphics.PrimitiveType.LineList,
                    drawVertices, 0, drawVertices.Length / 2);
            }
        }
        finally
        {
            device.DepthStencilState = previousDepth;
            device.BlendState = previousBlend;
            device.RasterizerState = previousRasterizer;
        }
    }

    private static void AddGizmoLine(List<VertexPositionColor> vertices,
        Vector3 start, Vector3 end, Color color)
    {
        vertices.Add(new VertexPositionColor(start, color));
        vertices.Add(new VertexPositionColor(end, color));
    }

    private static Color GetGizmoAxisColor(TransformGizmoAxis axis) => axis switch
    {
        TransformGizmoAxis.X => new Color(235, 78, 78),
        TransformGizmoAxis.Y => new Color(98, 220, 116),
        _ => new Color(92, 153, 242)
    };

    private static Transform CopyTransform(Transform source) => new()
    {
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale
    };

    private static float ScaleValue(float value, float factor)
    {
        var sign = value == 0f ? 1f : MathF.CopySign(1f, value);
        return MathF.CopySign(Math.Clamp(MathF.Abs(value) * factor, 0.01f, 1_000f), sign);
    }

    private void SelectSceneObjectAt(Vector2 logicalMouse)
    {
        var logicalViewport = new Viewport(0, 0, LogicalWidth, LogicalHeight);
        var nearPoint = logicalViewport.Unproject(new Vector3(logicalMouse, 0f),
            _camera.Projection, _camera.View, Matrix.Identity);
        var farPoint = logicalViewport.Unproject(new Vector3(logicalMouse, 1f),
            _camera.Projection, _camera.View, Matrix.Identity);
        var direction = farPoint - nearPoint;
        if (direction.LengthSquared() <= 0.000001f) return;
        direction.Normalize();
        var ray = new Ray(nearPoint, direction);
        SceneObject? nearest = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (var item in _sceneData.Objects)
        {
            if (!item.Enabled || GetSceneObjectBounds(item) is not { } bounds) continue;
            var distance = ray.Intersects(new BoundingBox(bounds.Min, bounds.Max));
            if (distance is not { } hitDistance || hitDistance < 0f || hitDistance >= nearestDistance) continue;
            nearest = item;
            nearestDistance = hitDistance;
        }

        if (nearest is null)
        {
            _editorUi?.ClearObjectSelection();
            return;
        }

        var assetId = nearest.GltfAsset?.AssetId ?? nearest.StaticMeshLod?.NearAsset.AssetId;
        _editorUi?.SelectObject(nearest.Id, assetId);
    }

}
