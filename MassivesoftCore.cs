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
using SPTarkov.Server.Core.Models.Spt.Server;

namespace MassivesoftCore;

#region Mod Metadata Definition
/// <summary>
/// Metadata definition for MassivesoftCore mod
/// Contains core information about the mod such as GUID, version, dependencies and compatibility
/// </summary>
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
#endregion

#region Mod Initialization Classes
/// <summary>
/// Core mod loading handler that executes on initial load
/// Triggers core initialization logic after game server database loading completes
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public class MassivesoftCoreClassLoading(ISptLogger<MassivesoftCoreClassLoading> logger, MassivesoftCoreClass massivesoftCore) : IOnLoad
{
    public Task OnLoad()
    {
        logger.Info("Massivesoft Core Loaded");
        massivesoftCore.OnLoad();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Post-load processing handler for MassivesoftCore
/// Executes additional logic after sub-mods are loaded
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 10)]
public class MassivesoftCoreClassAfterSubModLoaded(ISptLogger<MassivesoftCoreClassLoading> logger, MassivesoftCoreClass massivesoftCore) : IOnLoad
{
    public Task OnLoad()
    {
        logger.Info("Massivesoft Core Post Load Process");
        massivesoftCore.PostLoad();
        return Task.CompletedTask;
    }
}
#endregion

#region Core Business Logic Class
/// <summary>
/// Core business logic class for MassivesoftCore mod
/// </summary>
[Injectable(InjectionType.Singleton)]
public class MassivesoftCoreClass
{
    #region Constants
    // Default constant values used throughout the core functionality
    private const string STR_ALL_SLOTS = "AllSlots";
    private const string STR_CONFLICTING_ITEMS = "ConfilctingItems";
    private const string STR_AMMO = "Ammo";
    private readonly MongoId _playerInventoryId = new("55d7217a4bdc2d86028b456d");
    #endregion

    #region Dependencies
    private readonly ISptLogger<MassivesoftCoreClass> _logger;
    private readonly DatabaseService _databaseService;
    private readonly CustomItemService _customItemService;
    private readonly ICloner _cloner;
    private readonly ModHelper _modHelper;
    private readonly JsonUtil _jsonUtil;
    private readonly FileUtil _fileUtil;
    private readonly HandbookHelper _handbookHelper;
    #endregion

    #region Database Collections
    // Cached database collections for quick access
    public Dictionary<MongoId, TemplateItem>? DBItems { get; set; }
    public Dictionary<MongoId, Trader>? DBTraders { get; set; }
    public Globals? DBGlobals { get; set; }
    public Dictionary<MongoId, Preset>? DBPreset { get; set; }
    public List<HandbookItem>? DBHandbook { get; set; }
    public Dictionary<string, IEnumerable<Buff>>? DBBuff { get; set; }
    public List<HideoutProduction>? DBCrafts { get; set; }
    public LocaleBase? DBlocales { get; set; }
    #endregion

    #region Runtime State
    // Runtime state variables and tracking collections
    public string PathToMod = "";
    public MongoId DefaultTrader { get; set; } = new MongoId("5a7c2eca46aef81a7ca2145d");
    public List<MongoId> ListLoadedItem = new();
    public List<MongoId> ListLoadedAssort = new();
    private readonly List<PresetToTraderInfo> _presetToTraderInfos = new();
    private readonly Dictionary<string, Dictionary<MongoId, List<MongoId>>> _infoCompatibilityMapping = new();
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the MassivesoftCoreClass
    /// </summary>
    /// <param name="logger">Logging service</param>
    /// <param name="databaseService">Database access service</param>
    /// <param name="customItemService">Custom item management service</param>
    /// <param name="cloner">Object cloning utility</param>
    /// <param name="modHelper">Mod helper utility</param>
    /// <param name="jsonUtil">JSON serialization utility</param>
    /// <param name="fileUtil">File system utility</param>
    /// <param name="handbookHelper">Handbook management helper</param>
    public MassivesoftCoreClass(
        ISptLogger<MassivesoftCoreClass> logger,
        DatabaseService databaseService,
        CustomItemService customItemService,
        ICloner cloner,
        ModHelper modHelper,
        JsonUtil jsonUtil,
        FileUtil fileUtil,
        HandbookHelper handbookHelper)
    {
        _logger = logger;
        _databaseService = databaseService;
        _customItemService = customItemService;
        _cloner = cloner;
        _modHelper = modHelper;
        _jsonUtil = jsonUtil;
        _fileUtil = fileUtil;
        _handbookHelper = handbookHelper;

        // Initialize compatibility mapping collections
        _infoCompatibilityMapping.Add(STR_AMMO, new Dictionary<MongoId, List<MongoId>>());
        _infoCompatibilityMapping.Add(STR_ALL_SLOTS, new Dictionary<MongoId, List<MongoId>>());
        _infoCompatibilityMapping.Add(STR_CONFLICTING_ITEMS, new Dictionary<MongoId, List<MongoId>>());
    }
    #endregion

    #region Initialization Methods
    /// <summary>
    /// Initializes core mod data and loads database collections
    /// Called during initial mod loading phase
    /// </summary>
    public void OnLoad()
    {
        // Load core database collections
        DBItems = _databaseService.GetItems();
        DBTraders = _databaseService.GetTraders();
        DBGlobals = _databaseService.GetGlobals();
        DBPreset = _databaseService.GetGlobals().ItemPresets;
        PathToMod = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        DBHandbook = _databaseService.GetHandbook().Items;
        DBBuff = _databaseService.GetGlobals().Configuration.Health.Effects.Stimulator.Buffs;
        DBCrafts = _databaseService.GetHideout().Production.Recipes;
        DBlocales = _databaseService.GetLocales();
    }

    /// <summary>
    /// Executes post-load processing logic
    /// Handles compatibility processing and preset-to-trader mappings
    /// </summary>
    public void PostLoad()
    {
        ProcessCompatibilityInfo();
        ProcessPresetToTrader();
    }
    #endregion

    #region Core Item Management
    /// <summary>
    /// Advanced item creation method that clones existing items with custom modifications
    /// </summary>
    /// <param name="details">Detailed configuration for the new cloned item</param>
    public void AdvancedCreateItemFromClone(AdvancedNewItemFromCloneDetails details)
    {
        string traderId = details.TraderId ?? DefaultTrader;

        // Validate duplicate item ID
        if (ListLoadedItem.Contains(details.NewId))
        {
            _logger.Error($"AdvancedCreateItemFromClone: Id {details.NewId} duplicated!");
            return;
        }

        // Validate source item template
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

        // Set parent ID to the same as the source item if not specified
        details.ParentId ??= DBItems![details.ItemTplToClone.ToString()!].Parent;

        // Resolve handbook parent ID, setting it to the same as the source item if not specified
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

        // Create base cloned item using SPT's custom item service
        _customItemService.CreateItemFromClone(details);

        // Handle trader assort
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
                    PresetAddtoTraders(
                        traderId,
                        details.PresetIdToAdd,
                        details.TraderLoyaltyLevel ?? 1,
                        details.BarterSchemes,
                        details.BuyRestrictionMax ?? 1000);
                }
            }
            else
            {
                ItemAddtoTrader(
                    traderId,
                    details.NewId,
                    details.TraderLoyaltyLevel ?? 1,
                    details.BarterSchemes,
                    details.BuyRestrictionMax ?? 1000);
            }
        }

        // Handle slot copying
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

        // Handle slot addition
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

        // Handle mod slot integration, adding it to others items's mod slots
        if (details.AddtoModSlots)
        {
            if (details.ItemTplToClone == null)
            {
                _logger.Error($"AdvancedCreateItemFromClone: AdvancedNewItemFromCloneDetails of id {details.NewId} has invalid ItemTplToClone!");
            }
            else
            {
                string cloneId = details.AddtoModSlotsCloneID ?? details.ItemTplToClone.ToString()!;
                ModAddtoSlotClone(details.NewId, cloneId, details.ModSlot, details.AddtoConflicts);
            }
        }

        // Handle mastery configuration, adding the new item to existing mastery sections or creating new ones based on details
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
                    List<MongoId> newTemplates = new();
                    foreach (var item in m.Templates)
                    {
                        newTemplates.Add(new MongoId(item));
                    }

                    if (GetMasteringByName(m.Name, out Mastering? mastering))
                    {
                        if (mastering!.Templates is List<MongoId> tpls)
                        {
                            foreach (var id in newTemplates)
                            {
                                if (!tpls.Contains(id))
                                {
                                    tpls.Add(id);
                                }
                            }
                        }
                    }
                    else
                    {
                        // Create new mastery section if it doesn't exist
                        Mastering newMastering = new()
                        {
                            Name = m.Name,
                            Level2 = m.Level2,
                            Level3 = m.Level3,
                            Templates = newTemplates
                        };

                        var temp = DBGlobals!.Configuration.Mastering.ToList();
                        temp.Add(newMastering);
                        DBGlobals.Configuration.Mastering = temp.ToArray();
                    }
                }
            }
        }

        // Handle mastery cloning, copying mastery configuration from source item to new item
        if (details.CloneMasteries)
        {
            string cloneId = details.WeaponCloneMasteriesID ?? details.ItemTplToClone.ToString()!;
            WeaponCopyMastering(details.NewId, cloneId);
        }

        // Handle preset adding
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

        // Handle ammo compatibility cloning, cloning ammo compatibility as source item
        if (details.AmmoCloneCompatibility)
        {
            AmmoCloneCompitability(details.NewId, details.ItemTplToClone.ToString()!);
        }

        // Handle weapon chamber compatibility cloning, cloning chamber configuration and compatibility from WeaponCloneChamberID to new weapon.
        if (details.WeaponCloneChamberCompatibility)
        {
            string cloneId = details.WeaponCloneChamberID ?? details.ItemTplToClone.ToString()!;
            WeaponCopyChambers(details.NewId, cloneId);
        }

        // Handle magazine cartridge compatibility cloning, cloning cartridge configuration and compatibility from MagCloneCartridgeID to new magazine.
        if (details.MagCloneCartridgeCompatibility)
        {
            MagCopyCartridges(details.NewId, details.MagCloneCartridgeID ?? details.ItemTplToClone.ToString()!);
        }

        // Handle buff addition
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

        // Handle craft addition
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

        // Handle additional trader assortment data
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

        // Handle scripted conflicting items
        if (details.ScriptedConflictingInfos != null)
        {
            AddScriptedConflictingList(details.NewId, details.ScriptedConflictingInfos);
        }

        // Handle additional locale data
        if (details.AdditionalLocales != null)
        {
            AddAdditionalLocales(details.AdditionalLocales);
        }

        // Add to primary weapon slots
        if (details.AddToPrimaryWeaponSlot)
        {
            ModAddtoSlot(details.NewId, DBItems![_playerInventoryId], "FirstPrimaryWeapon");
            ModAddtoSlot(details.NewId, DBItems![_playerInventoryId], "SecondPrimaryWeapon");
        }

        // Add to holster slot
        if (details.AddToHolsterWeaponSlot)
        {
            ModAddtoSlot(details.NewId, DBItems![_playerInventoryId], "Holster");
        }

        // Process and validate item slot IDs
        ProcessItemSlots(details.NewId);

        // Track loaded item
        ListLoadedItem.Add(details.NewId);
    }
    #endregion

    #region Helper Methods - Handbook & Mastery
    /// <summary>
    /// Retrieves the parent ID of an item in the handbook
    /// </summary>
    /// <param name="id">Item ID to look up</param>
    /// <param name="parentId">Output parameter for the parent ID</param>
    /// <returns>True if parent ID was found, false otherwise</returns>
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

    /// <summary>
    /// Retrieves a mastery section by name
    /// </summary>
    /// <param name="masteryName">Name of the mastery section</param>
    /// <param name="outMastering">Output parameter for the mastery section</param>
    /// <returns>True if mastery section was found, false otherwise</returns>
    public bool GetMasteringByName(string masteryName, out Mastering? outMastering)
    {
        outMastering = null;
        foreach (Mastering mastering in DBGlobals!.Configuration.Mastering)
        {
            if (mastering.Name == masteryName)
            {
                outMastering = mastering;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Copies mastery configuration from one item to another
    /// </summary>
    /// <param name="itemId">Target item ID</param>
    /// <param name="itemCopyId">Source item ID to copy from</param>
    public void WeaponCopyMastering(MongoId itemId, MongoId itemCopyId)
    {
        foreach (Mastering mastering in DBGlobals!.Configuration.Mastering)
        {
            if (mastering.Templates.Contains(itemCopyId))
            {
                if (mastering.Templates is List<MongoId> tpls)
                {
                    if (!tpls.Contains(itemId))
                    {
                        tpls.Add(itemId);
                    }
                }
                return;
            }
        }
        _logger.Error($"WeaponCopyMastering: Mastering of id {itemCopyId} not found when copying for {itemId}!");
    }
    #endregion

    #region Helper Methods - Item Slots
    /// <summary>
    /// Copies slots from source items to target item with custom modifications
    /// </summary>
    /// <param name="itemId">Target item ID</param>
    /// <param name="copySlotInfos">Slot copy configuration</param>
    private void ItemCopySlot(MongoId itemId, CopySlotInfo[] copySlotInfos)
    {
        var slots = DBItems![itemId].Properties!.Slots!;
        List<Slot> newSlots = new();

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

            // Clone slot filters
            IEnumerable<SlotFilter> filters = _cloner.Clone(tgtSlot.Properties.Filters)!;

            // Add additional items to slot filter if specified
            if (item.ItemsAddToSlot != null && item.ItemsAddToSlot.Length > 0)
            {
                filters!.ElementAt(0).Filter!.UnionWith(Array.ConvertAll(item.ItemsAddToSlot, tpl => (MongoId)tpl));
            }

            // Create new slot with cloned configuration
            Slot newSlot = new()
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

            newSlots.Add(newSlot);
        }

        // Add new slots to target item
        if (slots is List<Slot> slotsList)
        {
            slotsList.AddRange(newSlots);
        }
    }

    /// <summary>
    /// Adds new slots to an item
    /// </summary>
    /// <param name="itemId">Target item ID</param>
    /// <param name="newSlots">Slots to add</param>
    private void ItemAddSlot(MongoId itemId, Slot[] newSlots)
    {
        var slots = DBItems![itemId].Properties?.Slots;
        if (slots is List<Slot> slotsList)
        {
            slotsList.AddRange(newSlots);
        }
    }

    /// <summary>
    /// Retrieves a specific slot by name from an item
    /// </summary>
    /// <param name="itemId">Item ID to check</param>
    /// <param name="slotName">Slot name to find</param>
    /// <param name="slotOut">Output parameter for the found slot</param>
    /// <returns>True if slot was found, false otherwise</returns>
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

    /// <summary>
    /// Validates if an item has properly configured slots
    /// </summary>
    /// <param name="tplItem">Item to validate</param>
    /// <returns>True if item has valid slots, false otherwise</returns>
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

    /// <summary>
    /// Validates if an item has properly configured chambers
    /// </summary>
    /// <param name="tplItem">Item to validate</param>
    /// <returns>True if item has valid chambers, false otherwise</returns>
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

    /// <summary>
    /// Validates if an item has properly configured cartridges
    /// </summary>
    /// <param name="tplItem">Item to validate</param>
    /// <returns>True if item has valid cartridges, false otherwise</returns>
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

    /// <summary>
    /// Processes and validates slot IDs for an item, ensuring they follow the correct format and are unique
    /// </summary>
    /// <param name="id">Item ID to process</param>
    public void ProcessItemSlots(MongoId id)
    {
        TemplateItem item = DBItems![id];
        int indexNumber = 0;

        // Process standard slots
        if (ItemHasValidSlots(item))
        {
            indexNumber = 0;
            foreach (Slot s in item.Properties!.Slots!)
            {
                s.Parent = id;
                s.Id = id.ToString().Substring(0, 21) + 'a' + indexNumber.ToString("X2");
                indexNumber++;
            }
        }

        // Process cartridges
        if (ItemHasValidCartridges(item))
        {
            indexNumber = 0;
            foreach (Slot s in item.Properties!.Cartridges!)
            {
                s.Parent = id;
                s.Id = id.ToString().Substring(0, 21) + 'b' + indexNumber.ToString("X2");
                indexNumber++;
            }
        }

        // Process chambers
        if (ItemHasValidChambers(item))
        {
            indexNumber = 0;
            foreach (Slot s in item.Properties!.Chambers!)
            {
                s.Parent = id;
                s.Id = id.ToString().Substring(0, 21) + 'c' + indexNumber.ToString("X2");
                indexNumber++;
            }
        }
    }
    #endregion

    #region Helper Methods - Presets
    /// <summary>
    /// Adds a weapon preset to the game's preset collection
    /// </summary>
    /// <param name="preset">Preset to add</param>
    public void WeaponAddPreset(Preset preset)
    {
        // Normalize parent IDs to lowercase
        foreach (var item in preset.Items)
        {
            item.ParentId = item.ParentId?.ToLower();
        }

        // Add preset to collection (log error if duplicate)
        if (!DBPreset!.TryAdd(preset.Id, preset))
        {
            _logger.Error($"WeaponAddPreset: Weapon preset of id {preset.Id} already exist!");
        }
    }
    #endregion

    #region Helper Methods - Compatibility Mapping
    /// <summary>
    /// Adds a mod to compatibility mapping for specific slots
    /// </summary>
    /// <param name="idtoAdd">Mod ID to add</param>
    /// <param name="cloneId">Source item ID</param>
    /// <param name="modSlotName">Target mod slots</param>
    /// <param name="cloneConflicts">Whether to clone conflict information</param>
    public void ModAddtoSlotClone(MongoId idtoAdd, MongoId cloneId, string[]? modSlotName, bool cloneConflicts = false)
    {
        // Handle conflict mapping if requested
        if (cloneConflicts)
        {
            var innerDict = _infoCompatibilityMapping[STR_CONFLICTING_ITEMS];

            if (!innerDict.TryGetValue(cloneId, out var idList))
            {
                idList = new List<MongoId>();
                innerDict[cloneId] = idList;
            }

            idList.Add(idtoAdd);
        }

        // Handle all slots mapping if no specific slots provided
        if (modSlotName == null)
        {
            var innerDict = _infoCompatibilityMapping[STR_ALL_SLOTS];

            if (!innerDict.TryGetValue(cloneId, out var idList))
            {
                idList = new List<MongoId>();
                innerDict[cloneId] = idList;
            }

            idList.Add(idtoAdd);
        }
        else
        {
            // Handle specific slot mapping
            foreach (string name in modSlotName)
            {
                if (!_infoCompatibilityMapping.TryGetValue(name, out var innerDict))
                {
                    innerDict = new Dictionary<MongoId, List<MongoId>>();
                    _infoCompatibilityMapping[name] = innerDict;
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

    /// <summary>
    /// Clones ammo compatibility from source item to target item
    /// </summary>
    /// <param name="id">Target item ID</param>
    /// <param name="cloneId">Source item ID</param>
    public void AmmoCloneCompitability(MongoId id, MongoId cloneId)
    {
        // Add to all slots mapping
        {
            var innerDict = _infoCompatibilityMapping[STR_ALL_SLOTS];

            if (!innerDict.TryGetValue(cloneId, out var idList))
            {
                idList = new List<MongoId>();
                innerDict[cloneId] = idList;
            }

            idList.Add(id);
        }

        // Add to ammo specific mapping
        {
            var innerDict = _infoCompatibilityMapping[STR_AMMO];

            if (!innerDict.TryGetValue(cloneId, out var idList))
            {
                idList = new List<MongoId>();
                innerDict[cloneId] = idList;
            }

            idList.Add(id);
        }
    }

    /// <summary>
    /// Copies chamber configuration from source weapon to target weapon
    /// </summary>
    /// <param name="id">Target weapon ID</param>
    /// <param name="cloneId">Source weapon ID</param>
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

        // Clone chamber filter configuration
        var filter = _cloner.Clone(cloneItemTpl.Properties!.Chambers!.ElementAt(0)!.Properties!.Filters!.ElementAt(0).Filter);

        // Apply cloned filter to target weapon chambers
        foreach (var chamber in DBItems![id].Properties!.Chambers!)
        {
            chamber.Properties!.Filters!.ElementAt(0).Filter = _cloner.Clone(filter);
        }
    }

    /// <summary>
    /// Copies cartridge configuration from source magazine to target magazine
    /// </summary>
    /// <param name="id">Target magazine ID</param>
    /// <param name="cloneId">Source magazine ID</param>
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

        // Clone cartridge filter configuration
        DBItems![id].Properties!.Cartridges!.ElementAt(0).Properties!.Filters!.ElementAt(0).Filter =
            _cloner.Clone(cloneItemTpl.Properties!.Cartridges!.ElementAt(0).Properties!.Filters!.ElementAt(0).Filter);
    }

    /// <summary>
    /// Processes compatibility information for all items
    /// Maps slot filters, conflicts, ammo compatibility and more
    /// </summary>
    private void ProcessCompatibilityInfo()
    {
        foreach (KeyValuePair<MongoId, TemplateItem> tplItemEntry in DBItems!)
        {
            var tplItem = tplItemEntry.Value;

            if (tplItem.Type != "Item" || tplItem.Properties == null)
            {
                continue;
            }

            // Process conflicting items
            if (tplItem.Properties.ConflictingItems?.Count > 0)
            {
                ApplyInfoCompatibility(STR_CONFLICTING_ITEMS, tplItem.Properties.ConflictingItems);
            }

            // Process standard slots
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

                    ApplyInfoCompatibility(STR_ALL_SLOTS, slot.Properties!.Filters!.ElementAt(0).Filter!);
                }
            }

            // Process chambers (ammo compatibility)
            if (tplItem.Properties.Chambers?.Count() > 0)
            {
                foreach (Slot chamber in tplItem.Properties.Chambers)
                {
                    if (chamber.Properties?.Filters?.ElementAt(0)?.Filter != null)
                    {
                        ApplyInfoCompatibility(STR_AMMO, chamber.Properties.Filters.ElementAt(0).Filter!);
                    }
                }
            }

            // Process cartridges (ammo compatibility)
            if (tplItem.Properties.Cartridges?.Count() > 0)
            {
                foreach (Slot cart in tplItem.Properties.Cartridges)
                {
                    if (cart.Properties?.Filters?.ElementAt(0)?.Filter != null)
                    {
                        ApplyInfoCompatibility(STR_AMMO, cart.Properties.Filters.ElementAt(0).Filter!);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies compatibility mapping to the specified table
    /// </summary>
    /// <param name="tableName">Target compatibility table name</param>
    /// <param name="slotFilter">Slot filter to process</param>
    private void ApplyInfoCompatibility(string tableName, HashSet<MongoId> slotFilter)
    {
        if (_infoCompatibilityMapping.TryGetValue(tableName, out Dictionary<MongoId, List<MongoId>>? dict) != true)
        {
            return;
        }

        foreach (KeyValuePair<MongoId, List<MongoId>> addList in dict)
        {
            if (slotFilter.Contains(addList.Key))
            {
                // NOTE: Original code was cut off here - implementation incomplete
            }
        }
    }
    #endregion

    #region Helper Methods - Traders
    /// <summary>
    /// Adds an item to a trader's inventory with custom barter schemes
    /// </summary>
    /// <param name="traderId">Trader ID</param>
    /// <param name="itemId">Item ID to add</param>
    /// <param name="traderLoyaltyLevel">Required loyalty level</param>
    /// <param name="barterSchemes">Barter scheme configuration</param>
    /// <param name="buyRestrictionMax">Maximum purchase limit</param>
    /// <param name="unlimitedCount">Whether item has unlimited stock</param>
    /// <param name="stackObjectsCount">Stack size limit</param>
    public void ItemAddtoTrader(
        string traderId,
        MongoId itemId,
        int traderLoyaltyLevel,
        BarterScheme[] barterSchemes,
        int buyRestrictionMax = 1000,
        bool unlimitedCount = true,
        int stackObjectsCount = 9999999)
    {
        if (!TryGetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"ItemAddtoTraders: Trader with id {traderId} not found when adding {itemId}!");
            return;
        }

        // Generate valid assortment ID
        MongoId assortId = new(GenerateValidAssortId(itemId));

        // Create trader item entry
        Item item = GenerateValidTraderSingleItem(
            assortId,
            itemId,
            buyRestrictionMax,
            unlimitedCount,
            stackObjectsCount
        );

        // Add item to trader inventory
        trader.Assort.Items.Add(item);

        // Add barter scheme
        List<BarterScheme> barterSchemesList = barterSchemes.ToList();
        trader.Assort.BarterScheme.TryAdd<MongoId, List<List<BarterScheme>>>(
            assortId,
            new List<List<BarterScheme>> { barterSchemesList });

        // Add loyalty level requirement
        trader.Assort.LoyalLevelItems.TryAdd<MongoId, int>(assortId, traderLoyaltyLevel);

        // Track loaded assortment
        ListLoadedAssort.Add(assortId);
    }

    /// <summary>
    /// Overload - Adds an item to a trader's inventory with currency-based pricing
    /// </summary>
    /// <param name="traderId">Trader ID</param>
    /// <param name="itemId">Item ID to add</param>
    /// <param name="traderLoyaltyLevel">Required loyalty level</param>
    /// <param name="currency">Currency type ID</param>
    /// <param name="price">Item price</param>
    /// <param name="buyRestrictionMax">Maximum purchase limit</param>
    /// <param name="unlimitedCount">Whether item has unlimited stock</param>
    /// <param name="stackObjectsCount">Stack size limit</param>
    public void ItemAddtoTrader(
        string traderId,
        MongoId itemId,
        int traderLoyaltyLevel,
        MongoId currency,
        double price,
        int buyRestrictionMax = 1000,
        bool unlimitedCount = true,
        int stackObjectsCount = 9999999)
    {
        if (!TryGetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"ItemAddtoTraders: Trader with id {traderId} not found when adding {itemId}!");
            return;
        }

        // Generate valid assortment ID
        MongoId assortId = new(GenerateValidAssortId(itemId));

        // Create trader item entry
        Item item = GenerateValidTraderSingleItem(
            assortId,
            itemId,
            buyRestrictionMax,
            unlimitedCount,
            stackObjectsCount
        );

        // Add item to trader inventory
        trader.Assort.Items.Add(item);

        // Create currency-based barter scheme
        List<BarterScheme> barterSchemesList = new()
        {
            new BarterScheme
            {
                Count = price,
                Template = currency
            }
        };

        // Add barter scheme
        trader.Assort.BarterScheme.TryAdd<MongoId, List<List<BarterScheme>>>(
            assortId,
            new List<List<BarterScheme>> { barterSchemesList });

        // Add loyalty level requirement
        trader.Assort.LoyalLevelItems.TryAdd<MongoId, int>(assortId, traderLoyaltyLevel);

        // Track loaded assortment
        ListLoadedAssort.Add(assortId);
    }

    /// <summary>
    /// Adds preset to trader queue for post-load processing
    /// </summary>
    /// <param name="traderId">Trader ID</param>
    /// <param name="presetId">Preset ID to add</param>
    /// <param name="traderLoyaltyLevel">Required loyalty level</param>
    /// <param name="barterSchemes">Barter scheme configuration</param>
    /// <param name="buyRestrictionMax">Maximum purchase limit</param>
    /// <param name="unlimitedCount">Whether preset has unlimited stock</param>
    /// <param name="stackObjectsCount">Stack size limit</param>
    public void PresetAddtoTraders(
        string traderId,
        MongoId presetId,
        int traderLoyaltyLevel,
        BarterScheme[] barterSchemes,
        int buyRestrictionMax = 1000,
        bool unlimitedCount = true,
        int stackObjectsCount = 9999999)
    {
        _presetToTraderInfos.Add(new PresetToTraderInfo(
            traderId,
            presetId,
            traderLoyaltyLevel,
            barterSchemes,
            buyRestrictionMax,
            unlimitedCount,
            stackObjectsCount));
    }

    /// <summary>
    /// Processes preset-to-trader mappings and adds them to trader inventories
    /// </summary>
    private void ProcessPresetToTrader()
    {
        foreach (var presetInfo in _presetToTraderInfos)
        {
            AddTraderAssortFromPreset(
                presetInfo.TraderId,
                presetInfo.PresetId,
                presetInfo.TraderLoyaltyLevel,
                presetInfo.BarterSchemes,
                presetInfo.BuyRestrictionMax,
                presetInfo.UnlimitedCount,
                presetInfo.StackObjectsCount);
        }
    }

    /// <summary>
    /// Adds a preset to a trader's inventory as an assortment
    /// </summary>
    /// <param name="traderId">Trader ID</param>
    /// <param name="presetId">Preset ID to add</param>
    /// <param name="traderLoyaltyLevel">Required loyalty level</param>
    /// <param name="barterSchemes">Barter scheme configuration</param>
    /// <param name="buyRestrictionMax">Maximum purchase limit</param>
    /// <param name="unlimitedCount">Whether preset has unlimited stock</param>
    /// <param name="stackObjectsCount">Stack size limit</param>
    private void AddTraderAssortFromPreset(
        MongoId traderId,
        MongoId presetId,
        int traderLoyaltyLevel,
        BarterScheme[] barterSchemes,
        int buyRestrictionMax = 1000,
        bool unlimitedCount = true,
        int stackObjectsCount = 9999999)
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

        // Clone preset items
        Item[] items = _cloner.Clone(preset.Items)!.ToArray();
        string prefix = "d";
        MongoId assortId = prefix + preset.Parent.ToString().Substring(prefix.Length);

        // Process preset items
        foreach (var it in items!)
        {
            it.Id = prefix + it.Id.ToString().Substring(prefix.Length);

            // Check for duplicate assortment IDs
            if (ListLoadedAssort.Contains(it.Id))
            {
                _logger.Error($"PresetAddtoTraders: Assort item id {it.Id} duplicated!");
                return;
            }

            ListLoadedAssort.Add(it.Id);

            // Update parent IDs with prefix
            if (it.ParentId != null)
            {
                it.ParentId = prefix + it.ParentId.Substring(prefix.Length);
            }

            // Configure main assortment item
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

            // Add item to trader inventory
            trader.Assort.Items.Add(it);
        }

        // Add barter scheme for preset
        trader.Assort.BarterScheme.TryAdd<MongoId, List<List<BarterScheme>>>(
            assortId,
            new List<List<BarterScheme>> { barterSchemes.ToList() });

        // Add loyalty level requirement
        trader.Assort.LoyalLevelItems.TryAdd<MongoId, int>(assortId, traderLoyaltyLevel);
    }

    /// <summary>
    /// Adds additional assortment data to a trader
    /// </summary>
    /// <param name="traderId">Trader ID</param>
    /// <param name="assort">Assortment data to add</param>
    public void AssortsAddtoTrader(string traderId, TraderAssort assort)
    {
        if (!TryGetTrader(traderId, out Trader? trader) || trader == null)
        {
            _logger.Error($"AssortsAddtoTraders: Trader with id {traderId} not found!");
            return;
        }

        // Process loyalty level requirements
        List<MongoId> validAssorts = new();
        foreach (KeyValuePair<MongoId, int> loyalty in assort.LoyalLevelItems)
        {
            if (assort.BarterScheme[loyalty.Key] == null)
            {
                _logger.Error($"AssortsAddtoTraders: Check assort {loyalty.Key} no respective BarterScheme!");
                continue;
            }

            trader.Assort.BarterScheme.TryAdd<MongoId, List<List<BarterScheme>>>(
                loyalty.Key,
                assort.BarterScheme[loyalty.Key]);

            trader.Assort.LoyalLevelItems.TryAdd<MongoId, int>(loyalty.Key, loyalty.Value);

            validAssorts.Add(loyalty.Key);
        }

        // Process assortment items
        List<MongoId> validItems = new();
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

        // Validate assortment-item mapping
        foreach (MongoId id in validAssorts)
        {
            if (!validItems.Contains(id))
            {
                _logger.Error($"AssortsAddtoTraders: Check assort {id} no respective Item!");
            }
        }
    }

    /// <summary>
    /// Generates a valid assortment ID from an item ID
    /// </summary>
    /// <param name="itemId">Source item ID</param>
    /// <returns>Valid assortment ID</returns>
    private static string GenerateValidAssortId(string itemId)
    {
        char[] assortId = itemId.ToCharArray();

        // Modify first character to ensure valid assortment ID format
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

    /// <summary>
    /// Generates a valid trader item entry with standard configuration
    /// </summary>
    /// <param name="assortId">Assortment ID</param>
    /// <param name="tplId">Item template ID</param>
    /// <param name="buyRestrictionMax">Maximum purchase limit</param>
    /// <param name="unlimitedCount">Whether item has unlimited stock</param>
    /// <param name="stackObjectsCount">Stack size limit</param>
    /// <returns>Configured trader item</returns>
    private static Item GenerateValidTraderSingleItem(
        MongoId assortId,
        MongoId tplId,
        int buyRestrictionMax,
        bool unlimitedCount,
        int stackObjectsCount)
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
    #endregion

    #region Helper Methods - Data Access
    /// <summary>
    /// Attempts to retrieve a trader from the database
    /// </summary>
    /// <param name="traderId">Trader ID to retrieve</param>
    /// <param name="trader">Output parameter for the trader</param>
    /// <returns>True if trader was found, false otherwise</returns>
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

    /// <summary>
    /// Attempts to retrieve an item from the database
    /// </summary>
    /// <param name="id">Item ID to retrieve</param>
    /// <param name="tplItem">Output parameter for the item</param>
    /// <returns>True if item was found, false otherwise</returns>
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
    #endregion

    #region Helper Methods - Mod Slots & Content
    /// <summary>
    /// Adds a mod to a specific slot on an item
    /// </summary>
    /// <param name="modId">Mod ID to add</param>
    /// <param name="tplItem">Target item</param>
    /// <param name="slotName">Target slot name</param>
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

    /// <summary>
    /// Adds buffs to the game's buff collection
    /// </summary>
    /// <param name="buffs">Buffs to add (key = buff category, value = buff array)</param>
    public void AddBuffs(Dictionary<string, Buff[]> buffs)
    {
        foreach (KeyValuePair<string, Buff[]> buff in buffs)
        {
            DBBuff!.TryAdd(buff.Key, buff.Value);
        }
    }

    /// <summary>
    /// Adds hideout crafts to the game's craft collection
    /// </summary>
    /// <param name="crafts">Crafts to add</param>
    public void AddCrafts(HideoutProduction[] crafts)
    {
        DBCrafts!.AddRange(crafts);
    }

    /// <summary>
    /// Adds scripted conflicting items to compatibility mapping
    /// </summary>
    /// <param name="itemId">Target item ID</param>
    /// <param name="conflictingInfos">Conflict configuration</param>
    private void AddScriptedConflictingList(MongoId itemId, ConflictingInfos[] conflictingInfos)
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


    /// <summary>
    /// Adds additional locale data for items
    /// </summary>
    /// <param name="NewLocales">Locale data to add</param>
    public void AddAdditionalLocales(Dictionary<string, Dictionary<string,string>> NewLocales)
    {
        foreach (var lang in NewLocales)
        {
            if(DBlocales!.Global.TryGetValue(lang.Key, out var lazyloadedValue))
            {
                lazyloadedValue.AddTransformer(lazyloadedLocaleData =>
                {
                    foreach (var locale in lang.Value)
                    {
                        if (lazyloadedLocaleData!.ContainsKey(locale.Key))
                        {
                            lazyloadedLocaleData[locale.Key] = locale.Value;
                        }
                        else
                        {
                            lazyloadedLocaleData.Add(locale.Key, locale.Value);
                        }
                    }

                    return lazyloadedLocaleData;
                });
            }
        }
    }
    #endregion
}
#endregion

#region Supporting Classes
// Classes for type references used in the core logic
/// <summary>
/// Preset to trader mapping information
/// </summary>
public class PresetToTraderInfo
{
    public MongoId TraderId { get; }
    public MongoId PresetId { get; }
    public int TraderLoyaltyLevel { get; }
    public BarterScheme[] BarterSchemes { get; }
    public int BuyRestrictionMax { get; }
    public bool UnlimitedCount { get; }
    public int StackObjectsCount { get; }

    public PresetToTraderInfo(
        string traderId,
        MongoId presetId,
        int traderLoyaltyLevel,
        BarterScheme[] barterSchemes,
        int buyRestrictionMax,
        bool unlimitedCount,
        int stackObjectsCount)
    {
        TraderId = new MongoId(traderId);
        PresetId = presetId;
        TraderLoyaltyLevel = traderLoyaltyLevel;
        BarterSchemes = barterSchemes;
        BuyRestrictionMax = buyRestrictionMax;
        UnlimitedCount = unlimitedCount;
        StackObjectsCount = stackObjectsCount;
    }
}
#endregion

#region Json Deserialization Classes
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

    [JsonPropertyName("cloneMasteries")]
    public virtual bool CloneMasteries { get; set; } = false;

    [JsonPropertyName("weaponCloneMasteriesID")]
    public virtual string? WeaponCloneMasteriesID { get; set; }

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

    [JsonPropertyName("addtoModSlotsCloneID")]
    public virtual string? AddtoModSlotsCloneID { get; set; }

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

    [JsonPropertyName("weaponCloneChamberID")]
    public virtual string? WeaponCloneChamberID { get; set; }

    [JsonPropertyName("magCloneCartridgeCompatibility")]
    public virtual bool MagCloneCartridgeCompatibility { get; set; } = false;

    [JsonPropertyName("magCloneCartridgeID")]
    public virtual string? MagCloneCartridgeID { get; set; }

    [JsonPropertyName("scriptedConflictingInfos")]
    public virtual ConflictingInfos[]? ScriptedConflictingInfos { get; set; }

    [JsonPropertyName("additionalLocales")]
    public virtual Dictionary<string, Dictionary<string, string>>? AdditionalLocales { get; set; }

    [JsonPropertyName("addToPrimaryWeaponSlot")]
    public virtual bool AddToPrimaryWeaponSlot { get; set; } = false;

    [JsonPropertyName("addToHolsterWeaponSlot")]
    public virtual bool AddToHolsterWeaponSlot { get; set; } = false;

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

    [JsonPropertyName("Templates")]
    public virtual string[] Templates { get; set; } = [];

    [JsonPropertyName("Level2")]
    public virtual int Level2 { get; set; }

    [JsonPropertyName("Level3")]
    public virtual int Level3 { get; set; }
}

/// <summary>
/// Package for deserializing barter scheme data from JSON, with default template handling
/// </summary>
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
#endregion