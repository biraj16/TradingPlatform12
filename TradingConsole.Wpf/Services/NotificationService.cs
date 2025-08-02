using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using TradingConsole.Wpf.ViewModels;

namespace TradingConsole.Wpf.Services
{
    /// <summary>
    /// Service to handle sending notifications to external services like Telegram.
    /// </summary>
    public class NotificationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly SettingsViewModel _settingsViewModel;

        public NotificationService(SettingsViewModel settingsViewModel)
        {
            _settingsViewModel = settingsViewModel;
        }

        /// <summary>
        /// Sends a formatted trade signal notification to a Telegram chat via a bot.
        /// </summary>
        /// <param name="result">The analysis result containing the signal to send.</param>
        /// <param name="oldPrimarySignal">The previous primary signal state to determine if it's an entry or exit.</param>
        public async Task SendTelegramSignalAsync(AnalysisResult result, string oldPrimarySignal)
        {
            // Check if notifications are enabled and credentials are set
            if (!_settingsViewModel.IsTelegramNotificationEnabled ||
                string.IsNullOrWhiteSpace(_settingsViewModel.TelegramBotToken) ||
                string.IsNullOrWhiteSpace(_settingsViewModel.TelegramChatId))
            {
                return;
            }

            // Construct the message using Markdown for formatting
            var messageBuilder = new StringBuilder();
            string title;

            // --- NEW LOGIC for Entry/Exit/Reversal Titles ---
            if (result.PrimarySignal == "Bullish" && oldPrimarySignal != "Bullish")
            {
                title = $"*ENTRY SIGNAL: Go Long*";
            }
            else if (result.PrimarySignal == "Bearish" && oldPrimarySignal != "Bearish")
            {
                title = $"*ENTRY SIGNAL: Go Short*";
            }
            else if (result.PrimarySignal == "Neutral" && oldPrimarySignal == "Bullish")
            {
                title = $"*EXIT SIGNAL: Close Long Position*";
            }
            else if (result.PrimarySignal == "Neutral" && oldPrimarySignal == "Bearish")
            {
                title = $"*EXIT SIGNAL: Close Short Position*";
            }
            else
            {
                // This case handles a direct flip (e.g., Bullish to Bearish)
                // which implies exiting the old position and entering a new one.
                title = $"*REVERSAL SIGNAL: {result.FinalTradeSignal}*";
            }

            messageBuilder.AppendLine(title);
            messageBuilder.AppendLine($"Instrument: `{result.Symbol}`");
            messageBuilder.AppendLine($"LTP: `{result.LTP:N2}`");
            messageBuilder.AppendLine($"Conviction: `{result.ConvictionScore}`");
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("*Market Narrative:*");
            messageBuilder.AppendLine($"`{result.MarketNarrative}`");
            messageBuilder.AppendLine();

            if (result.BullishDrivers.Any())
            {
                messageBuilder.AppendLine("*Bullish Drivers:*");
                foreach (var driver in result.BullishDrivers)
                {
                    messageBuilder.AppendLine($"- `{driver}`");
                }
                messageBuilder.AppendLine();
            }

            if (result.BearishDrivers.Any())
            {
                messageBuilder.AppendLine("*Bearish Drivers:*");
                foreach (var driver in result.BearishDrivers)
                {
                    messageBuilder.AppendLine($"- `{driver}`");
                }
            }

            // Send the message
            await SendTelegramMessageAsync(messageBuilder.ToString());
        }


        /// <summary>
        /// Sends a message to the Telegram Bot API.
        /// </summary>
        /// <param name="message">The message content, supporting Markdown.</param>
        public async Task SendTelegramMessageAsync(string message)
        {
            var botToken = _settingsViewModel.TelegramBotToken;
            var chatId = _settingsViewModel.TelegramChatId;

            // The URL for the sendMessage method of the Telegram Bot API
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";

            // The content of the message
            var payload = new
            {
                chat_id = chatId,
                text = message,
                parse_mode = "Markdown"
            };

            try
            {
                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send the POST request
                var response = await _httpClient.PostAsync(url, content);

                // Optionally, check if the request was successful
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] Failed to send Telegram message. Status: {response.StatusCode}, Response: {errorContent}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[NotificationService] Successfully sent Telegram notification.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] Exception while sending Telegram message: {ex.Message}");
            }
        }
    }
}
