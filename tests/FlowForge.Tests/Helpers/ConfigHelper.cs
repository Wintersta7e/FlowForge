namespace FlowForge.Tests.Helpers;

internal static class ConfigHelper
{
    public static Dictionary<string, System.Text.Json.JsonElement> MakeConfig(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach ((string Key, string Value) pair in pairs)
        {
            dict[pair.Key] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>($"\"{pair.Value}\"");
        }
        return dict;
    }
}
