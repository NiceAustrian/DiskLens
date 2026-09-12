namespace DiskLens.UI.Animation;

public static class Easing
{
    public static float Linear(float t) => t;
    public static float OutCubic(float t) => 1 - MathF.Pow(1 - t, 3);
    public static float InOutCubic(float t) => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
    public static float OutQuint(float t) => 1 - MathF.Pow(1 - t, 5);
    public static float OutExpo(float t) => t >= 1 ? 1 : 1 - MathF.Pow(2, -10 * t);
    public static float OutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }
}

/// <summary>Something the animator ticks every frame until it reports it is done.</summary>
public interface IAnimation
{
    /// <summary>Advances by <paramref name="dt"/> seconds. Returns false when finished.</summary>
    bool Tick(float dt);
}

/// <summary>A float that glides towards its target. Read <see cref="Value"/> every frame while animating.</summary>
public sealed class Tween : IAnimation
{
    private float _from, _to, _elapsed, _duration;
    private Func<float, float> _easing = Easing.OutCubic;
    private bool _active;

    public Tween(float initial)
    {
        Value = _from = _to = initial;
    }

    public float Value { get; private set; }
    public float Target => _to;
    public bool IsActive => _active;
    public event Action? Completed;

    /// <summary>Starts (or retargets) the animation. Returns this so it can be handed to the animator.</summary>
    public Tween To(float target, float durationSeconds = 0.25f, Func<float, float>? easing = null)
    {
        if (Math.Abs(target - _to) < 1e-4f && (_active || Math.Abs(Value - target) < 1e-4f)) return this;
        _from = Value;
        _to = target;
        _elapsed = 0;
        _duration = Math.Max(0.0001f, durationSeconds);
        _easing = easing ?? Easing.OutCubic;
        _active = true;
        return this;
    }

    public void Jump(float value)
    {
        Value = _from = _to = value;
        _active = false;
    }

    public bool Tick(float dt)
    {
        if (!_active) return false;
        _elapsed += dt;
        var t = Math.Clamp(_elapsed / _duration, 0, 1);
        Value = _from + (_to - _from) * _easing(t);
        if (t >= 1)
        {
            Value = _to;
            _active = false;
            Completed?.Invoke();
            return false;
        }
        return true;
    }
}

/// <summary>Drives all running animations; owned by the UI root.</summary>
public sealed class Animator
{
    private readonly HashSet<IAnimation> _running = [];
    private readonly List<IAnimation> _scratch = [];

    public bool HasWork => _running.Count > 0;

    public void Run(IAnimation animation) => _running.Add(animation);

    public void Stop(IAnimation animation) => _running.Remove(animation);

    /// <summary>Ticks everything. Returns true if any animation is still running afterwards.</summary>
    public bool Tick(float dt)
    {
        if (_running.Count == 0) return false;
        _scratch.Clear();
        _scratch.AddRange(_running);
        foreach (var a in _scratch)
        {
            if (!a.Tick(dt)) _running.Remove(a);
        }
        return _running.Count > 0;
    }
}
