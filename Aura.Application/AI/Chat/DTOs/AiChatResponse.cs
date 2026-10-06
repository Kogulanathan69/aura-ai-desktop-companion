using Aura.Application.AI.Models;
using Aura.Application.Conversations.DTOs;

namespace Aura.Application.AI.Chat.DTOs;

public enum AiChatStatus { Success, NotFound, Denied, GenerationFailed, PersistenceFailed }

public sealed record AiChatResponse(AiChatStatus Status, MessageDto? UserMessage = null,
    MessageDto? AssistantMessage = null, AiGenerationStatus? GenerationStatus = null);
