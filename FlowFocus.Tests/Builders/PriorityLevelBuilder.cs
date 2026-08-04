using FlowFocus.Core.Models;

namespace FlowFocus.Tests.Builders;

public class PriorityLevelBuilder
{
    private int _id = 1;
    private int _order = 3;
    private string _name = "Medium";
    private string _color = "#FFD700";

    public PriorityLevelBuilder WithId(int id)
    {
        _id = id;
        return this;
    }

    public PriorityLevelBuilder WithOrder(int order)
    {
        _order = order;
        return this;
    }

    public PriorityLevelBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public PriorityLevelBuilder WithColor(string color)
    {
        _color = color;
        return this;
    }

    public PriorityLevel Build()
    {
        return new()
        {
            Id = _id,
            Order = _order,
            Name = _name,
            Color = _color,
            LastChangesOn = DateTime.UtcNow
        };
    }

    public static PriorityLevel Critical => new PriorityLevelBuilder().WithId(1).WithOrder(1).WithName("Критический").WithColor("#FF4444").Build();
    public static PriorityLevel High => new PriorityLevelBuilder().WithId(2).WithOrder(2).WithName("Высокий").WithColor("#FF8C00").Build();
    public static PriorityLevel Medium => new PriorityLevelBuilder().WithId(3).WithOrder(3).WithName("Средний").WithColor("#FFD700").Build();
    public static PriorityLevel Low => new PriorityLevelBuilder().WithId(4).WithOrder(4).WithName("Низкий").WithColor("#4CAF50").Build();
    public static PriorityLevel Background => new PriorityLevelBuilder().WithId(5).WithOrder(5).WithName("Фоновый").WithColor("#2196F3").Build();
}
