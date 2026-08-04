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

    public static PriorityLevel Critical => new PriorityLevelBuilder().WithId(id: 1).WithOrder(order: 1).WithName(name: "Критический").WithColor(color: "#FF4444").Build();
    public static PriorityLevel High => new PriorityLevelBuilder().WithId(id: 2).WithOrder(order: 2).WithName(name: "Высокий").WithColor(color: "#FF8C00").Build();
    public static PriorityLevel Medium => new PriorityLevelBuilder().WithId(id: 3).WithOrder(order: 3).WithName(name: "Средний").WithColor(color: "#FFD700").Build();
    public static PriorityLevel Low => new PriorityLevelBuilder().WithId(id: 4).WithOrder(order: 4).WithName(name: "Низкий").WithColor(color: "#4CAF50").Build();
    public static PriorityLevel Background => new PriorityLevelBuilder().WithId(id: 5).WithOrder(order: 5).WithName(name: "Фоновый").WithColor(color: "#2196F3").Build();
}
