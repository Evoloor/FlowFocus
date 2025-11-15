namespace FlowFocus.Core.Enums;

public enum TaskStatus
{
    NotConfigured,
    Planned,
    Active,
    Completed,
    Irrelevant
}

public enum DependencyType
{
    Blocking,
    Related
}

public enum DependencyLogic
{
    And,
    Or
}

public enum DisplayType
{
    Nested,
    Independent
}