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
public enum Priority
{
    Guaranteed = 0,
    Urgent = 1,
    Critical = 3,
    Important = 5,
    Relevant = 8,
    Default = 13,
    SelfDevelopment = 21,
    Dreams = 34
}