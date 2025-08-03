// In TradingConsole.Wpf/Services/Analysis/SignalGenerationService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using TradingConsole.Core.Models;
using TradingConsole.Wpf.Services.Analysis;
using TradingConsole.Wpf.ViewModels;

namespace TradingConsole.Wpf.Services
{
    /// <summary>
    /// Responsible for generating all individual, raw signals from market data.
    /// This includes price action, volume, market profile, volatility, and momentum signals.
    /// </summary>
    public class SignalGenerationService
    {
        private readonly AnalysisStateManager _stateManager;
        private readonly SettingsViewModel _settingsViewModel;
        private readonly HistoricalIvService _historicalIvService;

        public SignalGenerationService(AnalysisStateManager stateManager, SettingsViewModel settingsViewModel, HistoricalIvService historicalIvService)
        {
            _stateManager = stateManager;
            _settingsViewModel = settingsViewModel;
            _historicalIvService = historicalIvService;
        }

        public void GenerateAllSignals(DashboardInstrument instrument, DashboardInstrument instrumentForAnalysis, AnalysisResult result)
        {
            var tickState = _stateManager.TickAnalysisState[instrumentForAnalysis.SecurityId];
            tickState.cumulativePriceVolume += instrumentForAnalysis.AvgTradePrice * instrumentForAnalysis.LastTradedQuantity;
            tickState.cumulativeVolume += instrumentForAnalysis.LastTradedQuantity;
            result.Vwap = (tickState.cumulativeVolume > 0) ? tickState.cumulativePriceVolume / tickState.cumulativeVolume : 0;
            _stateManager.TickAnalysisState[instrumentForAnalysis.SecurityId] = tickState;

            var (priceVsVwap, priceVsClose, dayRange) = CalculatePriceActionSignals(instrument, result.Vwap);
            result.PriceVsVwapSignal = priceVsVwap;
            result.PriceVsCloseSignal = priceVsClose;
            result.DayRangeSignal = dayRange;

            var oneMinCandles = _stateManager.GetCandles(instrumentForAnalysis.SecurityId, TimeSpan.FromMinutes(1));
            if (oneMinCandles != null && oneMinCandles.Any())
            {
                var (volSignal, currentVol, avgVol) = CalculateVolumeSignal(oneMinCandles);
                result.VolumeSignal = volSignal;
                result.CurrentVolume = currentVol;
                result.AvgVolume = avgVol;
                result.OiSignal = CalculateOiSignal(oneMinCandles);
                result.OpenTypeSignal = AnalyzeOpenType(instrument, oneMinCandles);

                var (vwapBandSignal, upperBand, lowerBand) = CalculateVwapBandSignal(instrument.LTP, oneMinCandles);
                result.VwapBandSignal = vwapBandSignal;
                result.VwapUpperBand = upperBand;
                result.VwapLowerBand = lowerBand;
                result.AnchoredVwap = CalculateAnchoredVwap(oneMinCandles);
            }

            var fiveMinCandles = _stateManager.GetCandles(instrumentForAnalysis.SecurityId, TimeSpan.FromMinutes(5));
            if (oneMinCandles != null) result.CandleSignal1Min = RecognizeCandlestickPattern(oneMinCandles, result);
            if (fiveMinCandles != null) result.CandleSignal5Min = RecognizeCandlestickPattern(fiveMinCandles, result);

            if (_stateManager.MarketProfiles.TryGetValue(instrument.SecurityId, out var liveProfile))
            {
                result.InitialBalanceSignal = GetInitialBalanceSignal(instrument.LTP, liveProfile, instrument.SecurityId);
                result.InitialBalanceHigh = liveProfile.InitialBalanceHigh;
                result.InitialBalanceLow = liveProfile.InitialBalanceLow;
                result.DevelopingPoc = liveProfile.DevelopingTpoLevels.PointOfControl;
                result.DevelopingVah = liveProfile.DevelopingTpoLevels.ValueAreaHigh;
                result.DevelopingVal = liveProfile.DevelopingTpoLevels.ValueAreaLow;
                result.DevelopingVpoc = liveProfile.DevelopingVolumeProfile.VolumePoc;
                RunMarketProfileAnalysis(instrument, liveProfile, result);
            }
            var yesterdayProfile = _stateManager.HistoricalMarketProfiles.GetValueOrDefault(instrument.SecurityId)?.FirstOrDefault(p => p.Date.Date < DateTime.Today);
            result.YesterdayProfileSignal = AnalyzePriceRelativeToYesterdayProfile(instrument.LTP, yesterdayProfile);

            if (instrument.InstrumentType == "INDEX")
            {
                result.InstitutionalIntent = RunTier1InstitutionalIntentAnalysis(instrument);
            }
        }

        public void UpdateIvMetrics(DashboardInstrument instrument, decimal underlyingPrice)
        {
            if (!instrument.InstrumentType.StartsWith("OPT") || instrument.ImpliedVolatility <= 0) return;

            var ivKey = GetHistoricalIvKey(instrument, underlyingPrice);
            if (string.IsNullOrEmpty(ivKey)) return;

            if (!_stateManager.IntradayIvStates.ContainsKey(ivKey))
            {
                _stateManager.IntradayIvStates[ivKey] = new IntradayIvState();
            }
            var ivState = _stateManager.IntradayIvStates[ivKey];

            ivState.DayHighIv = Math.Max(ivState.DayHighIv, instrument.ImpliedVolatility);
            ivState.DayLowIv = Math.Min(ivState.DayLowIv, instrument.ImpliedVolatility);

            _historicalIvService.RecordDailyIv(ivKey, ivState.DayHighIv, ivState.DayLowIv);

            var (ivRank, ivPercentile) = CalculateIvRankAndPercentile(instrument.ImpliedVolatility, ivKey, ivState);
            var result = _stateManager.GetResult(instrument.SecurityId);
            result.IvRank = ivRank;
            result.IvPercentile = ivPercentile;
        }

        #region Signal Calculation Logic

        private (string, long, long) CalculateVolumeSignal(List<Candle> candles)
        {
            if (!candles.Any()) return ("N/A", 0, 0);
            long currentCandleVolume = candles.Last().Volume;
            if (candles.Count < 2) return ("Building History...", currentCandleVolume, 0);
            var historyCandles = candles.Take(candles.Count - 1).ToList();
            if (historyCandles.Count > _settingsViewModel.VolumeHistoryLength)
            {
                historyCandles = historyCandles.Skip(historyCandles.Count - _settingsViewModel.VolumeHistoryLength).ToList();
            }
            if (!historyCandles.Any()) return ("Building History...", currentCandleVolume, 0);
            double averageVolume = historyCandles.Average(c => (double)c.Volume);
            if (averageVolume > 0 && currentCandleVolume > (averageVolume * _settingsViewModel.VolumeBurstMultiplier))
            {
                return ("Volume Burst", currentCandleVolume, (long)averageVolume);
            }
            return ("Neutral", currentCandleVolume, (long)averageVolume);
        }

        private string CalculateOiSignal(List<Candle> candles)
        {
            if (candles.Count < 2) return "Building History...";
            var currentCandle = candles.Last();
            var previousCandle = candles[candles.Count - 2];
            if (previousCandle.OpenInterest == 0 || currentCandle.OpenInterest == 0) return "Building History...";
            bool isPriceUp = currentCandle.Close > previousCandle.Close;
            bool isPriceDown = currentCandle.Close < previousCandle.Close;
            bool isOiUp = currentCandle.OpenInterest > previousCandle.OpenInterest;
            bool isOiDown = currentCandle.OpenInterest < previousCandle.OpenInterest;
            if (isPriceUp && isOiUp) return "Long Buildup";
            if (isPriceUp && isOiDown) return "Short Covering";
            if (isPriceDown && isOiUp) return "Short Buildup";
            if (isPriceDown && isOiDown) return "Long Unwinding";
            return "Neutral";
        }

        private (string priceVsVwap, string priceVsClose, string dayRange) CalculatePriceActionSignals(DashboardInstrument instrument, decimal vwap)
        {
            string priceVsVwap = (vwap > 0) ? (instrument.LTP > vwap ? "Above VWAP" : "Below VWAP") : "Neutral";
            string priceVsClose = (instrument.Close > 0) ? (instrument.LTP > instrument.Close ? "Above Close" : "Below Close") : "Neutral";
            string dayRange = "Mid-Range";
            decimal range = instrument.High - instrument.Low;
            if (range > 0)
            {
                decimal position = (instrument.LTP - instrument.Low) / range;
                if (position > 0.8m) dayRange = "Near High";
                else if (position < 0.2m) dayRange = "Near Low";
            }
            return (priceVsVwap, priceVsClose, dayRange);
        }

        private string RecognizeCandlestickPattern(List<Candle> candles, AnalysisResult analysisResult)
        {
            if (candles.Count < 3) return "N/A";

            string pattern = IdentifyCandlePattern(candles);
            if (pattern == "N/A") return "N/A";

            string context = GetPatternContext(analysisResult);
            string volumeInfo = GetVolumeConfirmation(candles.Last(), candles[^2]);

            return $"{pattern}{context}{volumeInfo}";
        }

        // --- UPGRADED METHOD: Now includes more patterns and tolerance ---
        private string IdentifyCandlePattern(List<Candle> candles)
        {
            var c1 = candles.Last();    // Current, most recent candle
            var c2 = candles[^2]; // Previous candle
            var c3 = candles[^3]; // Two candles ago

            decimal body1 = Math.Abs(c1.Open - c1.Close);
            decimal range1 = c1.High - c1.Low;
            if (range1 == 0) return "N/A";

            decimal upperShadow1 = c1.High - Math.Max(c1.Open, c1.Close);
            decimal lowerShadow1 = Math.Min(c1.Open, c1.Close) - c1.Low;

            // Single Candle Patterns (with tolerance)
            if (body1 / range1 < 0.15m) return "Neutral Doji";
            if (lowerShadow1 > body1 * 1.8m && upperShadow1 < body1 * 0.9m) return c1.Close > c1.Open ? "Bullish Hammer" : "Bearish Hanging Man";
            if (upperShadow1 > body1 * 1.8m && lowerShadow1 < body1 * 0.9m) return c1.Close > c1.Open ? "Bullish Inv Hammer" : "Bearish Shooting Star";
            if (body1 / range1 > 0.85m) return c1.Close > c1.Open ? "Bullish Marubozu" : "Bearish Marubozu";

            // Double Candle Patterns (with tolerance)
            if (c1.Close > c2.Open && c1.Open < c2.Close && c1.Close > c1.Open && c2.Close < c2.Open) return "Bullish Engulfing";
            if (c1.Open > c2.Close && c1.Close < c2.Open && c1.Close < c1.Open && c2.Close > c2.Open) return "Bearish Engulfing";

            // --- NEW PATTERNS ---
            decimal c2BodyMidpoint = c2.Open + (c2.Close - c2.Open) / 2;
            if (c2.Close < c2.Open && c1.Open < c2.Low && c1.Close > c2BodyMidpoint && c1.Close < c2.Open) return "Bullish Piercing Line";
            if (c2.Close > c2.Open && c1.Open > c2.High && c1.Close < c2BodyMidpoint && c1.Close > c2.Open) return "Bearish Dark Cloud Cover";

            // Triple Candle Patterns
            bool isMorningStar = c3.Close < c3.Open && Math.Max(c2.Open, c2.Close) < c3.Close && c1.Close > c1.Open && c1.Close > (c3.Open + c3.Close) / 2;
            if (isMorningStar) return "Bullish Morning Star";

            bool isEveningStar = c3.Close > c3.Open && Math.Min(c2.Open, c2.Close) > c3.Close && c1.Close < c1.Open && c1.Close < (c3.Open + c3.Close) / 2;
            if (isEveningStar) return "Bearish Evening Star";

            // --- NEW PATTERNS ---
            bool isThreeWhiteSoldiers = c3.Close > c3.Open && c2.Close > c2.Open && c1.Close > c1.Open &&
                                        c2.Open > c3.Open && c2.Close > c3.Close &&
                                        c1.Open > c2.Open && c1.Close > c2.Close;
            if (isThreeWhiteSoldiers) return "Three White Soldiers";

            bool isThreeBlackCrows = c3.Close < c3.Open && c2.Close < c2.Open && c1.Close < c1.Open &&
                                     c2.Open < c3.Open && c2.Close < c3.Close &&
                                     c1.Open < c2.Open && c1.Close < c2.Close;
            if (isThreeBlackCrows) return "Three Black Crows";

            return "N/A";
        }

        private string GetPatternContext(AnalysisResult analysisResult)
        {
            if (analysisResult.DayRangeSignal == "Near Low" || analysisResult.VwapBandSignal == "At Lower Band" || analysisResult.MarketProfileSignal.Contains("VAL"))
            {
                return " at Key Support";
            }
            if (analysisResult.DayRangeSignal == "Near High" || analysisResult.VwapBandSignal == "At Upper Band" || analysisResult.MarketProfileSignal.Contains("VAH"))
            {
                return " at Key Resistance";
            }
            return string.Empty;
        }

        private string GetVolumeConfirmation(Candle current, Candle previous)
        {
            if (previous.Volume > 0)
            {
                decimal volChange = ((decimal)current.Volume - previous.Volume) / previous.Volume;
                if (volChange > 0.5m)
                {
                    return " (+Vol)";
                }
            }
            return "";
        }

        private string AnalyzeOpenType(DashboardInstrument instrument, List<Candle> oneMinCandles)
        {
            if (oneMinCandles.Count < 3) return "Analyzing Open...";
            var firstCandle = oneMinCandles[0];
            bool isFirstCandleStrong = Math.Abs(firstCandle.Close - firstCandle.Open) > (firstCandle.High - firstCandle.Low) * 0.7m;
            if (isFirstCandleStrong && firstCandle.Close > firstCandle.Open) return "Open-Drive (Bullish)";
            if (isFirstCandleStrong && firstCandle.Close < firstCandle.Open) return "Open-Drive (Bearish)";
            return "Open-Auction (Rotational)";
        }

        private (string, decimal, decimal) CalculateVwapBandSignal(decimal ltp, List<Candle> candles)
        {
            if (candles.Count < 2) return ("N/A", 0, 0);
            var vwap = candles.Last().Vwap;
            if (vwap == 0) return ("N/A", 0, 0);
            decimal sumOfSquares = candles.Sum(c => (c.Close - vwap) * (c.Close - vwap));
            decimal stdDev = (decimal)Math.Sqrt((double)(sumOfSquares / candles.Count));
            var upperBand = vwap + (stdDev * _settingsViewModel.VwapUpperBandMultiplier);
            var lowerBand = vwap - (stdDev * _settingsViewModel.VwapLowerBandMultiplier);
            string signal = "Inside Bands";
            if (ltp > upperBand) signal = "Above Upper Band";
            else if (ltp < lowerBand) signal = "Below Lower Band";
            return (signal, upperBand, lowerBand);
        }

        private decimal CalculateAnchoredVwap(List<Candle> candles)
        {
            if (candles == null || !candles.Any()) return 0;
            decimal cumulativePriceVolume = candles.Sum(c => c.Close * c.Volume);
            long cumulativeVolume = candles.Sum(c => c.Volume);
            return (cumulativeVolume > 0) ? cumulativePriceVolume / cumulativeVolume : 0;
        }

        private string GetInitialBalanceSignal(decimal ltp, MarketProfile profile, string securityId)
        {
            if (!profile.IsInitialBalanceSet) return "IB Forming";
            if (!_stateManager.InitialBalanceState.ContainsKey(securityId)) _stateManager.InitialBalanceState[securityId] = (false, false);
            var (isBreakout, isBreakdown) = _stateManager.InitialBalanceState[securityId];
            if (ltp > profile.InitialBalanceHigh && !isBreakout)
            {
                _stateManager.InitialBalanceState[securityId] = (true, false);
                return "IB Breakout";
            }
            if (ltp < profile.InitialBalanceLow && !isBreakdown)
            {
                _stateManager.InitialBalanceState[securityId] = (false, true);
                return "IB Breakdown";
            }
            if (ltp > profile.InitialBalanceHigh && isBreakout) return "IB Extension Up";
            if (ltp < profile.InitialBalanceLow && isBreakdown) return "IB Extension Down";
            return "Inside IB";
        }

        private string AnalyzePriceRelativeToYesterdayProfile(decimal ltp, MarketProfileData? previousDay)
        {
            if (previousDay == null || ltp == 0) return "N/A";
            if (ltp > previousDay.TpoLevelsInfo.ValueAreaHigh) return "Trading Above Y-VAH";
            if (ltp < previousDay.TpoLevelsInfo.ValueAreaLow) return "Trading Below Y-VAL";
            return "Trading Inside Y-Value";
        }

        private void RunMarketProfileAnalysis(DashboardInstrument instrument, MarketProfile currentProfile, AnalysisResult result)
        {
            var previousDayProfile = _stateManager.HistoricalMarketProfiles.GetValueOrDefault(instrument.SecurityId)?.FirstOrDefault(p => p.Date.Date < DateTime.Today.Date);
            if (previousDayProfile == null)
            {
                result.MarketProfileSignal = "Awaiting Previous Day Data";
                return;
            }
            var prevVAH = previousDayProfile.TpoLevelsInfo.ValueAreaHigh;
            var currentVAL = currentProfile.DevelopingTpoLevels.ValueAreaLow;
            if (currentVAL > prevVAH) { result.MarketProfileSignal = "True Acceptance Above Y-VAH"; return; }
            result.MarketProfileSignal = "Trading Inside Y-Value";
        }

        public void UpdateMarketProfile(MarketProfile profile, Candle priceCandle, Candle volumeCandle)
        {
            profile.UpdateInitialBalance(priceCandle);
            var tpoPeriod = profile.GetTpoPeriod(priceCandle.Timestamp);
            for (decimal price = priceCandle.Low; price <= priceCandle.High; price += profile.TickSize)
            {
                var quantizedPrice = profile.QuantizePrice(price);
                if (!profile.TpoLevels.ContainsKey(quantizedPrice)) profile.TpoLevels[quantizedPrice] = new List<char>();
                if (!profile.TpoLevels[quantizedPrice].Contains(tpoPeriod)) profile.TpoLevels[quantizedPrice].Add(tpoPeriod);
            }
        }

        private string RunTier1InstitutionalIntentAnalysis(DashboardInstrument spotIndex)
        {
            return "Neutral";
        }

        public void RunDailyBiasAnalysis(DashboardInstrument instrument, AnalysisResult result)
        {
            var profiles = _stateManager.HistoricalMarketProfiles.GetValueOrDefault(instrument.SecurityId);
            if (profiles == null || profiles.Count < 3)
            {
                result.DailyBias = "Insufficient History";
                result.MarketStructure = "Unknown";
                return;
            }

            var sortedProfiles = profiles.OrderByDescending(p => p.Date).ToList();
            var p1 = sortedProfiles[0];
            var p2 = sortedProfiles[1];
            var p3 = sortedProfiles[2];

            bool isP1Higher = p1.TpoLevelsInfo.ValueAreaLow > p2.TpoLevelsInfo.ValueAreaHigh;
            bool isP2Higher = p2.TpoLevelsInfo.ValueAreaLow > p3.TpoLevelsInfo.ValueAreaHigh;
            bool isP1OverlapHigher = p1.TpoLevelsInfo.PointOfControl > p2.TpoLevelsInfo.ValueAreaHigh;
            bool isP2OverlapHigher = p2.TpoLevelsInfo.PointOfControl > p3.TpoLevelsInfo.ValueAreaHigh;

            if ((isP1Higher && isP2Higher) || (isP1OverlapHigher && isP2OverlapHigher))
            {
                result.MarketStructure = "Trending Up";
                result.DailyBias = "Bullish";
                return;
            }

            bool isP1Lower = p1.TpoLevelsInfo.ValueAreaHigh < p2.TpoLevelsInfo.ValueAreaLow;
            bool isP2Lower = p2.TpoLevelsInfo.ValueAreaHigh < p3.TpoLevelsInfo.ValueAreaLow;
            bool isP1OverlapLower = p1.TpoLevelsInfo.PointOfControl < p2.TpoLevelsInfo.ValueAreaLow;
            bool isP2OverlapLower = p2.TpoLevelsInfo.PointOfControl < p3.TpoLevelsInfo.ValueAreaLow;

            if ((isP1Lower && isP2Lower) || (isP1OverlapLower && isP2OverlapLower))
            {
                result.MarketStructure = "Trending Down";
                result.DailyBias = "Bearish";
                return;
            }

            result.MarketStructure = "Balancing";
            result.DailyBias = "Neutral / Rotational";
        }

        public decimal GetTickSize(DashboardInstrument? instrument) => (instrument?.InstrumentType == "INDEX") ? 1.0m : 0.05m;

        private string GetHistoricalIvKey(DashboardInstrument instrument, decimal underlyingPrice)
        {
            return $"{instrument.UnderlyingSymbol}_ATM_CE";
        }

        private (decimal ivRank, decimal ivPercentile) CalculateIvRankAndPercentile(decimal currentIv, string key, IntradayIvState ivState)
        {
            var (histHigh, histLow) = _historicalIvService.Get90DayIvRange(key);
            if (histHigh == 0 || histLow == 0) return (0m, 0m);

            decimal histRange = histHigh - histLow;
            decimal ivRank = (histRange > 0) ? ((currentIv - histLow) / histRange) * 100 : 0m;

            return (Math.Max(0, Math.Min(100, Math.Round(ivRank, 2))), 0m);
        }

        #endregion
    }
}
