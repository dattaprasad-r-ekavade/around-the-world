using Ember.Assets;
using Ember.Physics;
using Microsoft.Xna.Framework;
using SharpGLTF.Schema2;
using System;
using System.IO;
using System.Linq;
using Xunit;
using NumericsQuaternion = System.Numerics.Quaternion;

namespace Ember.Engine.Tests;

public sealed class GltfCharacterMotionAnimatorTests
{
    [Fact]
    public void MotionAnimatorUsesPostPhysicsSpeedAndGroundedJumpTransitions()
    {
        var model = ModelRoot.Load(FoxFixturePath());
        var syntheticJump = model.CreateAnimation("Jump");
        var hip = model.LogicalNodes.Single(node => node.Name == "b_Hip_01");
        syntheticJump.CreateRotationChannel(hip,
            new System.Collections.Generic.SortedDictionary<float, NumericsQuaternion>
            {
                [0f] = NumericsQuaternion.Identity,
                [0.5f] = NumericsQuaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, 0.2f)
            }, linear: true);
        var asset = GltfSkinnedCharacterData.Import(model);

        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
        world.AddStaticBox(new Vector3(2f, 1f, 0f), new Vector3(0.2f, 2f, 8f));
        using var character = new PhysicsCharacterController(world, new Vector3(0f, 1f, 0f));
        var idle = asset.Animations.Single(clip => clip.Name == "Survey");
        var walk = asset.Animations.Single(clip => clip.Name == "Walk");
        var jump = asset.Animations.Single(clip => clip.Name == "Jump");
        var animator = new GltfCharacterMotionAnimator(character, asset, idle, walk, jump);
        Assert.Equal(CharacterMotionState.Idle, animator.State);

        character.SetMoveInput(Vector3.UnitX);
        var sawWalk = false;
        for (var step = 0; step < 180; step++)
        {
            world.Step(1f / 60f);
            animator.AdvanceFixedStep(1f / 60f);
            sawWalk |= animator.State == CharacterMotionState.Walk;
        }

        Assert.True(sawWalk);
        Assert.Equal(CharacterMotionState.Idle, animator.State);
        Assert.Equal("Survey", animator.CurrentClipName);

        character.RequestJump();
        world.Step(1f / 60f);
        animator.AdvanceFixedStep(1f / 60f);
        Assert.Equal(CharacterMotionState.Jump, animator.State);
        Assert.Equal("Jump", animator.CurrentClipName);

        for (var step = 0; step < 180; step++)
        {
            world.Step(1f / 60f);
            animator.AdvanceFixedStep(1f / 60f);
        }

        Assert.Equal(CharacterMotionState.Idle, animator.State);
        Assert.Equal("Survey", animator.CurrentClipName);
        Assert.Equal(animator.Pose.JointCount, animator.Pose.SkinMatrices.Count);
    }

    private static string FoxFixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "Fox.glb");
}
