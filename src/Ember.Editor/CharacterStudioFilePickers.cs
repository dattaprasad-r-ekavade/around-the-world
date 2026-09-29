using System;
using System.IO;
using System.Windows.Forms;

namespace Ember.Editor;

internal static class CharacterStudioFilePickers
{
    public static string? PickProjectFile(string? initialDirectory, IntPtr ownerHandle)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open an Ember project",
            Filter = "Ember project (ember.project.json)|ember.project.json|JSON files (*.json)|*.json",
            FilterIndex = 1,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            InitialDirectory = ExistingDirectoryOrDocuments(initialDirectory)
        };
        return dialog.ShowDialog(new WindowOwner(ownerHandle)) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    public static string? PickProjectParent(string? initialDirectory, IntPtr ownerHandle)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder for Ember projects",
            SelectedPath = ExistingDirectoryOrDocuments(initialDirectory),
            ShowNewFolderButton = true
        };
        return dialog.ShowDialog(new WindowOwner(ownerHandle)) == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    public static string? PickGltfFile(string? initialDirectory, IntPtr ownerHandle)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add a 3D model",
            Filter = "GLB models (*.glb)|*.glb",
            FilterIndex = 1,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            InitialDirectory = ExistingDirectoryOrDocuments(initialDirectory)
        };
        return dialog.ShowDialog(new WindowOwner(ownerHandle)) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    public static string? PickSceneTemplateFile(string? initialDirectory, IntPtr ownerHandle)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Locate a scene-template source",
            Filter = "Scene templates (*.embertemplate.json)|*.embertemplate.json|JSON files (*.json)|*.json",
            FilterIndex = 1,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            InitialDirectory = ExistingDirectoryOrDocuments(initialDirectory)
        };
        return dialog.ShowDialog(new WindowOwner(ownerHandle)) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    public static string? PickSceneSaveFile(string? initialDirectory, string suggestedFileName,
        IntPtr ownerHandle)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save the current scene",
            Filter = "Ember scene (*.json)|*.json",
            FilterIndex = 1,
            AddExtension = true,
            DefaultExt = "json",
            FileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "Scene.json" : suggestedFileName,
            OverwritePrompt = true,
            RestoreDirectory = true,
            InitialDirectory = ExistingDirectoryOrDocuments(initialDirectory)
        };
        return dialog.ShowDialog(new WindowOwner(ownerHandle)) == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private static string ExistingDirectoryOrDocuments(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private sealed class WindowOwner(IntPtr handle) : IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
