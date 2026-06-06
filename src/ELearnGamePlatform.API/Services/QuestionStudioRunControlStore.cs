using System.Collections.Concurrent;

namespace ELearnGamePlatform.API.Services;

public interface IQuestionStudioRunControlStore
{
    void RegisterRun(int runId);
    bool IsRegistered(int runId);
    bool PauseRun(int runId);
    bool ResumeRun(int runId);
    bool CancelRun(int runId);
    Task<bool> WaitForExecutionAsync(int runId);
    bool SealRun(int runId);
    void CompleteRun(int runId);
}

public sealed class QuestionStudioRunControlStore : IQuestionStudioRunControlStore
{
    private readonly ConcurrentDictionary<int, RunControlState> _runs = new();

    public void RegisterRun(int runId)
        => _runs[runId] = new RunControlState();

    public bool IsRegistered(int runId)
        => _runs.TryGetValue(runId, out var state) && !state.IsCompleted;

    public bool PauseRun(int runId)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return false;
        }

        lock (state)
        {
            if (state.IsPaused || state.IsCancelled || state.IsCompleted || state.IsSealed)
            {
                return false;
            }

            state.IsPaused = true;
            state.ResumeSignal = NewResumeSignal();
            return true;
        }
    }

    public bool ResumeRun(int runId)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return false;
        }

        TaskCompletionSource<bool>? signal;
        lock (state)
        {
            if (!state.IsPaused || state.IsCancelled || state.IsCompleted || state.IsSealed)
            {
                return false;
            }

            state.IsPaused = false;
            signal = state.ResumeSignal;
        }

        signal.TrySetResult(true);
        return true;
    }

    public bool CancelRun(int runId)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return false;
        }

        TaskCompletionSource<bool>? signal;
        lock (state)
        {
            if (state.IsCancelled || state.IsCompleted || state.IsSealed)
            {
                return false;
            }

            state.IsPaused = false;
            state.IsCancelled = true;
            signal = state.ResumeSignal;
        }

        signal.TrySetResult(false);
        return true;
    }

    public async Task<bool> WaitForExecutionAsync(int runId)
    {
        while (_runs.TryGetValue(runId, out var state))
        {
            Task<bool>? waitTask = null;
            lock (state)
            {
                if (state.IsCancelled || state.IsCompleted)
                {
                    return false;
                }

                if (!state.IsPaused)
                {
                    return true;
                }

                waitTask = state.ResumeSignal.Task;
            }

            if (!await waitTask.ConfigureAwait(false))
            {
                return false;
            }
        }

        return false;
    }

    public void CompleteRun(int runId)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return;
        }

        lock (state)
        {
            state.IsPaused = false;
            state.IsCompleted = true;
            state.ResumeSignal.TrySetResult(false);
        }
    }

    public bool SealRun(int runId)
    {
        if (!_runs.TryGetValue(runId, out var state))
        {
            return false;
        }

        lock (state)
        {
            if (state.IsPaused || state.IsCancelled || state.IsCompleted || state.IsSealed)
            {
                return false;
            }

            state.IsSealed = true;
            return true;
        }
    }

    private static TaskCompletionSource<bool> NewResumeSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RunControlState
    {
        public bool IsPaused { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsSealed { get; set; }
        public TaskCompletionSource<bool> ResumeSignal { get; set; } = NewResumeSignal();
    }
}
