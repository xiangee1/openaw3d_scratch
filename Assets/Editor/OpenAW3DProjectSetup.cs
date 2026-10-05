using UnityEditor;

public static class OpenAW3DProjectSetup
{
    [InitializeOnLoadMethod]
    static void Prepare()
    {
        EditorApplication.delayCall += Run;
    }

    public static void Run()
    {
        const string path = "Assets/Scenes/MainMenu.unity";
        var scenes = EditorBuildSettings.scenes;
        if (scenes.Length != 1 || scenes[0].path != path || !scenes[0].enabled)
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
    }
}
