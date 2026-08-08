using FlowFocus.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace FlowFocus.Blazor.EditDialogContents;

public abstract class LabelInputBase<TLabel> : ComponentBase where TLabel : TaskLabelBase
{
    [Parameter] public List<TLabel> SuggestedLabels { get; set; } = [];
    [Parameter] public List<TLabel> SelectedLabels { get; set; } = [];
    [Parameter] public HashSet<int> SelectedLabelIds { get; set; } = [];
    [Parameter] public EventCallback<TLabel> OnLabelAdded { get; set; }
    [Parameter] public EventCallback<TLabel> OnLabelRemoved { get; set; }

    protected string NewInput { get; set; } = string.Empty;
    protected List<TLabel> SearchResults { get; set; } = [];
    protected MudTextField<string>? InputRef;
    protected int InputResetKey;

    protected bool IsAddButtonDisabled => string.IsNullOrWhiteSpace(NewInput);

    protected abstract List<TLabel> SearchLabels(string query);
    protected abstract TLabel? GetByName(string name);
    protected abstract TLabel GetOrCreate(string name);

    protected async Task OnInputChanged()
    {
        if (string.IsNullOrWhiteSpace(NewInput))
        {
            SearchResults = [];
            await InvokeAsync(StateHasChanged);
            return;
        }

        SearchResults = SearchLabels(NewInput);
        await InvokeAsync(StateHasChanged);
    }

    protected async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(NewInput))
        {
            await AddNew();
        }
        else if (e.Key == "Escape")
        {
            await ResetInputFocus();
        }
    }

    protected async Task AddNew()
    {
        if (string.IsNullOrWhiteSpace(NewInput)) return;

        var name = NewInput.Trim();
        var existing = GetByName(name);
        var label = existing ?? GetOrCreate(name);
        await AddLabel(label);
    }

    protected async Task AddLabel(TLabel label)
    {
        if (SelectedLabelIds.Contains(label.Id)) return;
        SelectedLabels.Add(label);
        SelectedLabelIds.Add(label.Id);
        await OnLabelAdded.InvokeAsync(label);
        await ResetInputFocus();
    }

    protected async Task RemoveLabel(TLabel label)
    {
        SelectedLabels.RemoveAll(x => x.Id == label.Id);
        SelectedLabelIds.Remove(label.Id);
        await OnLabelRemoved.InvokeAsync(label);
    }

    protected async Task ResetInputFocus()
    {
        NewInput = string.Empty;
        SearchResults = [];
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
        InputResetKey++;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
        await Task.Delay(30);
        if (InputRef != null)
        {
            await InputRef.FocusAsync();
        }
    }
}
