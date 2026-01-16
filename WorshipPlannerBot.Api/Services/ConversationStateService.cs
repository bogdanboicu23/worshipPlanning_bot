using System.Collections.Concurrent;

namespace WorshipPlannerBot.Api.Services;

public class ConversationStateService : IConversationStateService
{
    private readonly ConcurrentDictionary<long, ConversationState> _states = new();

    public void SetUserState(long userId, string state, object? data = null)
    {
        _states[userId] = new ConversationState
        {
            State = state,
            Data = data,
            CreatedAt = DateTime.UtcNow
        };
    }

    public ConversationState? GetUserState(long userId)
    {
        if (_states.TryGetValue(userId, out var state))
        {
            // Clear state if it's older than 10 minutes
            if (state.CreatedAt.AddMinutes(10) < DateTime.UtcNow)
            {
                ClearUserState(userId);
                return null;
            }
            return state;
        }
        return null;
    }

    public void ClearUserState(long userId)
    {
        _states.TryRemove(userId, out _);
    }
}

public interface IConversationStateService
{
    void SetUserState(long userId, string state, object? data = null);
    ConversationState? GetUserState(long userId);
    void ClearUserState(long userId);
}

public class ConversationState
{
    public string State { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime CreatedAt { get; set; }
}