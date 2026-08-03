namespace FlowFocus.Core.Exceptions;

/// <summary>
/// Исключение, возникающее при обнаружении циклической зависимости в графе связей задач.
/// </summary>
public class CircularDependencyException : Exception
{
    public CircularDependencyException() 
        : base("Обнаружена циклическая зависимость между задачами.") { }

    public CircularDependencyException(string message) 
        : base(message) { }

    public CircularDependencyException(string message, Exception innerException) 
        : base(message, innerException) { }
}
