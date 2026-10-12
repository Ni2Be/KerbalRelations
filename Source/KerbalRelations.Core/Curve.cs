using System.Collections.Generic;

namespace KerbalRelations.Core
{
    /// <summary>Piecewise-linear curve, clamped at both ends.</summary>
    public class Curve
    {
        private readonly List<KeyValuePair<double, double>> _keys = new List<KeyValuePair<double, double>>();

        public int Count
        {
            get { return _keys.Count; }
        }

        public Curve Add(double x, double y)
        {
            int index = 0;
            while (index < _keys.Count && _keys[index].Key < x)
                ++index;
            _keys.Insert(index, new KeyValuePair<double, double>(x, y));
            return this;
        }

        public double Evaluate(double x)
        {
            if (_keys.Count == 0)
                return 1d;
            if (x <= _keys[0].Key)
                return _keys[0].Value;
            var last = _keys[_keys.Count - 1];
            if (x >= last.Key)
                return last.Value;
            for (int i = 1; i < _keys.Count; ++i)
            {
                if (x <= _keys[i].Key)
                {
                    var a = _keys[i - 1];
                    var b = _keys[i];
                    var span = b.Key - a.Key;
                    if (span <= 0d)
                        return b.Value;
                    return a.Value + (b.Value - a.Value) * (x - a.Key) / span;
                }
            }
            return last.Value;
        }

        /// <summary>0 % happiness halves, 100 % is neutral, 200 % doubles.</summary>
        public static Curve Default()
        {
            return new Curve().Add(0d, 0.5d).Add(100d, 1d).Add(200d, 2d);
        }
    }

    /// <summary>Maps happiness to a multiplier, with one curve per named effect.</summary>
    public class EffectRegistry
    {
        public const string RunSpeed = "RunSpeed";
        public const string Research = "Research";
        public const string Experience = "Experience";

        private readonly Dictionary<string, Curve> _curves = new Dictionary<string, Curve>();
        private readonly Curve _default = Curve.Default();

        public IEnumerable<string> Names
        {
            get { return _curves.Keys; }
        }

        public void Set(string effect, Curve curve)
        {
            _curves[effect] = curve;
        }

        public bool Has(string effect)
        {
            return _curves.ContainsKey(effect);
        }

        /// <summary>Unknown effects use the default curve, so callers never need to register first.</summary>
        public double GetMultiplier(string effect, double happiness)
        {
            Curve curve;
            if (effect != null && _curves.TryGetValue(effect, out curve))
                return curve.Evaluate(happiness);
            return _default.Evaluate(happiness);
        }
    }
}
