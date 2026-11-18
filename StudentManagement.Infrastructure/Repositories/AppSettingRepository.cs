using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Infrastructure.Repositories;

public class AppSettingRepository : IAppSettingRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ReadOnlyDbContext _readOnlyDbContext;

    public AppSettingRepository(ApplicationDbContext dbContext, ReadOnlyDbContext readOnlyDbContext)
    {
        _dbContext = dbContext;
        _readOnlyDbContext = readOnlyDbContext;
    }

    public async Task<IReadOnlyList<AppSetting>> GetAllReadOnlyAsync(CancellationToken cancellationToken = default)
    {
        return await _readOnlyDbContext.AppSettings.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(AppSetting setting, CancellationToken cancellationToken = default)
    {
        await UpsertRangeAsync([setting], cancellationToken);
    }

    public async Task UpsertRangeAsync(IEnumerable<AppSetting> settings, CancellationToken cancellationToken = default)
    {
        foreach (var setting in settings)
        {
            var existing = await _dbContext.AppSettings.FirstOrDefaultAsync(s => s.Key == setting.Key, cancellationToken);
            if (existing is null)
            {
                _dbContext.AppSettings.Add(setting);
            }
            else
            {
                existing.Value = setting.Value;
                existing.UpdatedAtUtc = setting.UpdatedAtUtc;
                existing.UpdatedByUserId = setting.UpdatedByUserId;
            }
        }

        // One SaveChanges for the whole batch: a failure leaves no partially-applied settings.
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
