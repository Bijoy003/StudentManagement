using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Infrastructure.Repositories;

public class ErrorLogRepository : IErrorLogRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ReadOnlyDbContext _readOnlyDbContext;

    public ErrorLogRepository(ApplicationDbContext dbContext, ReadOnlyDbContext readOnlyDbContext)
    {
        _dbContext = dbContext;
        _readOnlyDbContext = readOnlyDbContext;
    }

    public async Task AddAsync(ErrorLog errorLog, CancellationToken cancellationToken = default)
    {
        _dbContext.ErrorLogs.Add(errorLog);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ErrorLog>> GetRecentReadOnlyAsync(int take, string? source, DateTime? sinceUtc, CancellationToken cancellationToken = default)
    {
        var query = _readOnlyDbContext.ErrorLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(log => log.Source.Contains(source));
        }

        if (sinceUtc is not null)
        {
            query = query.Where(log => log.TimestampUtc >= sinceUtc.Value);
        }

        return await query
            .OrderByDescending(log => log.TimestampUtc)
            .ThenByDescending(log => log.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
