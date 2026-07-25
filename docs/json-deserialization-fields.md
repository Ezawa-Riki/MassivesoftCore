# MassivesoftCore JSON 反序列化字段说明

本文档介绍 `MassivesoftCore` 暴露给子 mod 使用的 JSON 反序列化字段。核心类型是 `AdvancedNewItemFromCloneDetails`，它继承自 SPT 的 `NewItemFromCloneDetails`，并额外提供交易员上架、预设、槽位、兼容性、技能熟练度、Buff、制作、语言文本等扩展字段。

## 基本规则

- JSON 字段名以源码里的 `JsonPropertyName` 为准，大小写敏感。
- 未填写的布尔字段默认为 `false`。
- 未填写的可空字段默认为 `null`，通常表示“不处理该功能”或“使用克隆来源作为默认来源”。
- 物品 ID、商人 ID、预设 ID 等一般是 EFT/SPT 的 24 位 MongoId 字符串。
- `newId` 是必填字段，用来表示新物品模板 ID。
- 继承自 `NewItemFromCloneDetails` 的字段仍然可用，例如克隆来源、父级、Handbook、价格、模板属性覆盖等字段；本文主要说明 MassivesoftCore 新增或重映射的字段。

## AdvancedNewItemFromCloneDetails

### 基础字段

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `newId` | `string` | 必填 | 新物品模板 ID。必须唯一，重复会报错并停止创建该物品。 |

### 交易员上架

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `addtoTraders` | `bool` | `false` | 是否把新物品或指定预设加入交易员货架。 |
| `addPresetInsteadOfItem` | `bool` | `false` | 为 `true` 时，上架 `presetIdToAdd` 指向的武器预设，而不是 `newId` 对应的单个物品。 |
| `presetIdToAdd` | `string?` | `null` | 要上架的预设 ID。仅在 `addPresetInsteadOfItem` 为 `true` 时需要。 |
| `traderId` | `string?` | 默认商人 `5a7c2eca46aef81a7ca2145d` | 目标交易员 ID。 |
| `traderLoyaltyLevel` | `int?` | `1` | 上架所需商人等级。 |
| `barterScheme` | `DeserializationBarterScheme[]?` | 1 卢布 | 换购方案数组。未填写时默认价格为 `fleaPriceRoubles`，若该字段也为空则为 `1`。 |
| `buyRestrictionMax` | `int?` | `1000` | 单次刷新最大购买数量。 |
| `additionalAssortData` | `TraderAssort?` | `null` | 直接追加完整交易员 assort 数据，适合复杂货架结构。 |

`additionalAssortData` 必须至少包含 `items`、`barter_scheme` 和 `loyal_level_items` 对应的数据，否则会被视为无效。

### 武器预设

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `addWeaponPreset` | `bool` | `false` | 是否把 `weaponPresets` 写入全局武器预设表。 |
| `weaponPresets` | `Preset[]?` | `null` | 要新增的武器预设数组。 |
| `addweaponpreset` | `bool?` | `null` | 旧版兼容字段。存在时会覆盖 `addWeaponPreset`。 |
| `weaponpresets` | `Preset[]?` | `null` | 旧版兼容字段。存在时会覆盖 `weaponPresets`。 |

注意：代码会把预设内每个 item 的 `parentId` 转成小写，避免部分 ID 大小写导致引用失败。

### 技能熟练度

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `masteries` | `bool` | `false` | 是否新增或补充武器熟练度分组。 |
| `masterySections` | `MasterySection[]?` | `null` | 熟练度分组配置。 |
| `cloneMasteries` | `bool` | `false` | 是否把某个武器已有的熟练度关系复制给新物品。 |
| `weaponCloneMasteriesID` | `string?` | 克隆来源物品 ID | `cloneMasteries` 使用的来源武器 ID。 |

`masteries` 会按 `Name` 查找已有熟练度分组：找到就把 `Templates` 里的物品 ID 加进去，找不到就创建新分组。

### 槽位复制和新增

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `copySlot` | `bool` | `false` | 是否从其他物品复制槽位到新物品。 |
| `copySlots` | `CopySlotInfo[]?` | `null` | 复制槽位的规则列表。 |
| `addSlot` | `bool` | `false` | 是否直接把 `addSlots` 里的槽位追加到新物品。 |
| `addSlots` | `Slot[]?` | `null` | 要直接追加的 SPT `Slot` 数据。 |

`copySlot` 适合“从现有物品拿一个槽位结构，再改槽位名或追加可安装物”的场景。`addSlot` 则要求你自己提供完整 Slot 数据。

### 加入其他物品槽位

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `addtoModSlots` | `bool` | `false` | 是否把新物品加入其他物品的可安装槽位过滤器。 |
| `addtoModSlotsCloneID` | `string?` | 克隆来源物品 ID | 以哪个旧物品为参照：凡是能装该旧物品的位置，也加入新物品。 |
| `modSlot` | `string[]?` | `null` | 限定槽位名。为 `null` 时匹配所有槽位；填写后只匹配指定槽位名。 |
| `addtoConflicts` | `bool` | `false` | 是否同步处理 `ConflictingItems`，让与旧物品冲突的位置也加入新物品冲突。 |

这组字段用于解决“新配件/新武器已经创建，但其他物品槽位里还不认识它”的兼容性问题。

### 弹药、枪膛和弹匣兼容

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `ammoCloneCompatibility` | `bool` | `false` | 把新弹药加入所有兼容克隆来源弹药的槽位、枪膛和弹匣过滤器。 |
| `weaponCloneChamberCompatibility` | `bool` | `false` | 把来源武器的枪膛可用弹药复制到新武器。 |
| `weaponCloneChamberID` | `string?` | 克隆来源物品 ID | 枪膛兼容复制来源。 |
| `magCloneCartridgeCompatibility` | `bool` | `false` | 把来源弹匣的 cartridge 过滤器复制到新弹匣。 |
| `magCloneCartridgeID` | `string?` | 克隆来源物品 ID | 弹匣 cartridge 兼容复制来源。 |

`ammoCloneCompatibility` 是全局映射式处理，会在后加载阶段扫描数据库并把新 ID 注入相关过滤器。`weaponCloneChamberCompatibility` 和 `magCloneCartridgeCompatibility` 是直接复制来源物品的过滤器。

### Buff、制作和本地化

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `addBuffs` | `bool` | `false` | 是否添加兴奋剂 Buff 配置。 |
| `buffs` | `Dictionary<string, Buff[]>?` | `null` | Buff 表，key 为 Buff ID 或分组名，value 为 Buff 数组。 |
| `addCrafts` | `bool` | `false` | 是否添加藏身处制作配方。 |
| `crafts` | `HideoutProduction[]?` | `null` | 藏身处制作配方数组。 |
| `additionalLocales` | `Dictionary<string, Dictionary<string, string>>?` | `null` | 追加或覆盖本地化文本。第一层 key 是语言代码，例如 `en`、`ch`。 |

`additionalLocales` 会对已有语言表执行追加或覆盖；如果语言代码不存在，则不会创建新的语言表。

### 玩家装备栏

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `addToPrimaryWeaponSlot` | `bool` | `false` | 把新物品加入玩家库存的第一主武器槽和第二主武器槽。 |
| `addToHolsterWeaponSlot` | `bool` | `false` | 把新物品加入玩家库存的手枪套槽。 |

这两个字段适合新增武器类型时，若武器类型导致其不会被游戏自动加入对应装备栏时（如霰弹枪），补齐玩家装备栏兼容。

### 脚本化冲突列表

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `scriptedConflictingInfos` | `ConflictingInfos[]?` | `null` | 从指定物品槽位收集可安装物，并加入新物品的 `ConflictingItems`。 |

## CopySlotInfo

用于 `copySlots`。

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `id` | `MongoId` | 必填 | 槽位来源物品 ID。 |
| `newSlotName` | `string` | 必填 | 复制到新物品后的槽位名。 |
| `tgtSlotName` | `string?` | `newSlotName` | 来源物品中的目标槽位名。为空时用 `newSlotName` 查找来源槽位。 |
| `itemsAddtoSlot` | `string[]?` | `null` | 复制过滤器后，额外加入该槽位允许安装的物品 ID。 |
| `required` | `bool?` | 来源槽位原值 | 是否必需安装。为空时沿用来源槽位的 `required`。 |

## ConflictingInfos

用于 `scriptedConflictingInfos`。

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `id` | `MongoId` | 必填 | 读取槽位的来源物品 ID。 |
| `tgtSlotName` | `string` | 必填 | 来源物品中的槽位名。 |
| `itemsAddtoSlot` | `string[]?` | `null` | 除来源槽位过滤器外，额外加入冲突列表的物品 ID。 |

## MasterySection

用于 `masterySections`。

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `Name` | `string` | 必填 | 熟练度分组名称。注意首字母大写。 |
| `Templates` | `string[]` | `[]` | 属于该熟练度分组的物品模板 ID。 |
| `Level2` | `int` | `0` | 熟练度 2 级需求值。 |
| `Level3` | `int` | `0` | 熟练度 3 级需求值。 |

这几个字段的 JSON 名是大写开头：`Name`、`Templates`、`Level2`、`Level3`。

## DeserializationBarterScheme

用于 `barterScheme`。它继承 SPT 的 `BarterScheme`，主要额外支持从 JSON 的 `_tpl` 字段写入 `Template`。

| 字段 | 类型 | 默认值 | 作用 |
| --- | --- | --- | --- |
| `_tpl` | `MongoId` | 卢布 ID | 付款物品模板 ID，例如卢布、美元、欧元或其他物品。 |
| `count` | `double` | 取决于 SPT 默认值 | 付款数量。 |

如果 `_tpl` 没有传入，会默认使用卢布。

## 示例

### 新物品上架给商人

```json
{
  "newId": "674000000000000000000001",
  "itemTplToClone": "5447b5cf4bdc2d65278b4567",
  "addtoTraders": true,
  "traderId": "5a7c2eca46aef81a7ca2145d",
  "traderLoyaltyLevel": 1,
  "buyRestrictionMax": 20,
  "barterScheme": [
    {
      "_tpl": "5449016a4bdc2d6f028b456f",
      "count": 12000
    }
  ]
}
```

### 新配件继承旧配件可安装位置

```json
{
  "newId": "674000000000000000000002",
  "itemTplToClone": "5b7be4895acfc400170e2dd5",
  "addtoModSlots": true,
  "addtoModSlotsCloneID": "5b7be4895acfc400170e2dd5",
  "modSlot": ["mod_mount", "mod_tactical"],
  "addtoConflicts": true
}
```

### 新弹药继承兼容性

```json
{
  "newId": "674000000000000000000003",
  "itemTplToClone": "59e690b686f7746c9f75e848",
  "ammoCloneCompatibility": true
}
```

### 复制槽位并额外允许一个物品

```json
{
  "newId": "674000000000000000000004",
  "itemTplToClone": "5447a9cd4bdc2dbd208b4567",
  "copySlot": true,
  "copySlots": [
    {
      "id": "5447a9cd4bdc2dbd208b4567",
      "tgtSlotName": "mod_scope",
      "newSlotName": "mod_scope",
      "itemsAddtoSlot": ["674000000000000000000002"],
      "required": false
    }
  ]
}
```

## 常见坑

- 字段大小写要完全匹配，例如 `addtoTraders` 不是 `addToTraders`。
- `itemsAddtoSlot` 里的 `to` 是小写 `to`，不是 `itemsAddToSlot`。
- `Name`、`Templates`、`Level2`、`Level3` 是大写开头。
- 开启某个功能开关后，配套数组或对象也要提供；例如 `copySlot: true` 时需要 `copySlots`。
- `addPresetInsteadOfItem: true` 时必须提供 `presetIdToAdd`，否则只会记录错误。
- 新物品 ID 和 assort ID 重复会报错。批量生成时要保证 ID 唯一。
