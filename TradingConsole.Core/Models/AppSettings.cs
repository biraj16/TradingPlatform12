using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TradingConsole.Core.Models
{
    public class SignalDriver : ObservableModel
    {
        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private int _weight;
        public int Weight { get => _weight; set => SetProperty(ref _weight, value); }

        private bool _isEnabled = true;
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }

        public SignalDriver(string name, int weight, bool isEnabled = true)
        {
            Name = name;
            Weight = weight;
            IsEnabled = isEnabled;
        }

        public SignalDriver() { }

        protected new bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value)) return false;
            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class StrategySettings
    {
        // --- MAGA REFACTOR: Renamed and restructured for thesis-driven logic ---
        public ObservableCollection<SignalDriver> TrendContinuation_Bullish { get; set; }
        public ObservableCollection<SignalDriver> TrendContinuation_Bearish { get; set; }
        public ObservableCollection<SignalDriver> MeanReversion_Bullish { get; set; }
        public ObservableCollection<SignalDriver> MeanReversion_Bearish { get; set; }
        public ObservableCollection<SignalDriver> Reversal_Bullish { get; set; }
        public ObservableCollection<SignalDriver> Reversal_Bearish { get; set; }

        public StrategySettings()
        {
            TrendContinuation_Bullish = new ObservableCollection<SignalDriver>
            {
                new SignalDriver("True Acceptance Above Y-VAH", 5),
                new SignalDriver("Institutional Intent is Bullish", 4),
                new SignalDriver("5m VWAP EMA confirms bullish trend", 3),
                new SignalDriver("IB breakout is extending", 3),
                new SignalDriver("Bullish Pattern with Volume Confirmation", 3),
                new SignalDriver("Price above VWAP", 2),
                new SignalDriver("OI confirms new longs", 2),
                new SignalDriver("Initiative Buying Above Y-VAH", 2),
            };

            TrendContinuation_Bearish = new ObservableCollection<SignalDriver>
            {
                new SignalDriver("True Acceptance Below Y-VAL", 5),
                new SignalDriver("Institutional Intent is Bearish", 4),
                new SignalDriver("5m VWAP EMA confirms bearish trend", 3),
                new SignalDriver("IB breakdown is extending", 3),
                new SignalDriver("Bearish Pattern with Volume Confirmation", 3),
                new SignalDriver("Price below VWAP", 2),
                new SignalDriver("OI confirms new shorts", 2),
                new SignalDriver("Initiative Selling Below Y-VAL", 2),
            };

            MeanReversion_Bullish = new ObservableCollection<SignalDriver>
            {
                 new SignalDriver("Bullish Pattern at Key Support", 4),
                 new SignalDriver("Bullish Skew Divergence (Full)", 3),
                 new SignalDriver("Bullish OBV Div at range low", 3),
                 new SignalDriver("Bullish RSI Div at range low", 2),
                 new SignalDriver("Low volume suggests exhaustion (Bullish)", 1),
            };

            MeanReversion_Bearish = new ObservableCollection<SignalDriver>
            {
                new SignalDriver("Bearish Pattern at Key Resistance", 4),
                new SignalDriver("Bearish Skew Divergence (Full)", 3),
                new SignalDriver("Bearish OBV Div at range high", 3),
                new SignalDriver("Range Contraction", 2),
                new SignalDriver("Bearish RSI Div at range high", 2),
                new SignalDriver("Low volume suggests exhaustion (Bearish)", 1),
            };

            Reversal_Bullish = new ObservableCollection<SignalDriver>
            {
                new SignalDriver("Look Below and Fail at Y-VAL", 5),
                new SignalDriver("Bullish Skew Divergence (Full)", 4),
                new SignalDriver("Bullish Pattern at Key Support", 3),
            };

            Reversal_Bearish = new ObservableCollection<SignalDriver>
            {
                new SignalDriver("Look Above and Fail at Y-VAH", 5),
                new SignalDriver("Bearish Skew Divergence (Full)", 4),
                new SignalDriver("Bearish Pattern at Key Resistance", 3),
            };
        }
    }

    /// <summary>
    /// Holds all settings related to trade automation.
    /// </summary>
    public class AutomationSettings : ObservableModel
    {
        private bool _isAutomationEnabled;
        public bool IsAutomationEnabled { get => _isAutomationEnabled; set => SetProperty(ref _isAutomationEnabled, value); }

        private string _selectedAutoTradeIndex = "Nifty 50"; // Default to Nifty 50
        public string SelectedAutoTradeIndex { get => _selectedAutoTradeIndex; set => SetProperty(ref _selectedAutoTradeIndex, value); }

        [JsonIgnore] // This doesn't need to be saved in the settings file
        public List<string> AutoTradeableIndices { get; } = new List<string> { "Nifty 50", "Nifty Bank", "Sensex" };

        private int _lotsPerTrade = 1;
        public int LotsPerTrade { get => _lotsPerTrade; set => SetProperty(ref _lotsPerTrade, value); }

        private decimal _stopLossPoints = 10;
        public decimal StopLossPoints { get => _stopLossPoints; set => SetProperty(ref _stopLossPoints, value); }

        private decimal _targetPoints = 20;
        public decimal TargetPoints { get => _targetPoints; set => SetProperty(ref _targetPoints, value); }

        private bool _isTrailingEnabled;
        public bool IsTrailingEnabled { get => _isTrailingEnabled; set => SetProperty(ref _isTrailingEnabled, value); }

        private decimal _trailingStopLossJump = 5;
        public decimal TrailingStopLossJump { get => _trailingStopLossJump; set => SetProperty(ref _trailingStopLossJump, value); }

        private int _minConvictionScore = 7;
        public int MinConvictionScore { get => _minConvictionScore; set => SetProperty(ref _minConvictionScore, value); }
    }


    public class IndexLevels
    {
        public decimal NoTradeUpperBand { get; set; }
        public decimal NoTradeLowerBand { get; set; }
        public decimal SupportLevel { get; set; }
        public decimal ResistanceLevel { get; set; }
        public decimal Threshold { get; set; }
    }

    public class AppSettings
    {
        public Dictionary<string, int> FreezeQuantities { get; set; }
        public List<string> MonitoredSymbols { get; set; }
        public int ShortEmaLength { get; set; }
        public int LongEmaLength { get; set; }

        public int AtrPeriod { get; set; }
        public int AtrSmaPeriod { get; set; }

        public int RsiPeriod { get; set; }
        public int RsiDivergenceLookback { get; set; }
        public int VolumeHistoryLength { get; set; }
        public double VolumeBurstMultiplier { get; set; }
        public int IvHistoryLength { get; set; }
        public decimal IvSpikeThreshold { get; set; }

        public int ObvMovingAveragePeriod { get; set; }

        public decimal VwapUpperBandMultiplier { get; set; }
        public decimal VwapLowerBandMultiplier { get; set; }


        public Dictionary<string, IndexLevels> CustomIndexLevels { get; set; }
        public List<DateTime> MarketHolidays { get; set; }

        public bool IsAutoKillSwitchEnabled { get; set; }
        public decimal MaxDailyLossLimit { get; set; }

        public StrategySettings Strategy { get; set; }

        // --- ADDED: New property for Automation Settings ---
        public AutomationSettings AutomationSettings { get; set; }

        public bool IsTelegramNotificationEnabled { get; set; }
        public string? TelegramBotToken { get; set; }
        public string? TelegramChatId { get; set; }


        public AppSettings()
        {
            FreezeQuantities = new Dictionary<string, int>
            {
                { "NIFTY", 1800 },
                { "BANKNIFTY", 900 },
                { "FINNIFTY", 1800 },
                { "SENSEX", 1000 }
            };

            // --- MODIFIED: Cleaned up the default list to focus only on Nifty ---
            MonitoredSymbols = new List<string>
            {
                "IDX:Nifty 50",
                "FUT:NIFTY"
            };

            ShortEmaLength = 9;
            LongEmaLength = 21;

            AtrPeriod = 14;
            AtrSmaPeriod = 10;

            RsiPeriod = 14;
            RsiDivergenceLookback = 20;
            VolumeHistoryLength = 12;
            VolumeBurstMultiplier = 2.0;
            IvHistoryLength = 15;
            IvSpikeThreshold = 0.01m;

            ObvMovingAveragePeriod = 20;

            VwapUpperBandMultiplier = 2.0m;
            VwapLowerBandMultiplier = 2.0m;

            MarketHolidays = new List<DateTime>();

            IsAutoKillSwitchEnabled = false;
            MaxDailyLossLimit = 8000;

            CustomIndexLevels = new Dictionary<string, IndexLevels>
            {
                {
                    "NIFTY", new IndexLevels {
                        NoTradeUpperBand = 24500, NoTradeLowerBand = 24900,
                        SupportLevel = 24500, ResistanceLevel = 25500, Threshold = 20
                    }
                },
                {
                    "BANKNIFTY", new IndexLevels {
                        NoTradeUpperBand = 57500, NoTradeLowerBand = 56000,
                        SupportLevel = 56000, ResistanceLevel = 58000, Threshold = 50
                    }
                },
                {
                    "SENSEX", new IndexLevels {
                        NoTradeUpperBand = 84000, NoTradeLowerBand = 82500,
                        SupportLevel = 80100, ResistanceLevel = 85000, Threshold = 100
                    }
                }
            };

            Strategy = new StrategySettings();

            // --- ADDED: Initialize Automation Settings with defaults ---
            AutomationSettings = new AutomationSettings();

            IsTelegramNotificationEnabled = false;
            TelegramBotToken = string.Empty;
            TelegramChatId = string.Empty;
        }
    }
}
