using System;

namespace FlowFocus.Core;

public static class ServiceCollectionExtensions
{
    [Obsolete("Use FlowFocus.Data.ServiceCollectionExtensions.AddDataLayer(...) from the FlowFocus.Data project to register services.")]
    // Этот stub оставлен намеренно — реальная регистрация сервисов выполняется в FlowFocus.Data
    public static void AddFlowFocusServices()
    {
        throw new NotSupportedException("Use FlowFocus.Data.AddDataLayer in startup to register FlowFocus services.");
    }
}
