using System;
using System.IO;
using Ember.Rpg;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class RpgPlacementContentTests
{
    [Fact]
    public void CharacterStudioSampleRegistersActorAndItemDefinitions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "RpgPlacementDefinitions.json");
        var content = RpgContentJson.Load(path);

        Assert.Equal(2, content.Actors.Count);
        Assert.Equal("Town Guard", content.Actors.Get(new ContentId<ActorContentKind>("actors.town-guard"))!.Name);
        Assert.Equal(2, content.Items.Count);
        Assert.Equal("Iron Sword", content.Items.Get("items.iron-sword")!.Name);
    }
}
