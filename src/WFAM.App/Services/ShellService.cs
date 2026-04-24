namespace WFAM.App.Services;

/// <summary>
/// 资源管理器刷新通知。
/// </summary>
public interface IShellService
{
    void NotifyAssocChanged();
}

public sealed class ShellService : IShellService
{
    public void NotifyAssocChanged()
    {
        Helpers.NativeMethods.SHChangeNotify(
            Helpers.NativeMethods.SHCNE_ASSOCCHANGED,
            Helpers.NativeMethods.SHCNF_IDLIST,
            IntPtr.Zero, IntPtr.Zero);
    }
}
