// TradingConsole.Wpf/Services/Analysis/ThesisSynthesizer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TradingConsole.Wpf.Services.Analysis;
using TradingConsole.Wpf.ViewModels;

namespace TradingConsole.Wpf.Services
{
    /// <summary>
    /// Synthesizes a final trade signal and market thesis from various raw signals.
    /// This is the highest level of the analysis engine, responsible for decision-making.
    /// </summary>
    public class ThesisSynthesizer
    {
        private readonly SettingsViewModel _settingsViewModel;
        private readonly SignalLoggerService _signalLoggerService;
        private readonly NotificationService _notificationService;
        private readonly AnalysisStateManager _stateManager;

        public ThesisSynthesizer(SettingsViewModel settingsViewModel, SignalLoggerService signalLoggerService, NotificationService notificationService, AnalysisStateManager stateManager)
        {
            _settingsViewModel = settingsViewModel;
            _signalLoggerService = signalLoggerService;
            _notificationService = notificationService;
            _stateManager = stateManager;
        }

        public void SynthesizeTradeSignal(AnalysisResult result)
        {
            if (result.InstrumentGroup != "Indices") return;

            MarketThesis thesis = UpdateIntradayThesis(result);
            result.MarketThesis = thesis;

            var (bullDrivers, bearDrivers, conviction) = CalculateConvictionScore(result, thesis);
            result.BullishDrivers = bullDrivers;
            result.BearishDrivers = bearDrivers;
            result.ConvictionScore = conviction;

            string playbook = "Neutral / Observe";
            if (conviction >= 7) playbook = "Strong Bullish Conviction";
            else if (conviction >= 3) playbook = "Moderate Bullish Conviction";
            else if (conviction <= -7) playbook = "Strong Bearish Conviction";
            else if (conviction <= -3) playbook = "Moderate Bearish Conviction";

            string newPrimarySignal = "Neutral";
            if (conviction >= 3) newPrimarySignal = "Bullish";
            else if (conviction <= -3) newPrimarySignal = "Bearish";

            string oldPrimarySignal = result.PrimarySignal;
            result.PrimarySignal = newPrimarySignal;
            result.FinalTradeSignal = playbook;
            result.MarketNarrative = GenerateMarketNarrative(result);

            if (result.PrimarySignal != oldPrimarySignal)
            {
                if (_stateManager.LastSignalTime.TryGetValue(result.SecurityId, out var lastTime) && (DateTime.UtcNow - lastTime).TotalSeconds < 60)
                {
                    return;
                }
                _stateManager.LastSignalTime[result.SecurityId] = DateTime.UtcNow;

                _signalLoggerService.LogSignal(result);
                Task.Run(() => _notificationService.SendTelegramSignalAsync(result, oldPrimarySignal));
            }
        }

        private MarketThesis UpdateIntradayThesis(AnalysisResult result)
        {
            DominantPlayer player = DetermineDominantPlayer(result);
            result.DominantPlayer = player;

            if (result.MarketStructure == "Trending Up")
            {
                if (player == DominantPlayer.Buyers) return MarketThesis.Bullish_Trend;
                if (player == DominantPlayer.Sellers) return MarketThesis.Bullish_Rotation;
                return MarketThesis.Bullish_Trend;
            }

            if (result.MarketStructure == "Trending Down")
            {
                if (player == DominantPlayer.Sellers) return MarketThesis.Bearish_Trend;
                if (player == DominantPlayer.Buyers) return MarketThesis.Bearish_Rotation;
                return MarketThesis.Bearish_Trend;
            }

            return MarketThesis.Balancing;
        }

        private DominantPlayer DetermineDominantPlayer(AnalysisResult result)
        {
            int buyerEvidence = 0;
            int sellerEvidence = 0;

            if (result.PriceVsVwapSignal == "Above VWAP") buyerEvidence++;
            if (result.PriceVsVwapSignal == "Below VWAP") sellerEvidence++;
            if (result.EmaSignal5Min == "Bullish Cross") buyerEvidence++;
            if (result.EmaSignal5Min == "Bearish Cross") sellerEvidence++;
            if (result.OiSignal == "Long Buildup") buyerEvidence++;
            if (result.OiSignal == "Short Buildup") sellerEvidence++;

            if (buyerEvidence > sellerEvidence) return DominantPlayer.Buyers;
            if (sellerEvidence > buyerEvidence) return DominantPlayer.Sellers;
            return DominantPlayer.Balance;
        }

        private (List<string> BullishDrivers, List<string> BearishDrivers, int Score) CalculateConvictionScore(AnalysisResult r, MarketThesis thesis)
        {
            var bullDrivers = _settingsViewModel.Strategy.TrendContinuation_Bullish.Where(d => d.IsEnabled).ToList();
            var bearDrivers = _settingsViewModel.Strategy.TrendContinuation_Bearish.Where(d => d.IsEnabled).ToList();

            int score = 0;
            var triggeredBullDrivers = new List<string>();
            var triggeredBearDrivers = new List<string>();

            foreach (var driver in bullDrivers)
            {
                if (CheckDriverCondition(r, driver.Name))
                {
                    score += driver.Weight;
                    triggeredBullDrivers.Add($"{driver.Name} (+{driver.Weight})");
                }
            }

            foreach (var driver in bearDrivers)
            {
                if (CheckDriverCondition(r, driver.Name))
                {
                    score -= driver.Weight;
                    triggeredBearDrivers.Add($"{driver.Name} (-{driver.Weight})");
                }
            }
            return (triggeredBullDrivers, triggeredBearDrivers, score);
        }

        private bool CheckDriverCondition(AnalysisResult r, string driverName)
        {
            switch (driverName)
            {
                case "Confluence Momentum (Bullish)":
                    {
                        bool isPattern = r.CandleSignal5Min.Contains("Bullish");
                        bool isAtSupport = r.CandleSignal5Min.Contains("Support");
                        bool isVolumeConfirmed = r.VolumeSignal == "Volume Burst";
                        return isPattern && isAtSupport && isVolumeConfirmed;
                    }
                case "Confluence Momentum (Bearish)":
                    {
                        bool isPattern = r.CandleSignal5Min.Contains("Bearish");
                        bool isAtResistance = r.CandleSignal5Min.Contains("Resistance");
                        bool isVolumeConfirmed = r.VolumeSignal == "Volume Burst";
                        return isPattern && isAtResistance && isVolumeConfirmed;
                    }
                case "Option Breakout Setup (Bullish)":
                    {
                        bool wasInSqueeze = _stateManager.IsInVolatilitySqueeze.GetValueOrDefault(r.SecurityId);
                        bool isBreakoutTrigger = r.CandleSignal5Min.Contains("Bullish") && r.VolumeSignal == "Volume Burst";
                        if (wasInSqueeze && isBreakoutTrigger)
                        {
                            _stateManager.IsInVolatilitySqueeze[r.SecurityId] = false;
                            return true;
                        }
                        return false;
                    }
                case "Option Breakout Setup (Bearish)":
                    {
                        bool wasInSqueeze = _stateManager.IsInVolatilitySqueeze.GetValueOrDefault(r.SecurityId);
                        bool isBreakoutTrigger = r.CandleSignal5Min.Contains("Bearish") && r.VolumeSignal == "Volume Burst";
                        if (wasInSqueeze && isBreakoutTrigger)
                        {
                            _stateManager.IsInVolatilitySqueeze[r.SecurityId] = false;
                            return true;
                        }
                        return false;
                    }

                case "Price above VWAP": return r.PriceVsVwapSignal == "Above VWAP";
                case "Price below VWAP": return r.PriceVsVwapSignal == "Below VWAP";
                case "5m VWAP EMA confirms bullish trend": return r.VwapEmaSignal5Min == "Bullish Cross";
                case "5m VWAP EMA confirms bearish trend": return r.VwapEmaSignal5Min == "Bearish Cross";
                case "OI confirms new longs": return r.OiSignal == "Long Buildup";
                case "OI confirms new shorts": return r.OiSignal == "Short Buildup";
                default: return false;
            }
        }

        private string GenerateMarketNarrative(AnalysisResult r)
        {
            return $"Thesis: {r.MarketThesis}. Dominant Player: {r.DominantPlayer}. Open: {r.OpenTypeSignal}. vs VWAP: {r.PriceVsVwapSignal}.";
        }
    }
}
