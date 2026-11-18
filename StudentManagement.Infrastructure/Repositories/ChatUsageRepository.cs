using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Infrastructure.Repositories;

public class ChatUsageRepository : IChatUsageRepository
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ReadOnlyDbContext _readOnlyDbContext;

    public ChatUsageRepository(ApplicationDbContext dbContext, ReadOnlyDbContext readOnlyDbContext)
    {
        _dbContext = dbContext;
        _readOnlyDbContext = readOnlyDbContext;
    }

    public async Task AddAsync(ChatUsageLog log, CancellationToken cancellationToken = default)
    {
        _dbContext.ChatUsageLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetTotalTokensForUserOnDateReadOnlyAsync(string userId, DateTime usageDateUtc, CancellationToken cancellationToken = default)
    {
        return await _readOnlyDbContext.ChatUsageLogs
            .AsNoTracking()
            .Where(log => log.UserId == userId && log.UsageDateUtc == usageDateUtc)
            .SumAsync(log => log.TotalTokens, cancellationToken);
    }
}
