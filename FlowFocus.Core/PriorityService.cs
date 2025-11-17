using FlowFocus.Core.Enums;
using Color = System.Drawing.Color;
namespace FlowFocus.Core;
public record ColorCheckpoint(int Priority, Color Color)
{
    public static implicit operator ColorCheckpoint((int priority, Color color) tuple)
        => new(tuple.priority, tuple.color);
}
public class PriorityColorGradient
{
    private readonly List<ColorCheckpoint> _checkpoints;

    private PriorityColorGradient(IEnumerable<ColorCheckpoint> checkpoints)
    {
        _checkpoints = checkpoints.OrderBy(c => c.Priority).ToList();

        if (_checkpoints.Count < 2)
            throw new ArgumentException("Need at least 2 checkpoints for gradient");
    }

    public PriorityColorGradient(params ColorCheckpoint[] checkpoints)
        : this(checkpoints.AsEnumerable())
    {
    }

    public Color GetColor(int priority)
    {
        if (priority <= _checkpoints[0].Priority)
            return _checkpoints[0].Color;

        if (priority >= _checkpoints[^1].Priority)
            return _checkpoints[^1].Color;

        for (var i = 0; i < _checkpoints.Count - 1; i++)
        {
            var current = _checkpoints[i];
            var next = _checkpoints[i + 1];

            if (priority >= current.Priority && priority <= next.Priority)
            {
                return InterpolateColorHsl(current, next, priority);
            }
        }

        return Color.BlueViolet;
    }

    private Color InterpolateColorHsl(ColorCheckpoint from, ColorCheckpoint to, int currentPriority)
    {
        var progress = (float)(currentPriority - from.Priority) / (to.Priority - from.Priority);

        // Конвертируем в HSL
        var fromHsl = RgbToHsl(from.Color);
        var toHsl = RgbToHsl(to.Color);

        // Интерполируем HSL компоненты
        var h = LerpHsl(fromHsl.H, toHsl.H, progress, true);
        var s = Lerp(fromHsl.S, toHsl.S, progress);
        var l = Lerp(fromHsl.L, toHsl.L, progress);

        // Конвертируем обратно в RGB
        return HslToRgb(h, s, l);
    }

    private static (float H, float S, float L) RgbToHsl(Color color)
    {
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        float h = 0f, s = 0f, l = (max + min) / 2f;

        if (delta == 0) return (h, s, l);
        s = l < 0.5f ? delta / (max + min) : delta / (2f - max - min);

        if (Math.Abs(max - r) < 0.1) h = (g - b) / delta + (g < b ? 6f : 0f);
        else if (Math.Abs(max - g) < 0.1) h = (b - r) / delta + 2f;
        else if (Math.Abs(max - b) < 0.1) h = (r - g) / delta + 4f;

        h /= 6f;

        return (h, s, l);
    }

    private static Color HslToRgb(float h, float s, float l)
    {
        float r, g, b;

        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;

            r = HueToRgb(p, q, h + 1f/3f);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1f/3f);
        }

        return Color.FromArgb(
            (byte)(r * 255),
            (byte)(g * 255),
            (byte)(b * 255)
        );
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;

        return t switch
        {
            < 1f / 6f => p + (q - p) * 6f * t,
            < 1f / 2f => q,
            < 2f / 3f => p + (q - p) * (2f / 3f - t) * 6f,
            _ => p
        };
    }

    private static float LerpHsl(float start, float end, float progress, bool isHue = false)
    {
        if (!isHue) return start + (end - start) * progress;

        // Специальная интерполяция для Hue (учитываем круговую природу)
        var delta = end - start;
        if (Math.Abs(delta) > 0.5f)
        {
            if (delta > 0) start += 1f;
            else end += 1f;
        }
        
        var result = start + (end - start) * progress;
        return result % 1f;
    }

    private static float Lerp(float start, float end, float progress)
        => start + (end - start) * progress;
}
public class PriorityService
{
    private const int DefaultMinPriority = 0;
    private const int DefaultMaxPriority = 35;
    public int MinPriority => DefaultMinPriority;
    public int MaxPriority => DefaultMaxPriority;
    private readonly Color _defaultColor = Color.DodgerBlue;
    private readonly PriorityColorGradient _gradient = new(
        (DefaultMinPriority, Color.DarkRed),
        ((int)Priority.Important, Color.DarkOrange),
        ((int)Priority.Default, Color.ForestGreen),
        ((int)Priority.SelfDevelopment, Color.DodgerBlue),
        (DefaultMaxPriority, Color.Indigo)
    );

    public static PriorityService Shared { get; } = new();
    public Color GetColor(Priority priority) => GetColor((int)priority);
    public Color GetColor(int? priority)
    {
        return priority is null ? _defaultColor : _gradient.GetColor(priority.Value);
    }
    public string GetName(Priority priority) => priority switch
    {
        Priority.Guaranteed => "Гарантировано",
        Priority.Urgent => "Неотложно",
        Priority.Critical => "Срочно",
        Priority.Important => "Важно",
        Priority.Relevant => "Актуально",
        Priority.Default => "Стандарт",
        Priority.SelfDevelopment => "Саморазвитие",
        Priority.Dreams => "Мечты",
        _ => $"Приоритет {priority}"
    };

    public bool HasReferenceValue(int? userPriority)
    {
        return userPriority.HasValue && Enum.IsDefined(typeof(Priority), userPriority);
    }
    public bool TryGetPriorityName(int? userPriority, out string priorityName)
    {
        if (HasReferenceValue(userPriority))
        {
            priorityName = GetName((Priority)userPriority.Value);
            return true;
        }
        priorityName = $"Приоритет {userPriority}";
        return false;
    }
}
