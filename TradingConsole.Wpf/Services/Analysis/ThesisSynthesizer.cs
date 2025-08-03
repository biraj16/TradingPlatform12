// TradingConsole.Wpf/Services/Analysis/ThesisSynthesizer.cs
// --- MODIFIED: Complete overhaul of conviction scoring to use confluence and handle market phases/chop. ---
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TradingConsole.Wpf.Services.Analysis;
using TradingConsole.Wpf.ViewModels;

namespace TradingConsole.Wpf.Services
{
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
            if (result.InstrumentGroup != "INDEX") return;

            // Step 1: Determine the high-level thesis and dominant player
            MarketThesis thesis = UpdateIntradayThesis(result);
            result.MarketThesis = thesis;

            // Step 2: Calculate conviction score using the new confluence-based model
            var (bullDrivers, bearDrivers, conviction, isChoppy) = CalculateConfluenceScore(result, thesis);
            result.BullishDrivers = bullDrivers;
            result.BearishDrivers = bearDrivers;

            // --- NEW: Handle Market Open Volatility ---
            if (_stateManager.CurrentMarketPhase == MarketPhase.Opening)
            {
                conviction = (int)Math.Round(conviction * 0.5); // Reduce conviction by 50% during open
            }
            result.ConvictionScore = conviction;

            // Step 3: Determine final signal based on score and market condition
            string playbook;
            if (isChoppy)
            {
                playbook = "Choppy / Conflicting Signals";
                thesis = MarketThesis.Choppy;
                result.MarketThesis = thesis;
            }
            else if (conviction >= 7) playbook = "Strong Bullish Conviction";
            else if (conviction >= 3) playbook = "Moderate Bullish Conviction";
            else if (conviction <= -7) playbook = "Strong Bearish Conviction";
            else if (conviction <= -3) playbook = "Moderate Bearish Conviction";
            else playbook = "Neutral / Observe";

            string newPrimarySignal = "Neutral";
            if (!isChoppy) // Do not generate primary signals in choppy markets
            {
                if (conviction >= 3) newPrimarySignal = "Bullish";
                else if (conviction <= -3) newPrimarySignal = "Bearish";
            }

            // Step 4: Update result and log/notify if signal has changed
            string oldPrimarySignal = result.PrimarySignal;
            result.PrimarySignal = newPrimarySignal;
            result.FinalTradeSignal = playbook;
            result.MarketNarrative = GenerateMarketNarrative(result);

            if (result.PrimarySignal != oldPrimarySignal && oldPrimarySignal != "Initializing")
            {
                if (_stateManager.LastSignalTime.TryGetValue(result.SecurityId, out var lastTime) && (DateTime.UtcNow - lastTime).TotalSeconds < 60)
                {
                    return; // Debounce signals to prevent rapid flipping
                }
                _stateManager.LastSignalTime[result.SecurityId] = DateTime.UtcNow;

                _signalLoggerService.LogSignal(result);
                Task.Run(() => _notificationService.SendTelegramSignalAsync(result, oldPrimarySignal));
            }
        }

        /// <summary>
        /// REVISED: Calculates conviction score by grouping correlated signals to avoid double-counting
        /// and explicitly detects choppy market conditions.
        /// </summary>
        private (List<string> BullishDrivers, List<string> BearishDrivers, int Score, bool IsChoppy) CalculateConfluenceScore(AnalysisResult r, MarketThesis thesis)
        {
            var bullDrivers = new List<string>();
            var bearDrivers = new List<string>();

            // Define signal groups to prevent correlation issues
            int structureScore = 0;
            int momentumScore = 0;
            int confirmationScore = 0;

            // --- Group 1: Market Structure (Long-Term Context) ---
            if (r.MarketStructure == "Trending Up") structureScore += 3;
            if (r.MarketStructure == "Trending Down") structureScore -= 3;
            if (r.YesterdayProfileSignal == "Trading Above Y-VAH") structureScore += 2;
            if (r.YesterdayProfileSignal == "Trading Below Y-VAL") structureScore -= 2;

            // --- Group 2: Intraday Momentum ---
            if (r.PriceVsVwapSignal == "Above VWAP") momentumScore += 2;
            if (r.PriceVsVwapSignal == "Below VWAP") momentumScore -= 2;
            if (r.EmaSignal5Min == "Bullish Cross") momentumScore += 2;
            if (r.EmaSignal5Min == "Bearish Cross") momentumScore -= 2;
            if (r.CandleSignal5Min.Contains("Bullish")) momentumScore += 1;
            if (r.CandleSignal5Min.Contains("Bearish")) momentumScore -= 1;

            // --- Group 3: Confirmation (Volume, OI, Volatility) ---
            if (r.VolumeSignal == "Volume Burst" && r.LTP > r.Vwap) confirmationScore += 2;
            if (r.VolumeSignal == "Volume Burst" && r.LTP < r.Vwap) confirmationScore -= 2;
            if (r.OiSignal == "Long Buildup") confirmationScore += 2;
            if (r.OiSignal == "Short Buildup") confirmationScore -= 2;
            if (r.IntradayIvSpikeSignal == "IV Spike Up") confirmationScore += 1; // IV spike can confirm a breakout

            // --- NEW: Logic to detect conflicting signals (chop) ---
            bool isChoppy = (Math.Abs(structureScore) < 2 && Math.Abs(momentumScore) < 2) || // No clear direction
                            (structureScore > 2 && momentumScore < -2) || // Structure is bullish but momentum is bearish
                            (structureScore < -2 && momentumScore > 2);  // Structure is bearish but momentum is bullish

            // Combine scores based on confluence
            int finalScore = structureScore + momentumScore + confirmationScore;

            // Populate driver lists for UI display (simplified for brevity)
            if (structureScore > 0) bullDrivers.Add($"Structure Bullish (+{structureScore})"); else if (structureScore < 0) bearDrivers.Add($"Structure Bearish ({structureScore})");
            // --- THIS IS THE CORRECTED LINE ---
            if (momentumScore > 0) bullDrivers.Add($"Momentum Bullish (+{momentumScore})"); else if (momentumScore < 0) bearDrivers.Add($"Momentum Bearish ({momentumScore})");
            if (confirmationScore > 0) bullDrivers.Add($"Confirmation Bullish (+{confirmationScore})"); else if (confirmationScore < 0) bearDrivers.Add($"Confirmation Bearish ({confirmationScore})");

            return (bullDrivers, bearDrivers, finalScore, isChoppy);
        }

        private MarketThesis UpdateIntradayThesis(AnalysisResult result) { DominantPlayer player = DetermineDominantPlayer(result); result.DominantPlayer = player; if (result.MarketStructure == "Trending Up") { if (player == DominantPlayer.Buyers) return MarketThesis.Bullish_Trend; if (player == DominantPlayer.Sellers) return MarketThesis.Bullish_Rotation; return MarketThesis.Bullish_Trend; } if (result.MarketStructure == "Trending Down") { if (player == DominantPlayer.Sellers) return MarketThesis.Bearish_Trend; if (player == DominantPlayer.Buyers) return MarketThesis.Bearish_Rotation; return MarketThesis.Bearish_Trend; } return MarketThesis.Balancing; }
        private DominantPlayer DetermineDominantPlayer(AnalysisResult result) { int buyerEvidence = 0; int sellerEvidence = 0; if (result.PriceVsVwapSignal == "Above VWAP") buyerEvidence++; if (result.PriceVsVwapSignal == "Below VWAP") sellerEvidence++; if (result.EmaSignal5Min == "Bullish Cross") buyerEvidence++; if (result.EmaSignal5Min == "Bearish Cross") sellerEvidence++; if (result.OiSignal == "Long Buildup") buyerEvidence++; if (result.OiSignal == "Short Buildup") sellerEvidence++; if (buyerEvidence > sellerEvidence) return DominantPlayer.Buyers; if (sellerEvidence > buyerEvidence) return DominantPlayer.Sellers; return DominantPlayer.Balance; }
        private string GenerateMarketNarrative(AnalysisResult r) { return $"Thesis: {r.MarketThesis}. Dominant Player: {r.DominantPlayer}. Open: {r.OpenTypeSignal}. vs VWAP: {r.PriceVsVwapSignal}."; }
    }
}
