using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Storage;
using FlowFocus.Data;
using AppState = FlowFocus.Core.AppState;

namespace FlowFocus.WebApp;

public class AppManager : AppStateManager
{
    public AppManager():base()
    {
        Storage = new DbStorageService<AppState>(StorageKey.Tasks);
    }
}