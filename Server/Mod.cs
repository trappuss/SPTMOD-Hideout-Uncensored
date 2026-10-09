using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace HideoutUncensoredServer;

public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.trappuss.hideoutuncensored"; // same GUID as the client plugin (Forge rule)
    public string Name { get; init; } = "HideoutUncensored"; // letters and numbers only (Forge rule)
    public string Author { get; init; } = "trappuss";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.2.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/trappuss/SPTMOD-Hideout-Uncensored";
    public string License { get; init; } = "MIT";
}

/// <summary>
/// Items the client said it is about to discard in the hideout. An entry is used once and only for a short time, so
/// a discard anywhere else (the stash in the main menu) keeps destroying the item as usual.
/// </summary>
public static class LeftInHideout
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<string, DateTime> Announced = new();

    private static string Key(MongoId sessionId, string itemId) => sessionId + "/" + itemId;

    public static void Announce(MongoId sessionId, string itemId)
    {
        DateTime now = DateTime.UtcNow;
        foreach (var old in Announced.Where(p => now - p.Value > Lifetime).ToList())
        {
            Announced.TryRemove(old.Key, out _);
        }

        Announced[Key(sessionId, itemId)] = now;
    }

    public static bool Take(MongoId sessionId, string itemId)
    {
        return Announced.TryRemove(Key(sessionId, itemId), out DateTime at) && DateTime.UtcNow - at <= Lifetime;
    }
}

public record DiscardRequest : IRequestData
{
    [JsonPropertyName("item")]
    public string? Item { get; set; }
}

/// <summary>POST /hideoutuncensored/discard {"item":"<id>"} -> {"ok":true}. The next Remove of that item is mailed back.</summary>
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class DiscardRouter(JsonUtil jsonUtil)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<DiscardRequest>(
                "/hideoutuncensored/discard",
                (url, info, sessionId, output, cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(info.Item))
                    {
                        return new ValueTask<string>("{\"ok\":false}");
                    }

                    LeftInHideout.Announce(sessionId, info.Item);
                    return new ValueTask<string>("{\"ok\":true}");
                }
            ),
        ]
    ) { }

/// <summary>
/// InventoryController.DiscardItem handles the client's Remove operation: the item and everything in it leave the
/// profile for good. For an item announced by the hideout, a copy of it (with its contents) is mailed to the player
/// first, the way an insurance return arrives. If the mail cannot be sent the removal is skipped, so the item stays
/// in the profile rather than being lost.
/// </summary>
public class MailBackPatch : AbstractPatch
{
    private const long StorageSeconds = 365L * 24 * 60 * 60;
    private const string MessageText = "You left this in your hideout. Here it is back.";

    internal static ISptLogger<HideoutUncensoredLoader>? Logger;
    internal static MailSendService? Mail;
    internal static ICloner? Cloner;

    protected override MethodBase? GetTargetMethod()
    {
        return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.DiscardItem));
    }

    [PatchPrefix]
    public static bool Prefix(PmcData pmcData, InventoryRemoveRequestData request, MongoId sessionId)
    {
        string itemId = request.Item.ToString();
        if (!LeftInHideout.Take(sessionId, itemId))
        {
            return true;
        }

        try
        {
            // only the PMC's own inventory: not mail attachments, not the scav
            string? ownerId = request.FromOwner?.Id?.ToString();
            if (request.FromOwner?.Type == "Mail" || (ownerId is not null && ownerId != pmcData.Id?.ToString()))
            {
                return true;
            }

            List<Item> items = pmcData.Inventory!.Items!.GetItemWithChildren(request.Item);
            if (items.Count == 0)
            {
                return true;
            }

            List<Item> copies = Cloner!.Clone(items)!;
            Item root = copies.First(x => x.Id == request.Item);
            root.ParentId = null;
            root.SlotId = "hideout";
            root.Location = null;

            Mail!.SendSystemMessageToPlayer(sessionId, MessageText, copies, StorageSeconds);
            Logger?.Info($"[Hideout Uncensored] {items.Count} item(s) left in the hideout were mailed back to {sessionId}");
            return true;
        }
        catch (Exception e)
        {
            Logger?.Error($"[Hideout Uncensored] could not mail item {itemId} back, it stays in the profile: {e}");
            return false;
        }
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class HideoutUncensoredLoader(ISptLogger<HideoutUncensoredLoader> logger, MailSendService mailSendService, ICloner cloner) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        MailBackPatch.Logger = logger;
        MailBackPatch.Mail = mailSendService;
        MailBackPatch.Cloner = cloner;
        new MailBackPatch().Enable();
        logger.Success("[Hideout Uncensored] server part 1.2.0 loaded (hideout discards are mailed back)");
        return Task.CompletedTask;
    }
}
