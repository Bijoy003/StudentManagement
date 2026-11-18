using StudentManagement.Domain.Entities;

namespace StudentManagement.Domain.Interfaces;

public interface IAppSettingRepository
{
    Task<IReadOnlyList<AppSetting>> GetAllReadOnlyAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(AppSetting setting, CancellationToken cancellationToken = default);
    Task UpsertRangeAsync(IEnumerable<AppSetting> settings, CancellationToken cancellationToken = default);
}
