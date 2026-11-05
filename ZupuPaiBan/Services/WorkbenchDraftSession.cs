using ZupuPaiBan.Data;

namespace ZupuPaiBan.Services;

public enum DraftLeaveChoice { Save, Discard, Cancel }
public enum DraftTransitionResult { Completed, Cancelled, SaveFailed, SavedRefreshFailed, ReadFailed, SavedThenReadFailed }

/// <summary>Serializes a single-person editing session and makes commit and refresh outcomes explicit.</summary>
public sealed class WorkbenchDraftSession
{
    private readonly Func<IDisposable> _enterOperation;
    private readonly Func<Guid, Guid, Task<WorkbenchProfileSnapshot>> _read;
    private readonly Func<WorkbenchPersonDraft, Task> _save;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public WorkbenchPersonDraft? Draft { get; private set; }
    public string? Error { get; private set; }
    public Func<Guid, Task>? AfterCommitAsync { get; set; }

    public WorkbenchDraftSession(Func<IDisposable> enterOperation,
        Func<Guid, Guid, Task<WorkbenchProfileSnapshot>> read, Func<WorkbenchPersonDraft, Task> save)
    {
        _enterOperation = enterOperation; _read = read; _save = save;
    }

    public static WorkbenchDraftSession Create(TargetContextCoordinator coordinator) => new(coordinator.EnterOperation,
        async (genealogy, person) =>
        {
            await using var context = coordinator.CreateContext();
            return await new WorkbenchPersonProfileCommand(context).ReadAsync(genealogy, person);
        },
        async draft =>
        {
            await using var context = coordinator.CreateContext();
            await draft.SaveAsync(new WorkbenchPersonProfileCommand(context));
        });

    public async Task<DraftTransitionResult> SelectAsync(Guid genealogyId, Guid personId, DraftLeaveChoice choice)
    {
        await _gate.WaitAsync();
        try
        {
            if (Draft is { } current && current.Original.GenealogyId == genealogyId && current.Original.PersonId == personId)
                return DraftTransitionResult.Completed;
            using var operation = _enterOperation();
            var saved = Draft?.IsDirty == true && choice == DraftLeaveChoice.Save;
            var result = await LeaveCoreAsync(choice);
            if (result != DraftTransitionResult.Completed) return result;
            try { Draft = new WorkbenchPersonDraft(await _read(genealogyId, personId)); Error = null; return DraftTransitionResult.Completed; }
            catch (Exception error) { Error = error.Message; return saved ? DraftTransitionResult.SavedThenReadFailed : DraftTransitionResult.ReadFailed; }
        }
        finally { _gate.Release(); }
    }

    public async Task<DraftTransitionResult> LeaveAsync(DraftLeaveChoice choice)
    {
        await _gate.WaitAsync();
        try { using var operation = _enterOperation(); return await LeaveCoreAsync(choice); }
        finally { _gate.Release(); }
    }

    public async Task<DraftTransitionResult> SaveAsync()
    {
        await _gate.WaitAsync();
        try { using var operation = _enterOperation(); return await SaveCoreAsync(); }
        finally { _gate.Release(); }
    }

    private async Task<DraftTransitionResult> LeaveCoreAsync(DraftLeaveChoice choice)
    {
        if (Draft?.IsDirty == true)
        {
            if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
            if (choice == DraftLeaveChoice.Cancel) return DraftTransitionResult.Cancelled;
            if (choice == DraftLeaveChoice.Save)
            {
                var result = await SaveCoreAsync();
                if (result != DraftTransitionResult.Completed) return result;
            }
        }
        Draft = null; Error = null;
        return DraftTransitionResult.Completed;
    }

    private async Task<DraftTransitionResult> SaveCoreAsync()
    {
        if (Draft is not { } draft || !draft.IsDirty) return DraftTransitionResult.Completed;
        try { await _save(draft); }
        catch (Exception error) { Error = error.Message; return DraftTransitionResult.SaveFailed; }
        // Once committed, remove the old token before any fallible refresh.
        Draft = null;
        try
        {
            if (AfterCommitAsync is not null) await AfterCommitAsync(draft.Original.GenealogyId);
            Draft = new WorkbenchPersonDraft(await _read(draft.Original.GenealogyId, draft.Original.PersonId));
            Error = null;
            return DraftTransitionResult.Completed;
        }
        catch (Exception error) { Error = error.Message; return DraftTransitionResult.SavedRefreshFailed; }
    }
}
