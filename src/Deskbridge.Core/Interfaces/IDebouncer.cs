namespace Deskbridge.Core.Interfaces;

public interface IDebouncer
{
    void Schedule(Action action);
    void Cancel();
}
