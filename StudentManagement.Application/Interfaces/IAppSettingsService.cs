using StudentManagement.Application.Configuration;

namespace StudentManagement.Application.Interfaces;

public interface IAppSettingsService
{
    Task<ChatSettings> GetChatSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveChatSettingsAsync(ChatSettings settings, string? updatedByUserId, CancellationToken cancellationToken = default);
}
