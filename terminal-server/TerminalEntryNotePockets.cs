using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Common.Models.Logging;

namespace Manimal.Terminal.Server;

// zzap: the checkpoint letter is Special-Slot-only, but the original patch only opened
// the two stock pockets (standard + Unheard). Mods that give the PMC its own pockets
// template — SVM "CustomPocket" clones one with a new id — left the letter with no
// slot it could go into, so the map's AccessKeys check said the item was missing.
// Open every Pockets-class template's special slots instead.
internal static class TerminalEntryNotePockets
{
    internal const string LetterTpl = "68f213f8ea61d1803707cf7a";
    private const string PocketsClass = "557596e64bdc2dc2118b4571";

    // returns (pockets templates touched, slot filters newly opened). idempotent.
    internal static (int Pockets, int Filters) Allow(Dictionary<MongoId, TemplateItem> items)
    {
        var pocketsClass = new MongoId(PocketsClass);
        var letter = new MongoId(LetterTpl);
        int touched = 0, opened = 0;
        foreach (var template in items.Values)
        {
            if (template?.Properties?.Slots is null || !template.Parent.Equals(pocketsClass))
                continue;

            var any = false;
            foreach (var slot in template.Properties.Slots)
            {
                var filter = slot.Properties?.Filters?.FirstOrDefault()?.Filter;
                if (filter is null)
                    continue;
                any = true;
                if (filter.Add(letter))
                    opened++;
            }
            if (any)
                touched++;
        }
        return (touched, opened);
    }
}

// second pass after every Preload mod (and profile load) has run, for pockets
// templates registered by a mod that loads later than TerminalMod.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 4)]
public sealed class TerminalEntryNoteLatePockets(TemplateTable templateTable, ISptLogger<TerminalEntryNoteLatePockets> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (pockets, filters) = TerminalEntryNotePockets.Allow(templateTable.Items);
            if (filters > 0)
                logger.Info($"[Terminal] Prapor checkpoint letter: late pass opened {filters} more Special Slot filter(s) ({pockets} pockets template(s) checked)");
        }
        catch (Exception e)
        {
            logger.Warning($"[Terminal] Prapor checkpoint letter late pockets pass failed: {e.Message}");
        }
        return Task.CompletedTask;
    }
}
