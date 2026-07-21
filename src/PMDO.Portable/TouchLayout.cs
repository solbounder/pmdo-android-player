using System.Text.Json;

namespace PMDO.Portable;

/// <summary>Portable, versioned description of the Android touch controls. Coordinates are normalized to the overlay.</summary>
public sealed record TouchLayoutV1(int Version, TouchControlLayout DPad, IReadOnlyList<TouchControlLayout> Buttons)
{
    public const int CurrentVersion = 1;
    public const int MaximumButtons = 16;
    public static TouchLayoutV1 Default { get; } = new(CurrentVersion,
        new TouchControlLayout("dpad", "DPad", 0.08f, 0.62f, 1f, true),
        new[] {
            Button("a", "A", .86f, .72f), Button("b", "B", .93f, .63f), Button("x", "X", .79f, .63f), Button("y", "Y", .86f, .54f),
            Button("l", "L", .08f, .10f), Button("r", "R", .86f, .10f), Button("zl", "ZL", .18f, .10f), Button("zr", "ZR", .76f, .10f),
            Button("start", "Start", .55f, .82f), Button("select", "Select", .43f, .82f), Button("l3", "L3", .30f, .72f), Button("r3", "R3", .68f, .72f),
            Button("extra1", "A", .72f, .88f, false), Button("extra2", "B", .80f, .88f, false), Button("extra3", "X", .88f, .88f, false), Button("extra4", "Y", .96f, .88f, false)
        });

    private static TouchControlLayout Button(string id, string binding, float x, float y, bool visible = true) => new(id, binding, x, y, 1f, visible);
}

public sealed record TouchControlLayout(string Id, string Binding, float X, float Y, float Scale, bool Visible);

public static class TouchLayoutStorage
{
    public static string Serialize(TouchLayoutV1 layout) => JsonSerializer.Serialize(Normalize(layout));

    public static TouchLayoutV1 DeserializeOrDefault(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? TouchLayoutV1.Default : Normalize(JsonSerializer.Deserialize<TouchLayoutV1>(json) ?? TouchLayoutV1.Default); }
        catch (JsonException) { return TouchLayoutV1.Default; }
    }

    public static TouchLayoutV1 Normalize(TouchLayoutV1? layout)
    {
        if (layout is null || layout.Version != TouchLayoutV1.CurrentVersion) return TouchLayoutV1.Default;
        TouchControlLayout dpad = NormalizeControl(layout.DPad, TouchLayoutV1.Default.DPad);
        var defaults = TouchLayoutV1.Default.Buttons;
        var byId = (layout.Buttons ?? Array.Empty<TouchControlLayout>()).Where(x => x is not null).GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        return new TouchLayoutV1(TouchLayoutV1.CurrentVersion, dpad, defaults.Select(d => NormalizeControl(byId.TryGetValue(d.Id, out var value) ? value : d, d)).ToArray());
    }

    private static TouchControlLayout NormalizeControl(TouchControlLayout? value, TouchControlLayout fallback)
    {
        if (value is null || !StringComparer.Ordinal.Equals(value.Id, fallback.Id)) return fallback;
        string binding = AllowedBindings.Contains(value.Binding, StringComparer.Ordinal) ? value.Binding : fallback.Binding;
        return value with { Binding = binding, X = Clamp(value.X, 0f, 1f), Y = Clamp(value.Y, 0f, 1f), Scale = Clamp(value.Scale, .5f, 2f) };
    }

    public static readonly string[] AllowedBindings = ["A", "B", "X", "Y", "L", "R", "ZL", "ZR", "Start", "Select", "L3", "R3"];
    private static float Clamp(float value, float min, float max) => float.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
