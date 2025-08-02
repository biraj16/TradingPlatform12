// In TradingConsole.Wpf/Services/AnalysisDataModels.cs
using System;
using System.Collections.Generic;
using System.Linq;
using TradingConsole.Core.Models;

namespace TradingConsole.Wpf.Services
{
    #region Core Data Models

    /// <summary>
    /// Represents a single candlestick with price, volume, and open interest data.
    /// </summary>
    public class Candle
    {
        public DateTime Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
        public long OpenInterest { get; set; }
        public decimal Vwap { get; set; }
        public decimal AnchoredVwap { get; set; }
        internal decimal CumulativePriceVolume { get; set; } = 0;
        internal long CumulativeVolume { get; set; } = 0;

        public override string ToString()
        {
            return $"T: {Timestamp:HH:mm:ss}, O: {Open}, H: {High}, L: {Low}, C: {Close}, V: {Volume}";
        }
    }

    #endregion

    #region Indicator State Models

    /// <summary>
    /// Holds the running state for Exponential Moving Average (EMA) calculations.
    /// </summary>
    public class EmaState
    {
        public decimal CurrentShortEma { get; set; }
        public decimal CurrentLongEma { get; set; }
    }

    /// <summary>
    /// Holds the running state for Relative Strength Index (RSI) calculations.
    /// </summary>
    public class RsiState
    {
        public decimal AvgGain { get; set; }
        public decimal AvgLoss { get; set; }
        public List<decimal> RsiValues { get; } = new List<decimal>();
    }

    /// <summary>
    /// Holds the running state for Average True Range (ATR) calculations.
    /// </summary>
    public class AtrState
    {
        public decimal CurrentAtr { get; set; }
        public List<decimal> AtrValues { get; } = new List<decimal>();
    }

    /// <summary>
    /// Holds the running state for On-Balance Volume (OBV) calculations.
    /// </summary>
    public class ObvState
    {
        public decimal CurrentObv { get; set; }
        public List<decimal> ObvValues { get; } = new List<decimal>();
        public decimal CurrentMovingAverage { get; set; }
    }

    #endregion

    #region Market Context Models

    /// <summary>
    /// Holds the state for analyzing relative strength between spot and futures to determine institutional intent.
    /// </summary>
    public class RelativeStrengthState
    {
        public List<decimal> BasisDeltaHistory { get; } = new List<decimal>();
        public List<decimal> OptionsDeltaHistory { get; } = new List<decimal>();
        public string InstitutionalIntentSignal { get; set; } = "Neutral";
    }

    /// <summary>
    /// Holds the state for analyzing Implied Volatility (IV) skew between calls and puts.
    /// </summary>
    public class IvSkewState
    {
        public List<decimal> AtmCallIvHistory { get; } = new List<decimal>();
        public List<decimal> AtmPutIvHistory { get; } = new List<decimal>();
        public List<decimal> OtmCallIvHistory { get; } = new List<decimal>();
        public List<decimal> OtmPutIvHistory { get; } = new List<decimal>();
        public List<decimal> PutSkewSlopeHistory { get; } = new List<decimal>();
        public List<decimal> CallSkewSlopeHistory { get; } = new List<decimal>();
    }

    /// <summary>
    /// Holds the state for intraday Implied Volatility (IV) analysis, including daily range and percentile history.
    /// </summary>
    public class IntradayIvState
    {
        public decimal DayHighIv { get; set; } = 0;
        public decimal DayLowIv { get; set; } = decimal.MaxValue;
        public List<decimal> IvPercentileHistory { get; } = new List<decimal>();

        internal enum PriceZone { Inside, Above, Below }
        internal class CustomLevelState
        {
            public int BreakoutCount { get; set; }
            public int BreakdownCount { get; set; }
            public PriceZone LastZone { get; set; } = PriceZone.Inside;
        }
    }

    #endregion

    #region Market Profile

    /// <summary>
    /// Represents and calculates the market profile for a trading session, including TPO and Volume profiles.
    /// </summary>
    public class MarketProfile
    {
        public SortedDictionary<decimal, List<char>> TpoLevels { get; } = new SortedDictionary<decimal, List<char>>();
        public SortedDictionary<decimal, long> VolumeLevels { get; } = new SortedDictionary<decimal, long>();
        public TpoInfo TpoLevelsInfo { get; set; } = new TpoInfo();
        public VolumeProfileInfo VolumeProfileInfo { get; set; } = new VolumeProfileInfo();
        public decimal TickSize { get; }
        private readonly DateTime _sessionStartTime;
        private readonly DateTime _initialBalanceEndTime;

        public string LastMarketSignal { get; set; } = string.Empty;
        public DateTime Date { get; set; }

        public decimal InitialBalanceHigh { get; private set; }
        public decimal InitialBalanceLow { get; private set; }
        public bool IsInitialBalanceSet { get; private set; }

        public TpoInfo DevelopingTpoLevels { get; set; } = new TpoInfo();
        public VolumeProfileInfo DevelopingVolumeProfile { get; set; } = new VolumeProfileInfo();


        public MarketProfile(decimal tickSize, DateTime sessionStartTime)
        {
            TickSize = tickSize;
            _sessionStartTime = sessionStartTime;
            _initialBalanceEndTime = _sessionStartTime.AddMinutes(30);
            Date = sessionStartTime.Date;
            InitialBalanceLow = decimal.MaxValue;
        }

        public char GetTpoPeriod(DateTime timestamp)
        {
            var elapsed = timestamp - _sessionStartTime;
            int periodIndex = (int)(elapsed.TotalMinutes / 30);
            return (char)('A' + periodIndex);
        }

        public decimal QuantizePrice(decimal price)
        {
            return Math.Round(price / TickSize) * TickSize;
        }

        public void UpdateInitialBalance(Candle candle)
        {
            if (candle.Timestamp <= _initialBalanceEndTime)
            {
                InitialBalanceHigh = Math.Max(InitialBalanceHigh, candle.High);
                InitialBalanceLow = Math.Min(InitialBalanceLow, candle.Low);
            }
            else if (!IsInitialBalanceSet)
            {
                IsInitialBalanceSet = true;
            }
        }

        public MarketProfileData ToMarketProfileData()
        {
            var tpoCounts = this.TpoLevels.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);

            return new MarketProfileData
            {
                Date = this.Date,
                TpoLevelsInfo = this.DevelopingTpoLevels,
                VolumeProfileInfo = this.DevelopingVolumeProfile,
                TpoCounts = tpoCounts,
                VolumeLevels = new Dictionary<decimal, long>(this.VolumeLevels)
            };
        }
    }

    #endregion
}
