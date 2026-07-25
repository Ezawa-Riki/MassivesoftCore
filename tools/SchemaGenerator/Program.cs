using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using MassivesoftCore;
using SPTarkov.Server.Core.Models.Common;

var serializerOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
{
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
};

var exporterOptions = new JsonSchemaExporterOptions
{
    TreatNullObliviousAsNonNullable = true,
    TransformSchemaNode = (context, schema) =>
    {
        // Represent SPT MongoId values as their JSON string form.
        var type = context.TypeInfo.Type;
        var isNullableMongoId = Nullable.GetUnderlyingType(type) == typeof(MongoId);
        if (type == typeof(MongoId) || isNullableMongoId)
        {
            var mongoIdSchema = new JsonObject
            {
                ["pattern"] = "^[0-9a-fA-F]{24}$"
            };

            mongoIdSchema["type"] = isNullableMongoId
                ? new JsonArray("string", "null")
                : JsonValue.Create("string");

            return mongoIdSchema;
        }

        return schema;
    }
};

var itemSchema = (JsonObject)serializerOptions.GetJsonSchemaAsNode(
    typeof(AdvancedNewItemFromCloneDetails),
    exporterOptions);

AddRequiredProperty(itemSchema, "itemTplToClone");
itemSchema["allOf"] = BuildBusinessRules();
itemSchema["$comment"] =
    "Business rules mirror AdvancedCreateItemFromClone runtime validation. " +
    "Legacy weapon preset fields override their canonical counterparts when non-null.";
itemSchema["title"] = nameof(AdvancedNewItemFromCloneDetails);

var schema = BuildDictionarySchema(itemSchema);

var repositoryRoot = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var outputDirectory = Path.Combine(repositoryRoot, "schemas");
var outputFile = Path.Combine(
    outputDirectory,
    "AdvancedNewItemFromCloneDetails.schema.json");

Directory.CreateDirectory(outputDirectory);

var schemaJson = schema.ToJsonString(new JsonSerializerOptions
{
    WriteIndented = true
});

// Parse the final document once to verify that the generated JSON is valid.
_ = JsonNode.Parse(schemaJson)
    ?? throw new InvalidOperationException("The generated schema is empty.");

await File.WriteAllTextAsync(
    outputFile,
    schemaJson);

Console.WriteLine($"Schema generated: {outputFile}");
Console.WriteLine($"Item properties: {(itemSchema["properties"] as JsonObject)?.Count ?? 0}");
Console.WriteLine($"Business rules: {(itemSchema["allOf"] as JsonArray)?.Count ?? 0}");

static JsonObject BuildDictionarySchema(JsonObject itemSchema)
{
    const string definitionName = "AdvancedNewItemFromCloneDetails";
    const string definitionReference = "#/$defs/AdvancedNewItemFromCloneDetails";

    PrefixLocalReferences(itemSchema, definitionReference);

    return new JsonObject
    {
        ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
        ["title"] = "AdvancedNewItemFromCloneDetails dictionary",
        ["type"] = "object",
        ["minProperties"] = 1,
        ["properties"] = new JsonObject
        {
            ["$schema"] = new JsonObject
            {
                ["type"] = "string"
            }
        },
        ["additionalProperties"] = new JsonObject
        {
            ["$ref"] = definitionReference
        },
        ["not"] = new JsonObject
        {
            ["required"] = StringArray("$schema"),
            ["maxProperties"] = 1
        },
        ["$defs"] = new JsonObject
        {
            [definitionName] = itemSchema
        }
    };
}

static void PrefixLocalReferences(JsonNode? node, string prefix)
{
    if (node is JsonObject jsonObject)
    {
        if (jsonObject["$ref"] is JsonValue referenceNode
            && referenceNode.TryGetValue<string>(out var reference)
            && reference.StartsWith("#/", StringComparison.Ordinal))
        {
            jsonObject["$ref"] = prefix + reference[1..];
        }

        foreach (var property in jsonObject.ToList())
        {
            PrefixLocalReferences(property.Value, prefix);
        }
    }
    else if (node is JsonArray jsonArray)
    {
        foreach (var item in jsonArray)
        {
            PrefixLocalReferences(item, prefix);
        }
    }
}

static JsonArray BuildBusinessRules()
{
    return new JsonArray
    {
        RequireWhenTrue("copySlot", "copySlots", CollectionSchema("array")),
        RequireWhenTrue("addSlot", "addSlots", CollectionSchema("array")),
        RequireWhenTrue("masteries", "masterySections", CollectionSchema("array", 1)),
        BuildWeaponPresetRule(),
        RequireWhenTrue("addBuffs", "buffs", CollectionSchema("object", 1)),
        RequireWhenTrue("addCrafts", "crafts", CollectionSchema("array", 1)),
        new JsonObject
        {
            ["if"] = new JsonObject
            {
                ["allOf"] = new JsonArray
                {
                    PropertyEquals("addtoTraders", true),
                    PropertyEquals("addPresetInsteadOfItem", true)
                }
            },
            ["then"] = RequireProperty(
                "presetIdToAdd",
                new JsonObject
                {
                    ["type"] = "string",
                    ["pattern"] = "^[0-9a-fA-F]{24}$"
                })
        },
        new JsonObject
        {
            ["if"] = new JsonObject
            {
                ["required"] = StringArray("additionalAssortData"),
                ["properties"] = new JsonObject
                {
                    ["additionalAssortData"] = new JsonObject
                    {
                        ["type"] = "object"
                    }
                }
            },
            ["then"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["additionalAssortData"] = new JsonObject
                    {
                        ["required"] = StringArray(
                            "items",
                            "barter_scheme",
                            "loyal_level_items")
                    }
                }
            }
        }
    };
}

static JsonObject BuildWeaponPresetRule()
{
    var effectiveFlagIsTrue = new JsonObject
    {
        ["anyOf"] = new JsonArray
        {
            PropertyEquals("addweaponpreset", true),
            new JsonObject
            {
                ["allOf"] = new JsonArray
                {
                    PropertyIsAbsentOrNull("addweaponpreset"),
                    PropertyEquals("addWeaponPreset", true)
                }
            }
        }
    };

    var effectivePresetsAreNonEmpty = new JsonObject
    {
        ["anyOf"] = new JsonArray
        {
            RequireProperty("weaponpresets", CollectionSchema("array", 1)),
            new JsonObject
            {
                ["allOf"] = new JsonArray
                {
                    PropertyIsAbsentOrNull("weaponpresets"),
                    RequireProperty("weaponPresets", CollectionSchema("array", 1))
                }
            }
        }
    };

    return new JsonObject
    {
        ["if"] = effectiveFlagIsTrue,
        ["then"] = effectivePresetsAreNonEmpty
    };
}

static JsonObject RequireWhenTrue(
    string flagName,
    string propertyName,
    JsonObject propertySchema)
{
    return new JsonObject
    {
        ["if"] = PropertyEquals(flagName, true),
        ["then"] = RequireProperty(propertyName, propertySchema)
    };
}

static JsonObject PropertyEquals(string propertyName, bool value)
{
    return new JsonObject
    {
        ["required"] = StringArray(propertyName),
        ["properties"] = new JsonObject
        {
            [propertyName] = new JsonObject
            {
                ["const"] = value
            }
        }
    };
}

static JsonObject PropertyIsAbsentOrNull(string propertyName)
{
    return new JsonObject
    {
        ["anyOf"] = new JsonArray
        {
            new JsonObject
            {
                ["not"] = new JsonObject
                {
                    ["required"] = StringArray(propertyName)
                }
            },
            new JsonObject
            {
                ["required"] = StringArray(propertyName),
                ["properties"] = new JsonObject
                {
                    [propertyName] = new JsonObject
                    {
                        ["type"] = "null"
                    }
                }
            }
        }
    };
}

static JsonObject RequireProperty(string propertyName, JsonObject propertySchema)
{
    return new JsonObject
    {
        ["required"] = StringArray(propertyName),
        ["properties"] = new JsonObject
        {
            [propertyName] = propertySchema
        }
    };
}

static JsonObject CollectionSchema(string type, int? minimumCount = null)
{
    var schema = new JsonObject
    {
        ["type"] = type
    };

    if (minimumCount.HasValue)
    {
        schema[type == "array" ? "minItems" : "minProperties"] = minimumCount.Value;
    }

    return schema;
}

static void AddRequiredProperty(JsonObject schema, string propertyName)
{
    var required = schema["required"] as JsonArray ?? new JsonArray();
    schema["required"] = required;

    if (!required.Any(node => node?.GetValue<string>() == propertyName))
    {
        required.Add(propertyName);
    }
}

static JsonArray StringArray(params string[] values)
{
    var array = new JsonArray();
    foreach (var value in values)
    {
        array.Add(value);
    }

    return array;
}
