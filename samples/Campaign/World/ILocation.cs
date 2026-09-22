using Ember.Render;
using Ember.Ui;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Campaign;

public readonly record struct LookHint(string Line, PromptRole Role, Marker Marker);

public interface ILocation
{
    string Name { get; }
    StoneTextures.StonePalette Palette { get; }
    Color ClearColour { get; }
    IReadOnlyList<PointLight> Lights { get; }
    float SampleGround(float x, float z);
    Vector3 Collide(Vector3 origin, Vector3 delta, float radius);
    void Draw(SceneRenderer scene, BillboardRenderer billboards, GroundedView view,
        CampaignSprites sprites);
    LookHint? Probe(Vector3 eye, Vector3 forward);
}
