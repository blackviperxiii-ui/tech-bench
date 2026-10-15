using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace J1939Reader
{
    /// <summary>One second of engine readings. Doubles use NaN for "not published".</summary>
    internal struct TrendSample
    {
        public DateTime Time;
        public double Rpm;
        public double CoolantC;
        public double OilKpa;
        public double BatteryV;
        public double FuelLph;
        public bool Red;
        public bool Amber;
        public int ActiveCodes;

        public static bool Has(double v)
        {
            return !double.IsNaN(v);
        }
    }

    internal enum TrendChannel
    {
        Rpm = 0,
        Coolant = 1,
        Oil = 2,
        Battery = 3,
        Fuel = 4
    }

    /// <summary>
    /// Rolling 1 Hz history of the live readings. The app was already sampling these every 40 ms and
    /// discarding them, which made "did oil pressure drop before it shut down?" unanswerable.
    /// </summary>
    internal sealed class TrendLog
    {
        public const int DefaultCapacity = 30 * 60; // 30 minutes at 1 Hz

        readonly TrendSample[] _buf;
        int _start;
        int _count;
        DateTime _lastSample = DateTime.MinValue;

        public TrendLog() : this(DefaultCapacity) { }

        public TrendLog(int capacity)
        {
            if (capacity < 2) capacity = 2;
            _buf = new TrendSample[capacity];
        }

        public int Capacity { get { return _buf.Length; } }
        public int Count { get { return _count; } }

        public TrendSample this[int i]
        {
            get
            {
                if (i < 0 || i >= _count) throw new ArgumentOutOfRangeException("i");
                return _buf[(_start + i) % _buf.Length];
            }
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
            _lastSample = DateTime.MinValue;
        }

        public void Add(TrendSample s)
        {
            if (_count < _buf.Length)
            {
                _buf[(_start + _count) % _buf.Length] = s;
                _count++;
            }
            else
            {
                _buf[_start] = s;
                _start = (_start + 1) % _buf.Length;
            }
            _lastSample = s.Time;
        }

        /// <summary>Adds at most one sample per second; returns true when it took one.</summary>
        public bool Offer(TrendSample s)
        {
            if (_lastSample != DateTime.MinValue && (s.Time - _lastSample).TotalMilliseconds < 950)
                return false;
            Add(s);
            return true;
        }

        public List<TrendSample> Copy()
        {
            var list = new List<TrendSample>(_count);
            for (int i = 0; i < _count; i++) list.Add(this[i]);
            return list;
        }

        public static double Value(TrendSample s, TrendChannel ch)
        {
            switch (ch)
            {
                case TrendChannel.Rpm: return s.Rpm;
                case TrendChannel.Coolant: return s.CoolantC;
                case TrendChannel.Oil: return s.OilKpa;
                case TrendChannel.Battery: return s.BatteryV;
                case TrendChannel.Fuel: return s.FuelLph;
                default: return double.NaN;
            }
        }

        public static string Label(TrendChannel ch)
        {
            switch (ch)
            {
                case TrendChannel.Rpm: return "RPM";
                case TrendChannel.Coolant: return "Coolant °C";
                case TrendChannel.Oil: return "Oil kPa";
                case TrendChannel.Battery: return "Battery V";
                case TrendChannel.Fuel: return "Fuel L/h";
                default: return "?";
            }
        }

        /// <summary>Min/max of a channel over the samples that actually have a value.</summary>
        public static bool Range(List<TrendSample> samples, TrendChannel ch, out double min, out double max)
        {
            min = 0; max = 0;
            bool any = false;
            foreach (TrendSample s in samples)
            {
                double v = Value(s, ch);
                if (!TrendSample.Has(v)) continue;
                if (!any) { min = max = v; any = true; continue; }
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return any;
        }

        public static string Csv(List<TrendSample> samples)
        {
            var sb = new StringBuilder();
            sb.AppendLine("time,rpm,coolant_c,oil_kpa,battery_v,fuel_lph,red,amber,active_codes");
            foreach (TrendSample s in samples)
                sb.AppendLine(string.Join(",", new[]
                {
                    s.Time.ToString("o"),
                    Num(s.Rpm, "0"),
                    Num(s.CoolantC, "0"),
                    Num(s.OilKpa, "0"),
                    Num(s.BatteryV, "0.00"),
                    Num(s.FuelLph, "0.00"),
                    s.Red ? "1" : "0",
                    s.Amber ? "1" : "0",
                    s.ActiveCodes.ToString(CultureInfo.InvariantCulture)
                }));
            return sb.ToString();
        }

        static string Num(double v, string fmt)
        {
            return TrendSample.Has(v) ? v.ToString(fmt, CultureInfo.InvariantCulture) : "";
        }
    }
}
