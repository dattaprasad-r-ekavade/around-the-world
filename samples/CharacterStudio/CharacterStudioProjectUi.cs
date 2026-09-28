using System;
using System.IO;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private string _projectParentDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EmberProjects");
    private string _projectName = "New Game";
    private string _projectWorkspaceStatus = "Create a project or open an existing ember.project.json.";
}
