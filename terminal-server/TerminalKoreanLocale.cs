using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Utils;
using SysPath = System.IO.Path;

namespace Manimal.Terminal.Server;

// zzap: Korean text for the server-side strings Terminal adds or relies on — container
// names, map card / loading banner, the equipment cabinet, and the Content Backport
// quest items (that mod ships ch/en/ru only, so Korean clients saw English).
// Registered at PostLoad so this transformer runs after every other mod's locale
// transformers for 'kr' and wins; other languages are untouched.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 5)]
public sealed class TerminalKoreanLocale(LocaleTable localeTable, JsonUtil jsonUtil, ISptLogger<TerminalKoreanLocale> logger) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var modDir = SysPath.GetDirectoryName(typeof(TerminalKoreanLocale).Assembly.Location) ?? ".";
            var path = SysPath.Combine(modDir, "db", "locales", "kr.json");
            if (!System.IO.File.Exists(path))
                return;
            var overrides = await jsonUtil.DeserializeFromFileAsync<Dictionary<string, string>>(path, cancellationToken);
            if (overrides is null || overrides.Count == 0)
                return;
            overrides.Remove("_comment");
            if (!localeTable.Global.TryGetValue("kr", out var kr))
            {
                logger.Warning("[Terminal] no 'kr' global locale — Korean overrides skipped");
                return;
            }
            kr.AddTransformer(locale =>
            {
                if (locale is not null)
                    foreach (var entry in overrides) locale[entry.Key] = entry.Value;
                return locale;
            });
            logger.Info($"[Terminal] Korean locale: {overrides.Count} override(s) armed (containers, map card, cabinet, quest items)");
        }
        catch (Exception e)
        {
            logger.Warning($"[Terminal] Korean locale overrides failed: {e.Message}");
        }
    }
}
