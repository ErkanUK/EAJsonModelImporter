using System.Text.Json.Nodes;
using EAJsonModelImporter;

var schema = JsonNode.Parse("""
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "Library",
  "version": "1.2",
  "$defs": {
    "Person": { "type": "object", "properties": { "name": { "type": "string" } }, "required": ["name"] },
    "Author": { "allOf": [ { "$ref": "#/$defs/Person" } ], "type": "object" }
  },
  "type": "object",
  "properties": {
    "authors": { "type": "array", "items": { "$ref": "#/$defs/Author" } },
    "status": { "type": "string", "enum": ["open", "closed"] }
  },
  "required": ["authors"]
}
""")!;
var model = new SchemaConverter().Convert(schema, "fallback");
Assert(model.Name == "Library" && model.Version == "1.2", "schema metadata");
Assert(model.Classes.Any(x => x.Name == "Person" && x.Properties.Any(p => p.Name == "name" && p.Required)), "required attribute");
Assert(model.Classes.Any(x => x.Name == "Author" && x.Parents.Contains("Person")), "allOf inheritance");
Assert(model.Classes.Single(x => x.Name == "Library").Properties.Any(x => x.Name == "authors" && x.Many && x.IsReference), "array association");
Assert(model.Enums.Any(x => x.Values.SequenceEqual(["open", "closed"])), "enumeration");

var plain = JsonNode.Parse("""{"id":7,"customer":{"name":"Ada"},"lines":[{"sku":"A1","quantity":2}]}""")!;
var inferred = new SchemaConverter().Convert(plain, "order");
Assert(inferred.Classes.Any(x => x.Name == "Order"), "plain JSON root");
Assert(inferred.Classes.Any(x => x.Name == "Customer"), "nested object without owner prefix");
Assert(inferred.Classes.Any(x => x.Name == "Line"), "array item class without owner prefix");

var prefixed = JsonNode.Parse("""{"linkml":{"classes":{"Person":{"name":"Person"}}}}""")!;
var cleanNames = new SchemaConverter().Convert(prefixed, "LDMLoadModel");
Assert(cleanNames.Classes.Any(x => x.Name == "Linkml"), "first nested name");
Assert(cleanNames.Classes.Any(x => x.Name == "Person"), "leaf nested name");
Assert(cleanNames.Classes.All(x => x.Name != "Classes"), "classes container is flattened");
Assert(cleanNames.Classes.Where(x => x.Name != "LDMLoadModel").All(x => !x.Name.StartsWith("LDMLoadModel")), "no repeated root prefix");

var classContainer = JsonNode.Parse("""{"classes":{"TransformerLoadForecast":{"name":"Load forecast"},"TransformerActual":{"name":"Actual"}}}""")!;
var flattened = new SchemaConverter().Convert(classContainer, "Model");
Assert(flattened.Classes.Any(x => x.Name == "TransformerLoadForecast"), "classes container child name");
Assert(flattened.Classes.Any(x => x.Name == "TransformerActual"), "second classes container child name");
Assert(flattened.Classes.All(x => !x.Name.StartsWith("Classes")), "no Classes prefix");

var linkmlAttributes = JsonNode.Parse("""
{
  "classes": {
    "Terminal": {
      "description": "A terminal",
      "attributes": {
        "description": { "range": "string" },
        "conductingEquipment": { "range": "ConductingEquipment", "required": true }
      }
    },
    "ConductingEquipment": {
      "attributes": { "name": { "range": "string", "multivalued": false } }
    }
  }
}
""")!;
var attributeModel = new SchemaConverter().Convert(linkmlAttributes, "Grid");
var terminal = attributeModel.Classes.Single(x => x.Name == "Terminal");
Assert(attributeModel.Classes.All(x => !x.Name.EndsWith("Attributes")), "attributes container is not a class");
Assert(terminal.Properties.Any(x => x.Name == "description" && x.Type == "String" && !x.IsReference), "primitive attribute definition");
Assert(terminal.Properties.Any(x => x.Name == "conductingEquipment" && x.Type == "ConductingEquipment" && x.IsReference && x.Required), "class range association definition");

var namedEnums = JsonNode.Parse("""
{
  "$defs": {
    "Priority": { "type": "string", "enum": ["low", "high"] }
  },
  "type": "object",
  "properties": { "priority": { "$ref": "#/$defs/Priority" } }
}
""")!;
var namedEnumModel = new SchemaConverter().Convert(namedEnums, "Task");
Assert(namedEnumModel.Enums.Any(x => x.Name == "Priority" && x.Values.SequenceEqual(["low", "high"])), "named JSON Schema enum");
Assert(namedEnumModel.Classes.All(x => x.Name != "Priority"), "named JSON Schema enum is not a class");
Assert(namedEnumModel.Classes.Single(x => x.Name == "Task").Properties.Any(x => x.Name == "priority" && x.Type == "Priority" && !x.IsReference), "named JSON Schema enum attribute");

var linkmlEnums = JsonNode.Parse("""
{
  "enums": {
    "OperatingStatus": {
      "description": "Current status",
      "permissible_values": { "ON": { "description": "Active" }, "OFF": { "description": "Inactive" } }
    }
  },
  "classes": {
    "Device": {
      "attributes": { "status": { "range": "OperatingStatus" } }
    }
  }
}
""")!;
var linkmlEnumModel = new SchemaConverter().Convert(linkmlEnums, "Equipment");
Assert(linkmlEnumModel.Enums.Any(x => x.Name == "OperatingStatus" && x.Values.SequenceEqual(["ON", "OFF"])), "LinkML permissible values enum");
Assert(linkmlEnumModel.Enums.Single(x => x.Name == "OperatingStatus").ValueDescriptions["ON"] == "Active", "LinkML enum literal description");
Assert(linkmlEnumModel.Classes.All(x => x.Name != "OperatingStatus"), "LinkML enum is not a class");
Assert(linkmlEnumModel.Classes.Single(x => x.Name == "Device").Properties.Any(x => x.Name == "status" && x.Type == "OperatingStatus" && !x.IsReference), "LinkML enum attribute");

var yamlEnum = SimpleYaml.Parse("""
enums:
  SmartMeterYNEnum:
    description: SIMS smart meter indicator
    permissible_values:
      "Y":
        description: Smart meter installed and active
      "N":
        description: Not a smart meter
classes:
  Meter:
    attributes:
      smart:
        range: SmartMeterYNEnum
""");
var yamlEnumModel = new SchemaConverter().Convert(yamlEnum, "MeterModel");
var smartMeterEnum = yamlEnumModel.Enums.Single(x => x.Name == "SmartMeterYNEnum");
Assert(smartMeterEnum.Values.SequenceEqual(["Y", "N"]), "quoted YAML enum values");
Assert(smartMeterEnum.ValueDescriptions["Y"] == "Smart meter installed and active", "YAML enum literal notes");
Assert(yamlEnumModel.Classes.Single(x => x.Name == "Meter").Properties.Any(x => x.Name == "smart" && !x.IsReference), "YAML enum typed attribute");

var yaml = """
$schema: https://json-schema.org/draft/2020-12/schema
title: Product Catalogue
type: object
required:
  - products
properties:
  products:
    type: array
    items:
      type: object
      title: Product
      properties:
        code:
          type: string
""";
var yamlModel = new SchemaConverter().Convert(SimpleYaml.Parse(yaml), "catalogue");
Assert(yamlModel.Name == "ProductCatalogue", "YAML title");
Assert(yamlModel.Classes.Any(x => x.Name == "Product"), "YAML nested class");
Console.WriteLine("All importer tests passed.");

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Failed: " + name);
}
