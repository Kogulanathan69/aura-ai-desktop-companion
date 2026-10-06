using Aura.Application.Common.Interfaces;
using Aura.Domain.Entities;

namespace Aura.Application.Conversations.Security;

internal static class ConversationOwnershipQueries
{
    internal static IQueryable<Conversation> OwnedConversations(this IAuraDbContext dbContext, Guid userId) =>
        dbContext.Conversations.Where(x => x.UserId == userId &&
            ((x.Type == "General" && x.ProjectId == null) || (x.Type == "Project" && x.ProjectId != null &&
                dbContext.Projects.Any(p => p.Id == x.ProjectId && p.UserId == userId))));
}
