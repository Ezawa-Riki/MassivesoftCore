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
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums;

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
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 10)]
public class MassivesoftCoreClassAfterSubModLoaded(ISptLogger<MassivesoftCoreClassLoading> logger,
MassivesoftCoreClass massivesoftCore) : IOnLoad
{
    public Task OnLoad()
    {
        logger.Info("Massivesoft Core Post Load Process");
        massivesoftCore.PostLoad();
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
    public Dictionary<string, IEnumerable<Buff>>? DBBuff { get; set; }
    public List<HideoutProduction>? DBCrafts { get; set; }
    public MongoId DefaultTrader { get; set; } = new MongoId("5a7c2eca46aef81a7ca2145d");
    public string pathToMod = "";
    public List<MongoId> ListLoadedItem = new List<MongoId>();
    public List<MongoId> ListLoadedAssort = new List<MongoId>();
    private readonly List<PresetToTraderInfo> PresetToTraderInfos = new List<PresetToTraderInfo>();
    private readonly Dictionary<string, Dictionary<MongoId, List<MongoId>>> InfoCompatibilityMapping = new Dictionary<string, Dictionary<MongoId, List<MongoId>>>();
    public string strAllSlots = "AllSlots";
    public string strConfilctingItems = "ConfilctingItems";
    public string strAmmo = "Ammo";
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
        DBBuff = _databaseService.GetGlobals().Configuration.Health.Effects.Stimulator.Buffs;
        DBCrafts = _databaseService.GetHideout().Production.Recipes;
        InfoCompatibilityMapping.Add(strAmmo, new Dictionary<MongoId, List<MongoId>>());
        InfoCompatibilityMapping.Add(strAllSlots, new Dictionary<MongoId, List<MongoId>>());
        InfoCompatibilityMapping.Add(strConfilctingItems, new Dictionary<MongoId, List<MongoId>>());
    }
    public void PostLoad()
    {
        ProcessCompatibilityInfo();
        ProcessPresetToTrader();
    }
    public void AdvancedCreateItemFromClone(AdvancedNewItemFromCloneDetails details)
    {
        string traderId = details.TraderId ?? DefaultTrader;

        if (ListLoadedItem.Contains(details.NewId))
        {
            _logger.Error($"AdvancedCreateItemFromClone: Id {details.NewId} duplicated!");
            return;
        }
        if (details.ItemTplToClone == null)
        {
            _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has null ItemTplToClone!");
            return;
        }
        if (DBItems?.ContainsKey(details.ItemTplToClone.ToString()!) != true)
        {
            _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid ItemTplToClone!");
            return;
        }

        details.ParentId ??= DBItems![details.ItemTplToClone.ToString()!].Parent;

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
            else if (details.AddPresetInsteadOfItem)
            {
                if (details.PresetIdToAdd == null)
                {
                    _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} AddPresetInsteadOfItem is set but PresetIdToAdd not provided!");
                }
                else
                {
                    PresetAddtoTraders(traderId, details.PresetIdToAdd, details.TraderLoyaltyLevel ?? 1, details.BarterSchemes, details.BuyRestrictionMax ?? 1000);
                }
            }
            else
            {

                ItemAddtoTrader(traderId, details.NewId, details.TraderLoyaltyLevel ?? 1, details.BarterSchemes, details.BuyRestrictionMax ?? 1000);
            }
        }
        if (details.CopySlot)
        {
            if (details.CopySlotsInfo == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid copySlots!");
            }
            else
            {
                ItemCopySlot(details.NewId, details.CopySlotsInfo);
            }
        }
        if (details.AddSlot)
        {
            if (details.SlotsToAdd == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid addSlots!");
            }
            else
            {
                ItemAddSlot(details.NewId, details.SlotsToAdd);
            }
        }
        if (details.AddtoModSlots)
        {
            if (details.ItemTplToClone == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid ItemTplToClone!");
            }
            else
            {
                ModAddtoSlotClone(details.NewId, details.ItemTplToClone.ToString()!, details.ModSlot, details.AddtoConflicts);
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
        if (details.AddBuffs)
        {
            if (details.Buffs == null || details.Buffs.Count == 0)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid buffs!");
            }
            else
            {
                AddBuffs(details.Buffs);
            }
        }
        if (details.AddCrafts)
        {
            if (details.Crafts == null || details.Crafts.Length == 0)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid crafts!");
            }
            else
            {
                AddCrafts(details.Crafts);
            }
        }
        if (details.AdditionalAssortData != null)
        {
            var assort = details.AdditionalAssortData;
            if (assort.BarterScheme == null || assort.Items == null || assort.LoyalLevelItems == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdditionalAssortData of {details.NewId} is invalid!");
            }
            else
            {
                AssortsAddtoTrader(traderId, details.AdditionalAssortData);
            }
        }
        if (details.ScriptedConflictingInfos != null)
        {
            AddScriptedConflictingList(details.NewId, details.ScriptedConflictingInfos);
        }

        ProcessItemSlots(details.NewId);

        ListLoadedItem.Add(details.NewId);

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
    private void ItemCopySlot(MongoId itemId, CopySlotInfo[] copySlotInfos)
    {
        var slots = DBItems![itemId].Properties!.Slots!;
        List<Slot> newslots = new List<Slot>();
        foreach (var item in copySlotInfos)
        {
            string tgtSlotName = item.TgtSlotName ?? item.NewSlotName;

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
                Name = item.NewSlotName,
                Id = MongoId.Empty(),
                Parent = itemId,
                Properties = new SlotProperties
                {
                    Filters = filters
                },
                Required = item.Required ?? tgtSlot.Required,
                MergeSlotWithChildren = tgtSlot.MergeSlotWithChildren,
                Prototype = tgtSlot.Prototype
            };
            newslots.Add(newslot);
        }
        if (slots is List<Slot> slotsList)
        {
            slotsList.AddRange(newslots);
        }

    }
    private void ItemAddSlot(MongoId itemId, Slot[] newSlots)
    {
        var slots = DBItems![itemId].Properties?.Slots;
        if (slots is List<Slot> slotsList)
        {
            slotsList.AddRange(newSlots);
        }
    }
    public void WeaponAddPreset(Preset preset)
    {
        foreach (var item in preset.Items)
        {
            item.ParentId = item.ParentId?.ToLower();
        }
        if (!DBPreset!.TryAdd(preset.Id, preset))
        {
            _logger.Error($"WeaponAddPreset: Weapon preset of id {preset.Id} already exist!");
        }
    }
    public bool ItemGetSlotByName(MongoId itemId, string slotName, out Slot? slotOut)
    {
        slotOut = null;
        if (TryGetItem(itemId, out TemplateItem? tplItem) != true)
        {
            _logger.Error($"ItemGetSlotByName: Item of id {itemId} not found!");
            return false;
        }
        if (tplItem == null)
        {
            _logger.Error($"ItemGetSlotByName: Item of id {itemId} is null!");
            return false;
        }
        if (!ItemHasValidSlots(tplItem))
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
    public bool ItemHasValidSlots(TemplateItem tplItem)
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
    public void ModAddtoSlotClone(MongoId idtoAdd, MongoId cloneId, string[]? modSlotName, bool CloneConflicts = false)
    {
        if (CloneConflicts)
        {
            var innerDict = InfoCompatibilityMapping[strConfilctingItems];

            if (!innerDict.TryGetValue(cloneId, out var idList))
            {
                idList = new List<MongoId>();
                innerDict[cloneId] = idList;
            }

            idList.Add(idtoAdd);
        }
        if (modSlotName == null)
        {
            var innerDict = InfoCompatibilityMapping[strAllSlots];

            if (!innerDict.TryGetValue(cloneId, out var idList))
            {
                idList = new List<MongoId>();
                innerDict[cloneId] = idList;
            }

            idList.Add(idtoAdd);
        }
        else
        {
            foreach (string name in modSlotName)
            {
                if (!InfoCompatibilityMapping.TryGetValue(name, out var innerDict))
                {
                    innerDict = new Dictionary<MongoId, List<MongoId>>();
                    InfoCompatibilityMapping[name] = innerDict;
                }

                if (!innerDict.TryGetValue(cloneId, out var idList))
                {
                    idList = new List<MongoId>();
                    innerDict[cloneId] = idList;
                }

                idList.Add(idtoAdd);
            }
        }
    }
    public void ItemAddtoTrader(string traderId, MongoId itemId, int traderLoyaltyLevel, BarterScheme[] barterSchemes, int buyRestrictionMax = 1000, bool unlimitedCount = true, int stackObjectsCount = 9999999)
    {
        if (!TryGetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"ItemAddtoTraders: Trader with id {traderId} not found when adding {itemId}!");
            return;
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
        ListLoadedAssort.Add(assortId);
    }
    public void ItemAddtoTrader(string traderId, MongoId itemId, int traderLoyaltyLevel, MongoId currency, double price, int buyRestrictionMax = 1000, bool unlimitedCount = true, int stackObjectsCount = 9999999)
    {
        if (!TryGetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"ItemAddtoTraders: Trader with id {traderId} not found when adding {itemId}!");
            return;
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
        ListLoadedAssort.Add(assortId);
    }
    public void AssortsAddtoTrader(string traderId, TraderAssort assort)
    {
        if (!TryGetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"AssortsAddtoTraders: Trader with id {traderId} not found!");
            return;
        }
        List<MongoId> validAssorts = new List<MongoId>();
        foreach (KeyValuePair<MongoId, int> loyalty in assort.LoyalLevelItems)
        {
            if (assort.BarterScheme[loyalty.Key] == null)
            {
                _logger.Error($"AssortsAddtoTraders: Check assort {loyalty.Key} no respective BarterScheme!");
                continue;
            }
            trader.Assort.BarterScheme.TryAdd<MongoId, List<List<BarterScheme>>>(loyalty.Key, assort.BarterScheme[loyalty.Key]);
            trader.Assort.LoyalLevelItems.TryAdd<MongoId, int>(loyalty.Key, loyalty.Value);
            validAssorts.Add(loyalty.Key);
        }
        List<MongoId> validItems = new List<MongoId>();
        foreach (Item it in assort.Items)
        {
            if (ListLoadedAssort.Contains(it.Id))
            {
                _logger.Error($"AssortsAddtoTraders: Check assort item {it.Id} duplicated!");
                continue;
            }
            trader.Assort.Items.Add(it);
            ListLoadedAssort.Add(it.Id);
            validItems.Add(it.Id);
        }
        foreach (MongoId id in validAssorts)
        {
            if (!validItems.Contains(id))
            {
                _logger.Error($"AssortsAddtoTraders: Check assort {id} no respective Item!");
            }
        }
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
    private bool TryGetTrader(string traderId, out Trader? trader)
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
    private bool TryGetItem(MongoId id, out TemplateItem? tplItem)
    {
        try
        {
            tplItem = DBItems![id];
            return true;
        }
        catch (KeyNotFoundException)
        {
            tplItem = null;
            return false;
        }
    }
    public void AmmoCloneCompitability(MongoId id, MongoId cloneId)
    {
        {
            var innerDict = InfoCompatibilityMapping[strAllSlots];

            if (!innerDict.TryGetValue(cloneId, out var idList))
            {
                idList = new List<MongoId>();
                innerDict[cloneId] = idList;
            }

            idList.Add(id);
        }
        {
            var innerDict = InfoCompatibilityMapping[strAmmo];

            if (!innerDict.TryGetValue(cloneId, out var idList))
            {
                idList = new List<MongoId>();
                innerDict[cloneId] = idList;
            }

            idList.Add(id);
        }
    }
    public void ModAddtoSlot(MongoId modId, TemplateItem tplItem, string slotName)
    {
        foreach (Slot slot in tplItem.Properties!.Slots!)
        {
            if (slotName == slot.Name)
            {
                slot.Properties!.Filters!.ElementAtOrDefault(0)!.Filter!.Add(modId);
                return;
            }
        }
        _logger.Error($"ModAddtoSlot: Id {tplItem.Id} has no slot with name {slotName}!");
    }
    public void ProcessItemSlots(MongoId id)
    {
        TemplateItem item = DBItems![id];

        int indexNumber = 0;
        if (ItemHasValidSlots(item))
        {
            indexNumber = 0;
            foreach (Slot s in item.Properties!.Slots!)
            {
                s.Parent = id;
                s.Id = id.ToString().Substring(0, 21) + 'a' + indexNumber.ToString("X2");
            }
        }
        if (ItemHasValidCartridges(item))
        {
            indexNumber = 0;
            foreach (Slot s in item.Properties!.Cartridges!)
            {
                s.Parent = id;
                s.Id = id.ToString().Substring(0, 21) + 'b' + indexNumber.ToString("X2");
            }
        }
        if (ItemHasValidChambers(item))
        {
            indexNumber = 0;
            foreach (Slot s in item.Properties!.Chambers!)
            {
                s.Parent = id;
                s.Id = id.ToString().Substring(0, 21) + 'c' + indexNumber.ToString("X2");
            }
        }
    }
    public void AddBuffs(Dictionary<string, Buff[]> Buffs)
    {
        foreach (KeyValuePair<string, Buff[]> Buff in Buffs)
        {
            DBBuff!.TryAdd(Buff.Key, Buff.Value);
        }
    }
    public void AddCrafts(HideoutProduction[] Crafts)
    {
        DBCrafts!.AddRange(Crafts);
    }
    public void WeaponCopyChambers(MongoId id, MongoId cloneId)
    {
        if (TryGetItem(cloneId, out TemplateItem? cloneItemTpl) != true)
        {
            _logger.Error($"WeaponCopyChambers: Item of id {cloneId} not found!");
            return;
        }
        if (cloneItemTpl == null)
        {
            _logger.Error($"WeaponCopyChambers: Item of id {cloneId} is null!");
            return;
        }
        if (!ItemHasValidChambers(cloneItemTpl))
        {
            _logger.Error($"WeaponCopyChambers: Chambers of id {cloneId} not found when copying for {id}!");
            return;
        }
        var filter = _cloner.Clone(cloneItemTpl.Properties!.Chambers!.ElementAt(0)!.Properties!.Filters!.ElementAt(0).Filter);
        foreach (var chamber in DBItems![id].Properties!.Chambers!)
        {
            chamber.Properties!.Filters!.ElementAt(0).Filter = _cloner.Clone(filter);
        }
    }
    public void MagCopyCartridges(MongoId id, MongoId cloneId)
    {
        if (TryGetItem(cloneId, out TemplateItem? cloneItemTpl) != true)
        {
            _logger.Error($"MagCopyCartridges: Item of id {cloneId} not found!");
            return;
        }
        if (cloneItemTpl == null)
        {
            _logger.Error($"MagCopyCartridges: Item of id {cloneId} is null!");
            return;
        }
        if (!ItemHasValidCartridges(cloneItemTpl))
        {
            _logger.Error($"MagCopyCartridges: Cartridges of id {cloneId} not found when copying for {id}!");
            return;
        }
        DBItems![id].Properties!.Cartridges!.ElementAt(0).Properties!.Filters!.ElementAt(0).Filter = _cloner.Clone(cloneItemTpl.Properties!.Cartridges!.ElementAt(0).Properties!.Filters!.ElementAt(0).Filter);
    }
    private void AddTraderAssortFromPreset(MongoId traderId, MongoId presetId, int traderLoyaltyLevel, BarterScheme[] barterSchemes, int buyRestrictionMax = 1000, bool unlimitedCount = true, int stackObjectsCount = 9999999)
    {
        if (!TryGetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"PresetAddtoTraders: Trader with id {traderId} not found when adding {presetId}!");
            return;
        }
        if (DBPreset?.TryGetValue(presetId, out Preset? preset) != true)
        {
            _logger.Error($"PresetAddtoTraders: Weapon Preset of id {presetId} does not exist!");
            return;
        }
        if (preset == null)
        {
            _logger.Error($"PresetAddtoTraders: Weapon Preset of id {presetId} is invalid!");
            return;
        }
        Item[] items = _cloner.Clone(preset.Items)!.ToArray();
        string prefix = "d";
        MongoId assortId = prefix + preset.Parent.ToString().Substring(prefix.Length);
        foreach (var it in items!)
        {
            it.Id = prefix + it.Id.ToString().Substring(prefix.Length);
            if (ListLoadedAssort.Contains(it.Id))
            {
                _logger.Error($"PresetAddtoTraders: Assort item id {it.Id} duplicated!");
                return;
            }
            ListLoadedAssort.Add(it.Id);
            if (it.ParentId != null)
            {
                it.ParentId = prefix + it.ParentId.Substring(prefix.Length);
            }
            if (it.Id == assortId)
            {
                it.ParentId = "hideout";
                it.SlotId = "hideout";
                it.Upd = new Upd
                {
                    UnlimitedCount = unlimitedCount,
                    StackObjectsCount = stackObjectsCount,
                    BuyRestrictionMax = buyRestrictionMax
                };
            }
            trader.Assort.Items.Add(it);
        }
        trader.Assort.BarterScheme.TryAdd<MongoId, List<List<BarterScheme>>>(assortId, new List<List<BarterScheme>> { barterSchemes.ToList() });
        trader.Assort.LoyalLevelItems.TryAdd<MongoId, int>(assortId, traderLoyaltyLevel);
    }
    public void PresetAddtoTraders(string traderId, MongoId presetId, int traderLoyaltyLevel, BarterScheme[] barterSchemes, int buyRestrictionMax = 1000, bool unlimitedCount = true, int stackObjectsCount = 9999999)
    {
        PresetToTraderInfos.Add(new PresetToTraderInfo(traderId, presetId, traderLoyaltyLevel, barterSchemes, buyRestrictionMax, unlimitedCount, stackObjectsCount));
    }
    private void ProcessCompatibilityInfo()
    {
        foreach (KeyValuePair<MongoId, TemplateItem> tplItemEntry in DBItems!)
        {
            var tplItem = tplItemEntry.Value;
            if (tplItem.Type != "Item" || tplItem.Properties == null)
            {
                continue;
            }
            // Process ConflictingItems Info
            if (tplItem.Properties.ConflictingItems?.Count > 0)
            {
                ApplyInfoCompatibility(strConfilctingItems, tplItem.Properties.ConflictingItems);
            }
            // Process Slots
            if (tplItem.Properties.Slots?.Count() > 0)
            {
                foreach (Slot slot in tplItem.Properties.Slots)
                {
                    if (slot.Properties?.Filters?.ElementAt(0)?.Filter == null)
                    {
                        continue;
                    }
                    if (slot.Name != null)
                    {
                        ApplyInfoCompatibility(slot.Name, slot.Properties!.Filters!.ElementAt(0).Filter!);
                    }
                    ApplyInfoCompatibility(strAllSlots, slot.Properties!.Filters!.ElementAt(0).Filter!);
                }
            }
            // Process Chambers
            if (tplItem.Properties.Chambers?.Count() > 0)
            {
                foreach (Slot chamber in tplItem.Properties.Chambers)
                {
                    if (chamber.Properties?.Filters?.ElementAt(0)?.Filter != null)
                    {
                        ApplyInfoCompatibility(strAmmo, chamber.Properties.Filters.ElementAt(0).Filter!);
                    }
                }
            }
            // Process Cartridges
            if (tplItem.Properties.Cartridges?.Count() > 0)
            {
                foreach (Slot cart in tplItem.Properties.Cartridges)
                {
                    if (cart.Properties?.Filters?.ElementAt(0)?.Filter != null)
                    {
                        ApplyInfoCompatibility(strAmmo, cart.Properties.Filters.ElementAt(0).Filter!);
                    }
                }
            }
        }
    }

    private void ApplyInfoCompatibility(string tableName, HashSet<MongoId> slotFilter)
    {
        if (InfoCompatibilityMapping.TryGetValue(tableName, out Dictionary<MongoId, List<MongoId>>? dict) != true)
        {
            return;
        }
        foreach (KeyValuePair<MongoId, List<MongoId>> addList in dict)
        {
            if (slotFilter.Contains(addList.Key))
            {
                slotFilter.UnionWith(addList.Value);
            }
        }
    }

    private void ProcessPresetToTrader()
    {
        foreach (PresetToTraderInfo info in PresetToTraderInfos)
        {
            AddTraderAssortFromPreset(info.TraderId, info.PresetId, info.TraderLoyaltyLevel, info.BarterSchemes, info.BuyRestrictionMax, info.UnlimitedCount, info.StackObjectsCount);
        }
        string jsonFileName = "t.json";
        _fileUtil.WriteFileAsync(System.IO.Path.Combine(pathToMod, jsonFileName), _jsonUtil.Serialize(DBTraders![DefaultTrader].Assort, true)!);
    }
    public void AddScriptedConflictingList(MongoId itemId, ConflictingInfos[] conflictingInfos)
    {
        var itemConflictingItems = DBItems![itemId].Properties!.ConflictingItems!;
        foreach (var item in conflictingInfos)
        {
            string tgtSlotName = item.TgtSlotName;
            if (!ItemGetSlotByName(item.Id, tgtSlotName, out Slot? tgtSlot))
            {
                _logger.Error($"AddScriptedConflictingList: Slot {tgtSlotName} of id {item.Id} not found when adding to {itemId}!");
                continue;
            }
            if (tgtSlot!.Properties == null || tgtSlot!.Properties.Filters == null)
            {
                _logger.Error($"AddScriptedConflictingList: Slot {tgtSlotName} of id {item.Id} is invalid when adding to {itemId}!");
                continue;
            }
            HashSet<MongoId> filters = _cloner.Clone(tgtSlot.Properties.Filters.ElementAt(0).Filter)!;
            if (item.ItemsAddToSlot != null && item.ItemsAddToSlot.Length > 0)
            {
                filters.UnionWith(Array.ConvertAll(item.ItemsAddToSlot, tpl => (MongoId)tpl));
            }
            itemConflictingItems.UnionWith(filters);
        }
    }
    private record PresetToTraderInfo
    {
        public MongoId TraderId { get; set; }
        public MongoId PresetId { get; set; }
        public int TraderLoyaltyLevel { get; set; }
        public BarterScheme[] BarterSchemes { get; set; }
        public int BuyRestrictionMax { get; set; }
        public bool UnlimitedCount { get; set; }
        public int StackObjectsCount { get; set; }

        public PresetToTraderInfo(string traderId, MongoId presetId, int traderLoyaltyLevel, BarterScheme[] barterSchemes, int buyRestrictionMax = 1000, bool unlimitedCount = true, int stackObjectsCount = 9999999)
        {
            TraderId = traderId;
            PresetId = presetId;
            TraderLoyaltyLevel = traderLoyaltyLevel;
            BarterSchemes = barterSchemes;
            BuyRestrictionMax = buyRestrictionMax;
            UnlimitedCount = unlimitedCount;
            StackObjectsCount = stackObjectsCount;
        }
    }
}
public record AdvancedNewItemFromCloneDetails : NewItemFromCloneDetails
{

    //Trade infos
    [JsonPropertyName("addtoTraders")]
    public virtual bool AddToTraders { get; set; } = false;

    [JsonPropertyName("addPresetInsteadOfItem")]
    public virtual bool AddPresetInsteadOfItem { get; set; } = false;

    [JsonPropertyName("presetIdToAdd")]
    public virtual string? PresetIdToAdd { get; set; }

    [JsonPropertyName("traderId")]
    public virtual string? TraderId { get; set; }

    [JsonPropertyName("traderLoyaltyLevel")]
    public virtual int? TraderLoyaltyLevel { get; set; }

    [JsonPropertyName("barterScheme")]
    public virtual DeserializationBarterScheme[]? BarterSchemes { get; set; }

    [JsonPropertyName("buyRestrictionMax")]
    public virtual int? BuyRestrictionMax { get; set; }

    //Weapon preset adding
    [JsonPropertyName("addweaponpreset")]
    public virtual bool AddToPreset { get; set; } = false;

    [JsonPropertyName("weaponpresets")]
    public virtual Preset[]? Presets { get; set; }

    //Mastering adding
    [JsonPropertyName("masteries")]
    public virtual bool AddMasteries { get; set; } = false;

    [JsonPropertyName("masterySections")]
    public virtual MasterySection[]? MasterySections { get; set; }

    //Copy slots from other items
    [JsonPropertyName("copySlot")]
    public virtual bool CopySlot { get; set; } = false;

    [JsonPropertyName("copySlots")]
    public virtual CopySlotInfo[]? CopySlotsInfo { get; set; }


    //Add slots
    [JsonPropertyName("addSlot")]
    public virtual bool AddSlot { get; set; } = false;

    [JsonPropertyName("addSlots")]
    public virtual Slot[]? SlotsToAdd { get; set; }

    //Add to other items' slots
    [JsonPropertyName("addtoModSlots")]
    public virtual bool AddtoModSlots { get; set; } = false;

    [JsonPropertyName("modSlot")]
    public virtual string[]? ModSlot { get; set; }

    //Add to other items Conflicting Items
    [JsonPropertyName("addtoConflicts")]
    public virtual bool AddtoConflicts { get; set; } = false;

    //Add stimulator buffs
    [JsonPropertyName("addBuffs")]
    public virtual bool AddBuffs { get; set; } = false;

    [JsonPropertyName("buffs")]
    public virtual Dictionary<string, Buff[]>? Buffs { get; set; }

    //Add hideout crafting productions
    [JsonPropertyName("addCrafts")]
    public virtual bool AddCrafts { get; set; } = false;


    [JsonPropertyName("crafts")]
    public virtual HideoutProduction[]? Crafts { get; set; }

    [JsonPropertyName("additionalAssortData")]
    public virtual TraderAssort? AdditionalAssortData { get; set; }

    //Solve ammo, weapon and mag compatibilities
    [JsonPropertyName("ammoCloneCompatibility")]
    public virtual bool AmmoCloneCompatibility { get; set; } = false;

    [JsonPropertyName("weaponCloneChamberCompatibility")]
    public virtual bool WeaponCloneChamberCompatibility { get; set; } = false;

    [JsonPropertyName("magCloneCartridgeCompatibility")]
    public virtual bool MagCloneCartridgeCompatibility { get; set; } = false;

    [JsonPropertyName("scriptedConflictingInfos")]
    public virtual ConflictingInfos[]? ScriptedConflictingInfos { get; set; }

    //Aditional 
    [JsonPropertyName("newId")]
    public override required string NewId { get; set; }

}
public record ConflictingInfos
{

    [JsonPropertyName("id")]
    public virtual MongoId Id { get; set; }
    [JsonPropertyName("tgtSlotName")]
    public virtual required string TgtSlotName { get; set; }
    [JsonPropertyName("itemsAddtoSlot")]
    public virtual string[]? ItemsAddToSlot { get; set; }
}
public record CopySlotInfo
{
    [JsonPropertyName("id")]
    public virtual MongoId Id { get; set; }
    [JsonPropertyName("newSlotName")]
    public required virtual string NewSlotName { get; set; }
    [JsonPropertyName("tgtSlotName")]
    public virtual string? TgtSlotName { get; set; }
    [JsonPropertyName("itemsAddtoSlot")]
    public virtual string[]? ItemsAddToSlot { get; set; }
    [JsonPropertyName("required")]
    public virtual bool? Required { get; set; }
}

public record MasterySection
{
    [JsonPropertyName("Name")]
    public required virtual string Name { get; set; }

    [JsonPropertyName("Template")]
    public virtual string[] Templates { get; set; } = [];

    [JsonPropertyName("Level2")]
    public virtual int Level2 { get; set; }

    [JsonPropertyName("Level3")]
    public virtual int Level3 { get; set; }
}

public record DeserializationBarterScheme : BarterScheme
{
    private static readonly MongoId DefaultTemplate = Money.ROUBLES;

    [JsonPropertyName("_tpl")]
    public override MongoId Template
    {
        get => base.Template;
        set => base.Template = value;
    }

    public DeserializationBarterScheme()
    {

        if (base.Template == default(MongoId))
        {
            base.Template = DefaultTemplate;
        }
    }
}