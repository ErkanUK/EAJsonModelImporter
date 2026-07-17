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

        CreateDiagram(repository, package, model, elements);
        repository.RefreshModelView(package.PackageID);
        return package;
    }

    private static void CreateDiagram(EA.Repository repository, EA.Package package, ImportModel model,
        IReadOnlyDictionary<string, EA.Element> elements)
    {
        var diagram = (EA.Diagram)package.Diagrams.AddNew(model.Name + " Class Model", "Logical");
        diagram.Notes = "Generated from JSON, JSON Schema, or YAML.";
        diagram.Update();
        int count = elements.Count;
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        int index = 0;
        foreach (var element in elements.Values)
        {
            int column = index % columns, row = index / columns;
            int left = 40 + column * 300, top = -(40 + row * 220);
            var diagramObject = (EA.DiagramObject)diagram.DiagramObjects.AddNew(
                $"l={left};r={left + 240};t={top};b={top - 150};", "");
            diagramObject.ElementID = element.ElementID;
            diagramObject.Update();
            index++;
        }
        diagram.DiagramObjects.Refresh();
        repository.SaveDiagram(diagram.DiagramID);
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
