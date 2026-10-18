namespace MechwarriorTooling.Core;

public static class ModelCatalog
{
    public const int ObjectiveBuildingResource = 0x000E;
    private static readonly string[] Effects = ["AC shell", "Missile", "Explosion white", "Explosion yellow", "Explosion red",
        "Explosion smoke 1", "Explosion smoke 2", "Explosion smoke 3", "Sparks small", "Sparks large"];
    public static string Name(string bank, int resource, CombatDefinitions? definitions = null)
    {
        string file = Path.GetFileName(bank);
        if (file.Equals("othpck.tbl", StringComparison.OrdinalIgnoreCase))
        {
            if ((uint)resource < Effects.Length) return Effects[resource];
            if (resource == ObjectiveBuildingResource) return "Objective building (yellow / black)";
        }
        if (file.Equals("terpck.tbl", StringComparison.OrdinalIgnoreCase) && (uint)resource < 30)
            return resource == 1 ? "Red selection" : resource is 0 or (>= 12 and <= 17) or 24 or 25 ? "River" : "Hill";
        var part = definitions?.FindModelResource(bank, resource);
        if (part != null)
        {
            var first = part.Uses[0];
            return $"{part.Chassis} · {part.BodyPart} · {first.Animation} · shape {first.PhysicalFrame}";
        }
        return $"Resource {resource} (0x{resource:X4})";
    }
    public static string Evidence(string bank, int resource, CombatDefinitions? definitions = null)
    {
        if (definitions?.FindModelResource(bank, resource) != null) return "Native 0810:9C06 part binding and executable animation tables";
        string file = Path.GetFileName(bank);
        if (file.Equals("othpck.tbl", StringComparison.OrdinalIgnoreCase) && resource == ObjectiveBuildingResource)
            return "Native 0810:1A2E objective selection; EGA geometry colours";
        if ((file.Equals("othpck.tbl", StringComparison.OrdinalIgnoreCase) && (uint)resource < Effects.Length)
            || (file.Equals("terpck.tbl", StringComparison.OrdinalIgnoreCase) && (uint)resource < 30))
            return "User visual identification; not a proven native gameplay classification";
        return "Unidentified";
    }
}
