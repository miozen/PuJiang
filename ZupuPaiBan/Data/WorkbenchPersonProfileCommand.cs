using Microsoft.EntityFrameworkCore;
using ZupuPaiBan.Domain;
using ZupuPaiBan.Models;

namespace ZupuPaiBan.Data;

public sealed record WorkbenchProfileValues(string Name, PersonSex Sex, string? BirthDate, string? DeathDate, string? Biography);
public sealed record WorkbenchProfileSnapshot(Guid GenealogyId, Guid PersonId, long Generation, WorkbenchProfileValues Values);

/// <summary>Single-person command boundary. No UI or view state is used as data authority.</summary>
public sealed class WorkbenchPersonProfileCommand(ZupuDbContext context)
{
    public async Task<WorkbenchProfileSnapshot> ReadAsync(Guid genealogyId, Guid personId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var generation = await context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).SingleAsync(cancellationToken);
        var person = await context.People.AsNoTracking().SingleOrDefaultAsync(p => p.GenealogyId == genealogyId && p.Id == personId, cancellationToken)
            ?? throw new InvalidOperationException("当前族谱中不存在该人物。");
        var snapshot = new WorkbenchProfileSnapshot(genealogyId, personId, generation,
            new(person.Name, person.Sex, person.BirthDateText, person.DeathDateText, person.Biography));
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task SaveAsync(WorkbenchProfileSnapshot original, WorkbenchProfileValues values, CancellationToken cancellationToken = default)
    {
        var name = PersonProfileRules.Validate(values.Name, values.Sex, values.BirthDate, values.DeathDate, values.Biography);
        // One atomic statement scopes identity and tests the protected generation at the write itself.
        var count = await context.People.Where(p => p.GenealogyId == original.GenealogyId && p.Id == original.PersonId)
            .Where(p => context.Database.SqlQueryRaw<long>(UnlinkGenerationV1.GuardSql).Any(v => v == original.Generation))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name).SetProperty(p => p.Sex, values.Sex)
                .SetProperty(p => p.BirthDateText, values.BirthDate).SetProperty(p => p.DeathDateText, values.DeathDate)
                .SetProperty(p => p.Biography, values.Biography), cancellationToken);
        if (count != 1) throw new InvalidOperationException("人物或数据版本已变化，请重新读取后确认。");
    }
}

/// <summary>Holds one draft; a successful write is never retried with the old token.</summary>
public sealed class WorkbenchPersonDraft(WorkbenchProfileSnapshot original)
{
    private readonly object _gate = new();
    private WorkbenchProfileValues _values = original.Values;
    private bool _saving;
    private bool _isSaved;
    public WorkbenchProfileSnapshot Original { get; } = original;
    public WorkbenchProfileValues Values
    {
        get { lock (_gate) return _values; }
        set { lock (_gate) { if (_saving || _isSaved) throw new InvalidOperationException("保存中或已保存的草稿不能修改。"); _values = value; } }
    }
    public bool IsDirty { get { lock (_gate) return !_isSaved && _values != Original.Values; } }
    public bool IsSaved { get { lock (_gate) return _isSaved; } }

    public async Task SaveAsync(WorkbenchPersonProfileCommand command, CancellationToken cancellationToken = default)
    {
        WorkbenchProfileValues values;
        lock (_gate)
        {
            if (_isSaved || _saving) throw new InvalidOperationException("草稿正在保存或已保存，请重新读取人物。");
            _saving = true;
            values = _values;
        }
        try
        {
            await command.SaveAsync(Original, values, cancellationToken);
            lock (_gate) _isSaved = true;
        }
        finally { lock (_gate) _saving = false; }
    }
}
