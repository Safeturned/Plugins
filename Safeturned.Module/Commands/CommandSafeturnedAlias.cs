using Safeturned.Shared;

namespace Safeturned.Module.Commands;

public class CommandSafeturnedAlias : CommandSafeturnedBase
{
    public CommandSafeturnedAlias(ModuleRunner runner, CoroutineRunner coroutineRunner) : base("st", runner, coroutineRunner)
    {
    }
}
