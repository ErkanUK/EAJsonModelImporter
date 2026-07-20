namespace EAJsonModelImporter;

internal static class EaModelWriter
{
    public static EA.Package Write(EA.Repository repository, EA.Package target, ImportModel model)
    {
        string packageName = UniquePackageName(target, model.Name);
        var package = (EA.Package)target.Packages.AddNew(packageName, "");
        package.Notes = model.Description;
        if (model.Version.Length > 0) package.Version = model.Version;
        package.Update();
        target.Packages.Refresh();

        var elements = new Dictionary<string, EA.Element>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in model.Enums)
        {
            var element = (EA.Element)package.Elements.AddNew(definition.Name, "Enumeration");
            element.Notes = definition.Description;
            element.Update();
            foreach (var value in definition.Values)
            {
                var literal = (EA.Attribute)element.Attributes.AddNew(value, "");
                if (definition.ValueDescriptions.TryGetValue(value, out var description)) literal.Notes = description;
                literal.Update();
            }
            element.Attributes.Refresh();
            elements[definition.Name] = element;
        }
        foreach (var definition in model.Classes)
        {
            var element = (EA.Element)package.Elements.AddNew(definition.Name, "Class");
            element.Notes = definition.Description;
            element.Update();
            elements[definition.Name] = element;
        }
        package.Elements.Refresh();

        foreach (var definition in model.Classes)
        {
            var element = elements[definition.Name];
            foreach (var property in definition.Properties)
            {
                if (property.IsReference && elements.TryGetValue(property.Type, out var targetElement))
                {
                    var connector = (EA.Connector)element.Connectors.AddNew("", "Association");
                    connector.SupplierID = targetElement.ElementID;
                    connector.SupplierEnd.Role = property.Name;
                    connector.SupplierEnd.Cardinality = Cardinality(property);
                    connector.Notes = property.Description;
                    connector.Update();
                    continue;
                }

                var attribute = (EA.Attribute)element.Attributes.AddNew(property.Name, property.Type);
                attribute.Notes = property.Description;
                attribute.IsID = property.Identifier;
                attribute.LowerBound = property.Required ? "1" : "0";
                attribute.UpperBound = property.Many ? "*" : "1";
                if (elements.TryGetValue(property.Type, out var classifier)) attribute.ClassifierID = classifier.ElementID;
                attribute.Update();
            }
            element.Attributes.Refresh();
            element.Connectors.Refresh();

            foreach (var parentName in definition.Parents.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!elements.TryGetValue(parentName, out var parent)) continue;
                var generalization = (EA.Connector)element.Connectors.AddNew("", "Generalization");
                generalization.SupplierID = parent.ElementID;
                generalization.Update();
            }
            element.Connectors.Refresh();
        }

        CreateDiagrams(repository, package, model, elements);
        repository.RefreshModelView(package.PackageID);
        return package;
    }

    private static void CreateDiagrams(EA.Repository repository, EA.Package package, ImportModel model,
        IReadOnlyDictionary<string, EA.Element> elements)
    {
        if (!model.Classes.Any(x => x.DiagramDomains.Count > 0))
        {
            CreateGridDiagram(repository, package, model.Name + " Class Model",
                "Generated from JSON, JSON Schema, or YAML.", elements.Values, 0);
            return;
        }

        var domains = OrderedDomains(model);
        CreateOverviewDiagram(repository, package, model, elements, domains);
        for (int domainIndex = 0; domainIndex < domains.Count; domainIndex++)
        {
            string domain = domains[domainIndex];
            var definitions = model.Classes
                .Where(x => EffectiveDomains(x).Contains(domain, StringComparer.OrdinalIgnoreCase))
                .OrderBy(x => x.DiagramOrder).ThenBy(x => x.Name)
                .ToList();
            var domainElements = definitions.Where(x => elements.ContainsKey(x.Name)).Select(x => elements[x.Name]);
            CreateGridDiagram(repository, package, model.Name + " - " + DisplayDomain(domain),
                "Generated from ea_domains LinkML annotations.", domainElements, DomainColor(model, domain, domainIndex));
        }

        if (model.Enums.Count > 0)
        {
            var enumElements = model.Enums.Where(x => elements.ContainsKey(x.Name)).Select(x => elements[x.Name]);
            CreateGridDiagram(repository, package, model.Name + " - Enumerations",
                "Enumerations are separated from domain views to reduce diagram clutter.", enumElements, 0);
        }
    }

    private static void CreateOverviewDiagram(EA.Repository repository, EA.Package package, ImportModel model,
        IReadOnlyDictionary<string, EA.Element> elements, IReadOnlyList<string> domains)
    {
        var diagram = (EA.Diagram)package.Diagrams.AddNew(model.Name + " - Overview", "Logical");
        diagram.Notes = "Four-domain overview generated from ea_domains annotations. " +
            "Quadrants: top-left, top-right, bottom-left, bottom-right.";
        diagram.Update();

        const int quadrantWidth = 1040, quadrantHeight = 820;
        for (int domainIndex = 0; domainIndex < domains.Count; domainIndex++)
        {
            string domain = domains[domainIndex];
            var definitions = model.Classes
                .Where(x => PrimaryDomain(x).Equals(domain, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.DiagramOrder).ThenBy(x => x.Name).ToList();
            int quadrantColumn = domainIndex % 2, quadrantRow = domainIndex / 2;
            int originX = 40 + quadrantColumn * quadrantWidth;
            int originY = 60 + quadrantRow * quadrantHeight;
            const int columns = 3;
            for (int index = 0; index < definitions.Count; index++)
            {
                if (!elements.TryGetValue(definitions[index].Name, out var element)) continue;
                int column = index % columns, row = index / columns;
                AddDiagramObject(diagram, element, originX + column * 320, originY + row * 220,
                    DomainColor(model, domain, domainIndex));
            }
        }
        diagram.DiagramObjects.Refresh();
        repository.SaveDiagram(diagram.DiagramID);
    }

    private static void CreateGridDiagram(EA.Repository repository, EA.Package package, string name, string notes,
        IEnumerable<EA.Element> sourceElements, int backgroundColor)
    {
        var items = sourceElements.ToList();
        var diagram = (EA.Diagram)package.Diagrams.AddNew(name, "Logical");
        diagram.Notes = notes;
        diagram.Update();
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(items.Count)));
        for (int index = 0; index < items.Count; index++)
        {
            int column = index % columns, row = index / columns;
            AddDiagramObject(diagram, items[index], 40 + column * 300, 40 + row * 220, backgroundColor);
        }
        diagram.DiagramObjects.Refresh();
        repository.SaveDiagram(diagram.DiagramID);
    }

    private static void AddDiagramObject(EA.Diagram diagram, EA.Element element, int left, int top,
        int backgroundColor)
    {
        int eaTop = -top;
        var diagramObject = (EA.DiagramObject)diagram.DiagramObjects.AddNew(
            $"l={left};r={left + 240};t={eaTop};b={eaTop - 150};", "");
        diagramObject.ElementID = element.ElementID;
        if (backgroundColor != 0) diagramObject.BackgroundColor = backgroundColor;
        diagramObject.Update();
    }

    private static List<string> OrderedDomains(ImportModel model)
    {
        var observed = model.Classes.SelectMany(EffectiveDomains)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string[] preferred = ["network_spine", "load_planning", "asset_health", "source_lineage", "other"];
        return preferred.Where(x => observed.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Concat(observed.Where(x => !preferred.Contains(x, StringComparer.OrdinalIgnoreCase)).Order())
            .ToList();
    }

    private static IEnumerable<string> EffectiveDomains(ImportClass definition) =>
        definition.DiagramDomains.Count > 0 ? definition.DiagramDomains : ["other"];

    private static string PrimaryDomain(ImportClass definition) => EffectiveDomains(definition).First();

    private static string DisplayDomain(string domain) => string.Join(" ", domain.Replace('-', '_').Split('_',
        StringSplitOptions.RemoveEmptyEntries).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    private static int DomainColor(ImportModel model, string domain, int index)
    {
        string fallback = (index % 4) switch
        {
            0 => "#DDEBF7",
            1 => "#E2EFDA",
            2 => "#FFF2CC",
            _ => "#EAD1DC"
        };
        string configured = model.DiagramDomainColors.TryGetValue(domain, out string? color) ? color : fallback;
        return ParseEaColor(configured) ?? ParseEaColor(fallback)!.Value;
    }

    // EA stores diagram colours as BGR decimal: red is the least-significant byte.
    internal static int? ParseEaColor(string value)
    {
        string hex = value.Trim().TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(x => new string(x, 2)));
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int rgb)) return null;
        int red = (rgb >> 16) & 0xFF;
        int green = (rgb >> 8) & 0xFF;
        int blue = rgb & 0xFF;
        return red | (green << 8) | (blue << 16);
    }

    private static string Cardinality(ImportProperty property) => property.Many
        ? (property.Required ? "1..*" : "0..*")
        : (property.Required ? "1" : "0..1");

    private static string UniquePackageName(EA.Package target, string desired)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EA.Package child in target.Packages) names.Add(child.Name);
        if (!names.Contains(desired)) return desired;
        int suffix = 2;
        while (names.Contains(desired + " " + suffix)) suffix++;
        return desired + " " + suffix;
    }
}
