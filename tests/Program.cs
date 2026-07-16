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
Assert(cleanNames.Classes.Any(x => x.Name == "Classes"), "deep nested name");
Assert(cleanNames.Classes.Any(x => x.Name == "Person"), "leaf nested name");
Assert(cleanNames.Classes.Where(x => x.Name != "LDMLoadModel").All(x => !x.Name.StartsWith("LDMLoadModel")), "no repeated root prefix");

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
