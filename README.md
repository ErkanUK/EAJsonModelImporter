# EA JSON/YAML Model Importer

EA JSON/YAML Model Importer is a 64-bit add-in for Sparx Enterprise Architect 17. It converts JSON data, JSON Schema, YAML data, and YAML-based JSON Schema into an editable UML class model.

The importer creates native EA packages, classes, attributes, enumerations, associations, generalizations, multiplicities, notes, and a class diagram. The result can be edited using normal Enterprise Architect modelling tools.

## Requirements

- Sparx Enterprise Architect 17, 64-bit
- Windows x64
- .NET 9 Desktop Runtime, x64

The included `prebuilt` directory means the .NET SDK is not required for normal installation.

## Installation

1. Close Enterprise Architect.
2. Extract the release ZIP to a local directory.
3. Open PowerShell in the extracted directory.
4. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

5. Restart Enterprise Architect.
6. Confirm that **EA JSON/YAML Model Importer** appears under **Specialize > Manage Add-Ins**.

The installer registers the add-in for the current Windows user under EA's 64-bit `EAAddins64` registry location. Administrator rights are not normally required.

## Importing a model

1. Open an EA repository.
2. Select the package that should contain the imported model.
3. Open **Specialize > Add-Ins > JSON/YAML Model Importer**.
4. Choose **Import into selected package…**.
5. Select a `.json`, `.schema.json`, `.yaml`, or `.yml` file.
6. Review the preview showing the number of classes and enumerations.
7. Select **OK** to create the model.

The importer creates a new child package and a class diagram. It never overwrites an existing package. If the generated package name already exists, a numeric suffix is added.

## Mappings

| JSON, JSON Schema or YAML | Enterprise Architect UML |
|---|---|
| Root object | Root class and package |
| Object definition | Class |
| Primitive property | Attribute |
| Nested object | Class and association |
| `$ref` | Association to the referenced class |
| Array of primitives | Multivalued attribute |
| Array of objects or references | Association with upper multiplicity `*` |
| Required property | Lower multiplicity `1` |
| Optional property | Lower multiplicity `0` |
| `enum` | UML Enumeration and literals |
| `allOf` reference | Generalization |
| `oneOf` or `anyOf` references | Choice class |
| `title` | Model or class name |
| `description` | EA Notes |
| `version` | EA package version |
| JSON number | `Real` attribute |
| JSON integer | `Integer` attribute |
| JSON boolean | `Boolean` attribute |
| JSON string | `String` attribute |
| `date` / `date-time` format | `Date` / `DateTime` attribute |

For ordinary JSON or YAML data without a schema, the importer infers classes and types from the available values. For arrays of objects, the first non-null item is used as the structural sample.

## Example

Input:

```yaml
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
        price:
          type: number
```

This produces a `ProductCatalogue` class associated with a `Product` class. The association has multiplicity `1..*`, while `code` and `price` become attributes of `Product`.

Additional examples are available in the `samples` directory:

- `library.schema.json`
- `catalogue.yaml`

## YAML support

The MVP includes a dependency-free reader for the JSON-compatible subset of YAML commonly used by schemas and data files. It supports:

- Indented mappings and sequences
- String, number, boolean and null scalars
- Quoted values
- Comments
- Inline JSON-style arrays and objects
- Literal and folded block text

The following advanced YAML features are not yet supported:

- Anchors and aliases
- Custom tags
- Merge keys
- Multiple documents in one file
- Complex mapping keys

Convert documents using these features to JSON before importing them.

## Repeated imports and model updates

This MVP treats every import as a new model. It does not merge changes into a previously imported package. This protects existing EA content and makes testing reversible—delete the generated child package if the import is not required.

Future versions can add stable source identifiers, change comparison, and controlled model synchronization.

## Troubleshooting

### The add-in is not listed in EA

- Confirm that EA is 64-bit.
- Close EA and run `install.ps1` again.
- Check **Specialize > Manage Add-Ins** after restarting EA.
- Confirm that the .NET 9 Desktop Runtime x64 is installed.

### The import command is disabled

Select a package in EA's Browser before opening the add-in menu.

### The input fails to parse

- Validate JSON documents with a JSON parser.
- Check YAML indentation and ensure spaces are used consistently.
- Convert YAML that uses anchors, aliases, tags, or multiple documents to JSON.

### A generated type name looks different

Names are converted to UML-friendly PascalCase and punctuation or whitespace is removed. Nested types use their property or schema title directly. The owning class name is added only when two generated definitions would otherwise have the same name.

Structural definition containers named `classes`, `definitions`, `$defs`, or `schemas` are flattened. Their child keys become UML class names directly, so a `classes.TransformerLoadForecast` entry is imported as `TransformerLoadForecast`, not `ClassesTransformerLoadForecast`.

Within a class definition, an `attributes` section is also flattened. Primitive ranges become UML attributes on the owning class, while ranges that name another class become associations. The importer therefore creates `Terminal` with its properties and associations rather than a separate `TerminalAttributes` class.

## Building from source

The project targets `net9.0-windows` and references `lib/Interop.EA.dll`.

```powershell
dotnet restore .\EAJsonModelImporter.csproj
dotnet build .\EAJsonModelImporter.csproj -c Release
dotnet publish .\EAJsonModelImporter.csproj -c Release -o .\prebuilt
```

Automated converter tests are maintained separately from the installable package and cover JSON Schema, JSON inference, and YAML conversion.

## MVP status

This is a test build. Use a disposable EA package or repository until the generated model has been reviewed and accepted.
