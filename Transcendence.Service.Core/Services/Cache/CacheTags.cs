namespace Transcendence.Service.Core.Services.Cache;

public static class CacheTags
{
    public static string ForPatch(string patchVersion) => $"patch:{patchVersion.Trim()}";
}
