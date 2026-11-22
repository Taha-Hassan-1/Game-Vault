namespace GameVault.Web.Models;

/// <summary>
/// Picks one of twelve preset colours for a game without a cover. A class instead of an inline style
/// keeps the Content Security Policy free of 'unsafe-inline'.
/// </summary>
public static class Hue
{
    public const int Steps = 12;
    public static int Index(int id) => ((id * 47) % 360) / (360 / Steps);
    public static string Class(int id) => "gv-hue-" + Index(id);
}
