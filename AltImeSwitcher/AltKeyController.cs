namespace AltImeSwitcher;

internal enum ImeMode { Alphanumeric, Hiragana }
internal readonly record struct ImeRequest(ImeMode Mode, nint Foreground);
internal readonly record struct KeyEvent(int VirtualKey, ushort ScanCode, bool Extended, bool Down)
{
    public NativeMethods.Input ToInput() => ScanCode != 0 && VirtualKey is not (0x13 or 0x2C)
        ? NativeMethods.Input.ScanCode(ScanCode, Extended, Down)
        : NativeMethods.Input.Keyboard(VirtualKey, Down);
}

// All calls, including Tick, run on the hook thread. No locks or thread-pool timers.
internal sealed class AltKeyController(Func<NativeMethods.Input[], uint> send, Action<ImeRequest> requestIme,
    int holdMilliseconds = AppSettings.DefaultHoldMilliseconds)
{
    private int _holdMilliseconds = AppSettings.ValidateHoldMilliseconds(holdMilliseconds);
    private readonly AltState _left = new(NativeMethods.VK_LMENU);
    private readonly AltState _right = new(NativeMethods.VK_RMENU);
    private readonly HashSet<int> _heldKeys = [];
    private readonly Dictionary<int, KeyEvent> _replayedKeys = [];
    private readonly Dictionary<(uint Flags, uint Data), NativeMethods.Input> _mouseReleases = [];

    public void SetHoldMilliseconds(int value)
    {
        if (_holdMilliseconds != value) _holdMilliseconds = AppSettings.ValidateHoldMilliseconds(value);
    }

    public void Initialize(IEnumerable<int> heldKeys)
    {
        foreach (var key in heldKeys)
        {
            var state = GetAlt(key);
            if (state is not null) { state.Down = true; state.PassThrough = true; }
            else _heldKeys.Add(key);
        }
    }

    public bool Process(KeyEvent key, long now, nint foreground, bool modifierHeld = false)
    {
        var state = GetAlt(key.VirtualKey);
        if (state is null)
        {
            if (key.Down) _heldKeys.Add(key.VirtualKey);
            else _heldKeys.Remove(key.VirtualKey);
            if (_replayedKeys.ContainsKey(key.VirtualKey))
            {
                if (!key.Down) _replayedKeys.Remove(key.VirtualKey);
                return send([key.ToInput()]) == 1;
            }
            var replayed = key.Down && ForwardFollowing(key.ToInput());
            if (replayed) _replayedKeys[key.VirtualKey] = key;
            return replayed;
        }

        if (key.Down)
        {
            if (state.Down) return !state.PassThrough;
            state.Down = true;
            state.Start = now;
            state.HoldMilliseconds = _holdMilliseconds;
            state.Foreground = foreground;
            state.CanTap = true;
            // Preserve Ctrl+Alt, Shift+Alt, Win+Alt and AltGr when a key was already held.
            if (_heldKeys.Count != 0 || modifierHeld)
            {
                state.PassThrough = true;
                _ = ForwardPending();
                return false;
            }
            if (Other(state).Down) _ = ForwardPending();
            return true;
        }

        if (!state.Down) return false; // An up from before installation must not be consumed.
        if (state.PassThrough)
        {
            state.Reset();
            return false;
        }
        if (state.Forwarded)
        {
            state.Reset();
            // Queue up after our queued down, even if physical input arrives in a rapid burst.
            return send([NativeMethods.Input.Alt(key.VirtualKey, false)]) == 1;
        }

        var isTap = state.CanTap && now - state.Start < state.HoldMilliseconds;
        var target = state.Foreground;
        state.Reset();
        if (isTap)
        {
            if (target != 0 && target == foreground)
                requestIme(new(key.VirtualKey == NativeMethods.VK_LMENU ? ImeMode.Alphanumeric : ImeMode.Hiragana, target));
            return true;
        }

        // Timer delivery may be late. Preserve long-press behavior even in that case.
        var sent = send([NativeMethods.Input.Alt(key.VirtualKey, true), NativeMethods.Input.Alt(key.VirtualKey, false)]);
        return sent == 2; // With only down inserted, allow the physical up to release it.
    }

    public void Tick(long now)
    {
        if ((_left.Pending && now - _left.Start >= _left.HoldMilliseconds) ||
            (_right.Pending && now - _right.Start >= _right.HoldMilliseconds))
            _ = ForwardPending();
    }

    public bool ProcessMouse(NativeMethods.Input input)
    {
        var flags = input.Data.Mouse.Flags;
        var data = input.Data.Mouse.MouseData;
        if (_mouseReleases.Remove((flags, data))) return send([input]) == 1;
        if (flags is not (0x2 or 0x8 or 0x20 or 0x80 or 0x800 or 0x1000)) return false;
        if (!ForwardFollowing(input)) return false;
        var up = flags switch { 0x2 => 0x4u, 0x8 => 0x10u, 0x20 => 0x40u, 0x80 => 0x100u, _ => 0u };
        if (up != 0) _mouseReleases[(up, data)] = NativeMethods.Input.Mouse(up, data);
        return true;
    }

    public void ReleaseAll()
    {
        var releases = _replayedKeys.Values.Select(k => (k with { Down = false }).ToInput()).ToList();
        releases.AddRange(_mouseReleases.Values);
        _replayedKeys.Clear();
        _mouseReleases.Clear();
        foreach (var state in new[] { _left, _right })
        {
            if (state.Forwarded) releases.Add(NativeMethods.Input.Alt(state.Key, false));
            state.Reset();
        }
        _heldKeys.Clear();
        if (releases.Count != 0) _ = send(releases.ToArray());
    }

    private bool ForwardFollowing(NativeMethods.Input following)
    {
        if (_left.Pending || _right.Pending) return ForwardPending(following);
        // The timer's down may still be queued. Keep following events in the same replay stream.
        return (_left.Forwarded || _right.Forwarded) && send([following]) == 1;
    }

    private bool ForwardPending(NativeMethods.Input? following = null)
    {
        if (!_left.Pending && !_right.Pending) return false;
        var pending = new[] { _left, _right }.Where(s => s.Pending).OrderBy(s => s.Start).ToArray();
        if (pending.Length == 0) return false;
        foreach (var state in pending) state.CanTap = false;
        var batch = pending.Select(s => NativeMethods.Input.Alt(s.Key, true)).ToList();
        if (following.HasValue) batch.Add(following.Value);
        // A single SendInput batch guarantees Alt down precedes the triggering key/mouse input.
        var sent = send(batch.ToArray());
        for (var i = 0; i < pending.Length && i < sent; i++) pending[i].Forwarded = true;
        return following.HasValue && sent == batch.Count;
    }

    private AltState? GetAlt(int key) => key switch
    {
        NativeMethods.VK_LMENU => _left,
        NativeMethods.VK_RMENU => _right,
        _ => null
    };
    private AltState Other(AltState state) => state == _left ? _right : _left;

    private sealed class AltState(int key)
    {
        public int Key { get; } = key;
        public bool Down, Forwarded, PassThrough, CanTap;
        public long Start;
        public int HoldMilliseconds;
        public nint Foreground;
        public bool Pending => Down && !Forwarded && !PassThrough;
        public void Reset() { Down = Forwarded = PassThrough = CanTap = false; Start = 0; Foreground = 0; HoldMilliseconds = 0; }
    }
}
