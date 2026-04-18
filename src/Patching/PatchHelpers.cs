namespace DgLabSocketSpire2.Patching;

internal static class PatchHelpers
{
    public static Task Chain(Task original, Action action)
    {
        return Chain(original, () =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    public static async Task Chain(Task original, Func<Task> action)
    {
        await original;
        await action();
    }
}
