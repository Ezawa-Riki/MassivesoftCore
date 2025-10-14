using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Utils;
using System.Text.Json.Serialization;
using System.Reflection;
using SPTarkov.Server.Core.Loaders;

namespace MassivesoftCore;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.massivesoft.massivesoftcore";
    public override string Name { get; init; } = "MassivesoftCore";
    public override string Author { get; init; } = "Massivesoft";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");


    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string? License { get; init; } = "MIT";
}
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public class MassivesoftCoreClassLoading(ISptLogger<MassivesoftCoreClassLoading> logger,
MassivesoftCoreClass massivesoftCore) : IOnLoad
{
    public Task OnLoad()
    {
        logger.Info("Massivesoft Core Loaded");
        massivesoftCore.OnLoad();
        return Task.CompletedTask;
    }
}
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 4)]
public class MassivesoftCoreClassCheck(
    ISptLogger<MassivesoftCoreClassLoading> logger,
    MassivesoftCoreClass massivesoftCore,
    BundleLoader bundleLoader,
    JsonUtil jsonUtil
) : IOnLoad
{
    public Task OnLoad()
    {
        logger.Info(jsonUtil.Serialize(bundleLoader.GetBundles()));
        massivesoftCore.OnLoad();
        return Task.CompletedTask;
    }
}
[Injectable(InjectionType.Singleton)]
public class MassivesoftCoreClass
{
    private readonly ISptLogger<MassivesoftCoreClass> _logger;
    private readonly DatabaseService _databaseService;
    private readonly CustomItemService _customItemService;
    private readonly ICloner _cloner;
    private readonly ModHelper _modHelper;
    private readonly JsonUtil _jsonUtil;
    private readonly FileUtil _fileUtil;
    private readonly HandbookHelper _handbookHelper;
    public Dictionary<MongoId, TemplateItem>? DBItems { get; set; }
    public Dictionary<MongoId, Trader>? DBTraders { get; set; }
    public Mastering[]? DBMastering { get; set; }
    public Dictionary<MongoId, Preset>? DBPreset { get; set; }
    public List<HandbookItem>? DBHandbook { get; set; }
    public MongoId DefaultTrader { get; set; } = new MongoId("5a7c2eca46aef81a7ca2145d");
    public string pathToMod = "";
    public MassivesoftCoreClass(ISptLogger<MassivesoftCoreClass> logger, DatabaseService databaseService, CustomItemService customItemService, ICloner cloner, ModHelper modHelper, JsonUtil jsonUtil, FileUtil fileUtil, HandbookHelper handbookHelper)
    {
        _logger = logger;
        _databaseService = databaseService;
        _customItemService = customItemService;
        _cloner = cloner;
        _modHelper = modHelper;
        _jsonUtil = jsonUtil;
        _fileUtil = fileUtil;
        _handbookHelper = handbookHelper;
    }
    public void OnLoad()
    {
        DBItems = _databaseService.GetItems();
        DBTraders = _databaseService.GetTraders();
        DBMastering = _databaseService.GetGlobals().Configuration.Mastering;
        DBPreset = _databaseService.GetGlobals().ItemPresets;
        pathToMod = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        DBHandbook = _databaseService.GetHandbook().Items;
    }
    public void AdvancedCreateItemFromClone(AdvancedNewItemFromCloneDetails details)
    {
        if (details.ItemTplToClone == null)
        {
            _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid ItemTplToClone!");
            return;
        }
        if (details.ParentId == null)
        {
            details.ParentId = DBItems![details.ItemTplToClone.ToString()!].Parent;
        }
        if (details.HandbookParentId == null)
        {
            if (GetHandbookParent(details.ItemTplToClone.ToString()!, out MongoId parentId))
            {
                details.HandbookParentId = parentId;
            }
            else
            {
                _logger.Error($"AdvancedCreateItemFromClone: GetHandbookParent of id {details.ItemTplToClone} failed!");
                return;
            }
        }
        _customItemService.CreateItemFromClone(details);
        if (details.AddToTraders)
        {
            if (details.BarterSchemes == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid BarterSchemes!");
            }
            else
            {
                string TraderId = details.TraderId == null ? DefaultTrader : details.TraderId;
                ItemAddtoTraders(TraderId, details.NewId, details.TraderLoyaltyLevel ?? 1, details.BarterSchemes, details.BuyRestrictionMax ?? 1000);
            }
        }
        if (details.CopySlot)
        {
            if (details.CopySlotsInfo == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid CopySlotsInfo!");
            }
            else
            {
                ItemCopySlot(details.NewId, details.CopySlotsInfo);
            }
        }
        if (details.AddtoModSlots)
        {
            if (details.ModSlot == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid ModSlot!");
            }
            else if (details.ItemTplToClone == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid ItemTplToClone!");
            }
            else
            {
                ModAddtoSlotClone(details.NewId, details.ItemTplToClone.ToString()!, details.ModSlot);
            }
        }
        if (details.AddMasteries)
        {
            if (details.MasterySections == null || details.MasterySections.Length == 0)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid MasterySection!");
            }
            else
            {
                foreach (MasterySection m in details.MasterySections)
                {
                    MongoId[] newTemplates = Array.ConvertAll(m.Templates, tpl => (MongoId)tpl);
                    if (GetMasteringByName(m.Name, out Mastering? mastering))
                    {
                        mastering!.Templates = mastering.Templates.Union(newTemplates);
                    }
                    else
                    {
                        Mastering newMastering = new Mastering
                        {
                            Name = m.Name,
                            Level2 = m.Level2,
                            Level3 = m.Level3,
                            Templates = newTemplates
                        };
                        DBMastering = [.. DBMastering!, newMastering];
                    }
                }
            }
        }
        if (details.AddToPreset)
        {
            if (details.Presets == null || details.Presets.Length == 0)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid Preset!");
            }
            else
            {
                foreach (Preset preset in details.Presets)
                {
                    WeaponAddPreset(preset);
                }
            }
        }
        if (details.AmmoCloneCompatibility)
        {
            AmmoCloneCompitability(details.NewId, details.ItemTplToClone.ToString()!);
        }
        if (details.WeaponCloneChamberCompatibility)
        {
            WeaponCopyChambers(details.NewId, details.ItemTplToClone.ToString()!);
        }
        if (details.MagCloneCartridgeCompatibility)
        {
            MagCopyCartridges(details.NewId, details.ItemTplToClone.ToString()!);
        }
        ProcessItemCartridges(details.NewId);
        ProcessItemChambers(details.NewId);
        ProcessItemSlots(details.NewId);

        string jsonFileName = details.NewId + ".json";
        _fileUtil.WriteFileAsync(System.IO.Path.Combine(pathToMod, jsonFileName), _jsonUtil.Serialize(DBItems![details.NewId], true)!);
    }
    public bool GetHandbookParent(MongoId id, out MongoId parentId)
    {
        parentId = "";
        foreach (var hb in DBHandbook!)
        {
            if (hb.Id == id)
            {
                parentId = hb.ParentId;
                return true;
            }
        }
        return false;
    }
    public bool GetMasteringByName(string masteryName, out Mastering? outMastering)
    {
        outMastering = null;
        foreach (Mastering mastering in DBMastering!)
        {
            if (mastering.Name == masteryName)
            {
                outMastering = mastering;
                return true;
            }
        }
        return false;
    }
    public void WeaponAddMastering(MongoId itemId, string masteryName)
    {
        if (!GetMasteringByName(masteryName, out Mastering? mastering))
        {
            _logger.Error($"WeaponCopyMastering: Mastering of name {masteryName} not found when copying for {itemId}!");
            return;
        }
        if (!mastering!.Templates.Contains(itemId))
        {
            mastering.Templates = mastering.Templates.Append(itemId);
        }
    }
    public void WeaponCopyMastering(MongoId itemId, MongoId itemCopyId)
    {

        foreach (Mastering mastering in DBMastering!)
        {
            if (mastering.Templates.Contains(itemCopyId))
            {
                if (!mastering.Templates.Contains(itemId))
                {
                    mastering.Templates = mastering.Templates.Append(itemId);
                }
                return;
            }
        }
        _logger.Error($"WeaponCopyMastering: Mastering of id {itemCopyId} not found when copying for {itemId}!");
    }
    private void ItemCopySlot(MongoId itemId, CopySoltInfo[] copySoltInfos)
    {
        foreach (var item in copySoltInfos)
        {
            string tgtSlotName = item.TgtSlotName ?? item.NewSoltName;
            if (!ItemGetSlotByName(item.Id, tgtSlotName, out Slot? tgtSlot))
            {
                _logger.Error($"ItemCopySlot: Slot {tgtSlotName} of id {item.Id} not found when adding to {itemId}!");
                continue;
            }
            if (tgtSlot!.Properties == null || tgtSlot!.Properties.Filters == null)
            {
                _logger.Error($"ItemCopySlot: Slot {tgtSlotName} of id {item.Id} is invalid when adding to {itemId}!");
                continue;
            }
            IEnumerable<SlotFilter> filters = _cloner.Clone(tgtSlot.Properties.Filters)!;
            if (item.ItemsAddToSlot != null && item.ItemsAddToSlot.Length > 0)
            {
                filters!.ElementAt(0).Filter!.UnionWith(Array.ConvertAll(item.ItemsAddToSlot, tpl => (MongoId)tpl));
            }
            Slot newslot = new Slot
            {
                Name = item.NewSoltName,
                Id = MongoId.Empty(),
                Parent = itemId,
                Properties = new SlotProperties
                {
                    Filters = filters
                }
            };
            DBItems![itemId].Properties!.Slots = DBItems[itemId].Properties!.Slots!.Append(newslot);
        }
    }
    public void WeaponAddPreset(Preset preset)
    {
        if (DBPreset![preset.Id] == null)
        {
            DBPreset!.TryAdd(preset.Id, preset);
        }
        else
        {
            _logger.Error($"WeaponAddPreset: Weapon preset of id {preset.Id} already exist!");
        }
    }
    public bool ItemGetSlotByName(MongoId itemId, string slotName, out Slot? slotOut)
    {
        slotOut = null;
        TemplateItem tplItem = DBItems![itemId];
        if (!ItemHasValidSolts(tplItem))
        {
            return false;
        }
        foreach (Slot slot in tplItem.Properties!.Slots!)
        {
            if (slot.Name == slotName)
            {
                slotOut = slot;
                return true;
            }
        }
        return false;
    }
    public bool ItemHasValidSolts(TemplateItem tplItem)
    {
        if (tplItem.Type != "Item" || tplItem.Properties == null)
        {
            return false;
        }
        if (tplItem.Properties!.Slots == null || tplItem.Properties.Slots.Count() == 0)
        {
            return false;
        }
        return true;
    }
    public bool ItemHasValidChambers(TemplateItem tplItem)
    {
        if (tplItem.Type != "Item" || tplItem.Properties == null)
        {
            return false;
        }
        if (tplItem.Properties!.Chambers == null || tplItem.Properties.Chambers.Count() == 0)
        {
            return false;
        }
        return true;
    }
    public bool ItemHasValidCartridges(TemplateItem tplItem)
    {
        if (tplItem.Type != "Item" || tplItem.Properties == null)
        {
            return false;
        }
        if (tplItem.Properties!.Cartridges == null || tplItem.Properties.Cartridges.Count() == 0)
        {
            return false;
        }
        return true;
    }
    public void ModAddtoSlotClone(MongoId idtoAdd, MongoId idClone, string[]? modSlotName)
    {

        foreach (KeyValuePair<MongoId, TemplateItem> tplItemEntry in DBItems!)
        {
            TemplateItem tplItem = tplItemEntry.Value;
            if (!ItemHasValidSolts(tplItem))
            {
                continue;
            }
            foreach (Slot slot in tplItem.Properties!.Slots!)
            {
                if (modSlotName == null || modSlotName.Contains(slot.Name))
                {
                    if (slot.Properties?.Filters?.ElementAtOrDefault(0)?.Filter?.Contains(idClone) == true)
                    {
                        slot.Properties!.Filters!.ElementAtOrDefault(0)!.Filter!.Add(idtoAdd);
                    }
                }
            }
        }
    }
    public string ItemAddtoTraders(string traderId, MongoId itemId, int traderLoyaltyLevel, BarterScheme[] barterSchemes, int buyRestrictionMax = 1000, bool unlimitedCount = true, int stackObjectsCount = 9999999)
    {
        if (!GetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"ItemAddtoTraders: Trader with id {traderId} not found when adding {itemId}!");
            return "";
        }
        MongoId assortId = new MongoId(GenerateValidAssortId(itemId));
        Item item = GenerateValidTraderSingleItem(
            assortId,
            itemId,
            buyRestrictionMax,
            unlimitedCount,
            stackObjectsCount
        );
        trader.Assort.Items.Add(item);
        List<BarterScheme> barterSchemesList = barterSchemes.ToList();
        trader.Assort.BarterScheme.TryAdd<MongoId, List<List<BarterScheme>>>(assortId, new List<List<BarterScheme>> { barterSchemesList });
        trader.Assort.LoyalLevelItems.TryAdd<MongoId, int>(assortId, traderLoyaltyLevel);
        return assortId;
    }
    public string ItemAddtoTraders(string traderId, MongoId itemId, int traderLoyaltyLevel, MongoId currency, double price, int buyRestrictionMax = 1000, bool unlimitedCount = true, int stackObjectsCount = 9999999)
    {
        if (!GetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"ItemAddtoTraders: Trader with id {traderId} not found when adding {itemId}!");
            return "";
        }
        MongoId assortId = new MongoId(GenerateValidAssortId(itemId));
        Item item = GenerateValidTraderSingleItem(
            assortId,
            itemId,
            buyRestrictionMax,
            unlimitedCount,
            stackObjectsCount
        );
        trader.Assort.Items.Add(item);
        List<BarterScheme> barterSchemesList = new List<BarterScheme>
        {
            new BarterScheme
            {
                Count = price,
                Template = currency
            }
        };
        trader.Assort.BarterScheme.TryAdd<MongoId, List<List<BarterScheme>>>(assortId, new List<List<BarterScheme>> { barterSchemesList });
        trader.Assort.LoyalLevelItems.TryAdd<MongoId, int>(assortId, traderLoyaltyLevel);
        return assortId;
    }
    private static string GenerateValidAssortId(string itemId)
    {
        char[] assortId = itemId.ToCharArray();
        if (assortId[0] != '3')
        {
            assortId[0] = '3';
        }
        else
        {
            assortId[0] = '4';
        }
        return new string(assortId);
    }
    private static Item GenerateValidTraderSingleItem(MongoId assortId, MongoId tplId, int buyRestrictionMax, bool unlimitedCount, int stackObjectsCount)
    {
        return new Item
        {
            Id = assortId,
            Template = tplId,
            ParentId = "hideout",
            SlotId = "hideout",
            Upd = new Upd
            {
                UnlimitedCount = unlimitedCount,
                StackObjectsCount = stackObjectsCount,
                BuyRestrictionMax = buyRestrictionMax,
                BuyRestrictionCurrent = 0,
            }
        };
    }
    private bool GetTrader(string traderId, out Trader? trader)
    {
        try
        {
            trader = DBTraders![new MongoId(traderId)];
            return true;
        }
        catch (KeyNotFoundException)
        {
            trader = null;
            return false;
        }
    }
    public void TestLogging()
    {
        _logger.Info($"MassivesoftCore logging works!{DBItems?.Count}");
    }
    public void AmmoCloneCompitability(MongoId id, MongoId cloneId)
    {
        foreach (KeyValuePair<MongoId, TemplateItem> tplItemEntry in DBItems!)
        {
            TemplateItem tplItem = tplItemEntry.Value;
            if (ItemHasValidChambers(tplItem))
            {
                foreach (Slot slot in tplItem.Properties!.Chambers!)
                {
                    if (slot.Properties?.Filters?.ElementAtOrDefault(0)?.Filter?.Contains(cloneId) == true)
                    {
                        slot.Properties!.Filters!.ElementAtOrDefault(0)!.Filter!.Add(id);
                    }
                }
            }
            if (ItemHasValidCartridges(tplItem))
            {
                foreach (Slot slot in tplItem.Properties!.Cartridges!)
                {
                    if (slot.Properties?.Filters?.ElementAtOrDefault(0)?.Filter?.Contains(cloneId) == true)
                    {
                        slot.Properties!.Filters!.ElementAtOrDefault(0)!.Filter!.Add(id);
                    }
                }
                if (tplItem.Properties!.Slots == null || tplItem.Properties!.Slots.Count() == 0)
                {
                    continue;
                }
                foreach (Slot slot in tplItem.Properties!.Slots)
                {
                    if (slot.Properties?.Filters?.ElementAtOrDefault(0)?.Filter?.Contains(cloneId) == true)
                    {
                        slot.Properties!.Filters!.ElementAtOrDefault(0)!.Filter!.Add(id);
                    }
                }

            }
        }
    }
    public void ModAddtoSlot(MongoId modId, MongoId tgtId, string slotName)
    {
        TemplateItem tplItem = DBItems![tgtId];
        foreach (Slot slot in tplItem.Properties!.Slots!)
        {
            if (slotName == slot.Name)
            {
                slot.Properties!.Filters!.ElementAtOrDefault(0)!.Filter!.Add(modId);
                return;
            }
        }
        _logger.Error($"ModAddtoSlot: Id {tgtId} has no slot with name {slotName}!");
    }
    public void ProcessItemSlots(MongoId id)
    {
        TemplateItem item = DBItems![id];
        if (!ItemHasValidSolts(item))
        {
            return;
        }
        int indexNumber = 0;
        foreach (Slot s in item.Properties!.Slots!)
        {
            s.Parent = id;
            s.Id = id.ToString().Substring(0, 21) + 'a' + indexNumber.ToString("X2");
        }
    }
    public void ProcessItemCartridges(MongoId id)
    {
        TemplateItem item = DBItems![id];
        if (!ItemHasValidCartridges(item))
        {
            return;
        }
        int indexNumber = 0;
        foreach (Slot s in item.Properties!.Cartridges!)
        {
            s.Parent = id;
            s.Id = id.ToString().Substring(0, 21) + 'b' + indexNumber.ToString("X2");
        }
    }
    public void ProcessItemChambers(MongoId id)
    {
        TemplateItem item = DBItems![id];
        if (!ItemHasValidChambers(item))
        {
            return;
        }
        int indexNumber = 0;
        foreach (Slot s in item.Properties!.Chambers!)
        {
            s.Parent = id;
            s.Id = id.ToString().Substring(0, 21) + 'c' + indexNumber.ToString("X2");
        }
    }


    public void WeaponCopyChambers(MongoId id, MongoId cloneId)
    {
        if (!ItemHasValidChambers(DBItems![cloneId]))
        {
            _logger.Error($"WeaponCopyChambers: Chambers of id {cloneId} not found when copying for {id}!");
            return;
        }
        DBItems![id].Properties!.Chambers = _cloner.Clone(DBItems![cloneId].Properties!.Chambers);
    }
    public void MagCopyCartridges(MongoId id, MongoId cloneId)
    {
        if (!ItemHasValidCartridges(DBItems![cloneId]))
        {
            _logger.Error($"MagCopyCartridges: Cartridges of id {cloneId} not found when copying for {id}!");
            return;
        }
        DBItems![id].Properties!.Cartridges = _cloner.Clone(DBItems![cloneId]!.Properties!.Cartridges!);
    }
}
public record AdvancedNewItemFromCloneDetails : NewItemFromCloneDetails
{
    [JsonPropertyName("addtoTraders")]
    public virtual bool AddToTraders { get; set; } = false;

    [JsonPropertyName("traderId")]
    public virtual string? TraderId { get; set; }

    [JsonPropertyName("traderLoyaltyLevel")]
    public virtual int? TraderLoyaltyLevel { get; set; }

    [JsonPropertyName("barterScheme")]
    public virtual BarterScheme[]? BarterSchemes { get; set; }

    [JsonPropertyName("buyRestrictionMax")]
    public virtual int? BuyRestrictionMax { get; set; }

    [JsonPropertyName("addweaponpreset")]
    public virtual bool AddToPreset { get; set; } = false;

    [JsonPropertyName("weaponpresets")]
    public virtual Preset[]? Presets { get; set; }

    [JsonPropertyName("masteries")]
    public virtual bool AddMasteries { get; set; } = false;

    [JsonPropertyName("masterySections")]
    public virtual MasterySection[]? MasterySections { get; set; }

    [JsonPropertyName("copySlot")]
    public virtual bool CopySlot { get; set; } = false;

    [JsonPropertyName("copySlots")]
    public virtual CopySoltInfo[]? CopySlotsInfo { get; set; }

    [JsonPropertyName("addtoModSlots")]
    public virtual bool AddtoModSlots { get; set; } = false;

    [JsonPropertyName("modSlot")]
    public virtual string[]? ModSlot { get; set; }


    //New Keys
    [JsonPropertyName("ammoCloneCompatibility")]
    public virtual bool AmmoCloneCompatibility { get; set; } = false;

    [JsonPropertyName("weaponCloneChamberCompatibility")]
    public virtual bool WeaponCloneChamberCompatibility { get; set; } = false;

    [JsonPropertyName("magCloneCartridgeCompatibility")]
    public virtual bool MagCloneCartridgeCompatibility { get; set; } = false;

    [JsonPropertyName("newId")]
    public override required string NewId { get; set; }

}

public record CopySoltInfo
{
    [JsonPropertyName("id")]
    public virtual MongoId Id { get; set; }
    [JsonPropertyName("newSoltName")]
    public required virtual string NewSoltName { get; set; }
    [JsonPropertyName("tgtSlotName")]
    public virtual string? TgtSlotName { get; set; }
    [JsonPropertyName("itemsAddtoSlot")]
    public virtual string[]? ItemsAddToSlot { get; set; }
}

public record MasterySection
{
    [JsonPropertyName("name")]
    public required virtual string Name { get; set; }

    [JsonPropertyName("template")]
    public virtual string[] Templates { get; set; } = [];

    [JsonPropertyName("level2")]
    public virtual int Level2 { get; set; }

    [JsonPropertyName("level3")]
    public virtual int Level3 { get; set; }
}