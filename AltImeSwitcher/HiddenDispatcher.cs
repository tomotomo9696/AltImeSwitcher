namespace AltImeSwitcher;

internal class HiddenDispatcher : Control
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    public HiddenDispatcher() => SetVisibleCore(false);

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Keep a hidden top-level HWND: message-only windows miss power broadcasts.
            parameters.Parent = 0;
            parameters.Style &= ~(WsChild | WsVisible);
            parameters.ExStyle = (parameters.ExStyle & ~WsExAppWindow) | WsExToolWindow | WsExNoActivate;
            parameters.Width = 0;
            parameters.Height = 0;
            return parameters;
        }
    }
}
