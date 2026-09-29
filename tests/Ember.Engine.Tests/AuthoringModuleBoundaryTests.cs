using System.Linq;
using Ember.Authoring;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class AuthoringModuleBoundaryTests
{
    [Fact]
    public void GenericAuthoringAssemblyDoesNotReferenceRpgAssemblies()
    {
        var references = typeof(AuthoredContentRecoveryStore).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name is "Ember.Rpg" or "Ember.Authoring.Rpg");
    }

    [Fact]
    public void RpgAuthoringAssemblyOwnsTheOptionalRpgDependency()
    {
        var assembly = typeof(AuthoredProjectValidator).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.Equal("Ember.Authoring.Rpg", assembly.GetName().Name);
        Assert.Contains("Ember.Authoring", references);
        Assert.Contains("Ember.Rpg", references);
    }
}
