using Safeturned.Shared;

namespace Safeturned.Module.Commands;

public class CommandSafeturned : CommandSafeturnedBase
{
    public CommandSafeturned(ModuleRunner runner, CoroutineRunner coroutineRunner) : base("safeturned", runner, coroutineRunner)
    {
    }
}
