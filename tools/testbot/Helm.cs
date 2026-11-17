// Smooth steering for the test bot. Movement follows the camera, so the bot holds forward and steers by turning the
// camera with the arrow keys, the way a player would.
//  - Turns only when needed: it starts turning when the heading is off by more than StartDeg and stops once within
//    StopDeg (hysteresis), so on straight stretches the camera stays still.
//  - Corrects part of the error per tap, at most one tap per MinTapGap: the camera eases and keeps turning a little after
//    the key is released.
//  - Learns the camera's turn rate from every tap (it changes with the game's speed mode and the camera's easing).
//  - Targets more than 70 degrees off ahead use the nearest key direction instead (turning the camera that far would
//    walk the party away from its route first).
sealed class Helm
{
    const double StartDeg = 10, StopDeg = 3, Gain = 0.6;
    static readonly TimeSpan MinTapGap = TimeSpan.FromMilliseconds(150);
    readonly Func<double> _cameraAngle;
    bool _turning;
    DateTime _lastTap = DateTime.MinValue;

    /// <summary>Radians the camera's forward angle turns per ms of Right arrow (signed), refined as it steers.</summary>
    public double Rate { get; private set; }

    public Helm(Func<double> cameraAngle, double initialRate) { _cameraAngle = cameraAngle; Rate = initialRate; }

    /// <summary>One steering update toward a world heading (radians, as Math.Atan2(dz, dx)).</summary>
    public void Update(double heading)
    {
        double cam = _cameraAngle();
        if (!double.IsFinite(cam) || !double.IsFinite(heading)) return; // the camera reads NaN for a moment now and then
        double rel = Norm(heading - cam);
        if (Math.Abs(rel) > 70 * Math.PI / 180)
        {
            var dirs = new (char[] k, double ang)[] { (['W', 'D'], 45), (['D'], 90), (['S', 'D'], 135), (['S'], 180), (['S', 'A'], -135), (['A'], -90), (['W', 'A'], -45) };
            var pick = dirs.OrderBy(o => Math.Abs(Norm(rel - o.ang * Math.PI / 180))).First();
            Input.Keep(pick.k);
            _turning = false;
            return;
        }
        Input.Keep(['W']);
        double err = Math.Abs(rel) * 180 / Math.PI;
        if (!_turning && err > StartDeg) _turning = true;
        else if (_turning && err < StopDeg) _turning = false;
        if (!_turning) { Learn(cam); return; } // a settled reading after the last tap is still worth learning from
        if (Rate == 0 || DateTime.UtcNow - _lastTap < MinTapGap) return;
        int ms = (int)Math.Clamp(Math.Abs(Gain * rel / Rate), 10, 90);
        bool right = rel * Rate > 0;
        Learn(cam);
        _pending = (cam, ms, right);
        Input.HoldArrow(right ? Input.Right : Input.Left, ms);
        _lastTap = DateTime.UtcNow;
    }

    // The tap before this one: how far it turned the camera, measured now that the camera has settled (it eases and
    // keeps turning after the key is released, so measuring right after the tap reads too little).
    (double before, int ms, bool right)? _pending;
    void Learn(double now)
    {
        if (_pending is not { } p || DateTime.UtcNow - _lastTap < MinTapGap) return; // not settled yet
        _pending = null;
        double observed = Norm(now - p.before) / p.ms * (p.right ? 1 : -1);
        if (double.IsFinite(observed) && Math.Sign(observed) == Math.Sign(Rate) && Math.Abs(observed) > Math.Abs(Rate) / 3 && Math.Abs(observed) < Math.Abs(Rate) * 3)
            Rate = 0.8 * Rate + 0.2 * observed;
    }

    static double Norm(double a) => Math.IEEERemainder(a, 2 * Math.PI);
}
