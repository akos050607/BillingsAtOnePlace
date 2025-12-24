using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;

namespace BillingsAtOnePlace.Controllers
{
    // The Controller is responsible for handling requests.
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        // We store transactions in this file.
        // The extension is .jsonl (JSON Lines), meaning each line is a valid JSON object.
        private readonly string _filePath = "transactions.jsonl";

        // The name of the AI model. Can be swapped for e.g. "google/gemini-2.0-flash-exp".
        private const string AI_MODEL = "openai/gpt-4o-mini";
        private static readonly HttpClient _httpClient = new HttpClient();
        
        private string GetApiKey()
        {
            // The secrets.json file is excluded in .gitignore, so it won't be uploaded to GitHub.
            const string secretFile = "secrets.json";
            
            if (!System.IO.File.Exists(secretFile))
            {
                Console.WriteLine("❌ ERROR: secrets.json file not found! Create it in the project root.");
                return "";
            }

            try
            {
                var content = System.IO.File.ReadAllText(secretFile);
                using var doc = JsonDocument.Parse(content);
                
                // We look for the OpenRouterApiKey property in the JSON.
                return doc.RootElement.GetProperty("OpenRouterApiKey").GetString() ?? "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERROR reading secrets.json: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// [GET] Endpoint: api/webhook
        /// The Frontend calls this to retrieve saved data.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTransactions()
        {
            if (!System.IO.File.Exists(_filePath))
            {
                return Ok(new List<object>());
            }

            var result = new List<object>();
            
            var lines = await System.IO.File.ReadAllLinesAsync(_filePath);

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    try
                    {
                        var transaction = JsonSerializer.Deserialize<object>(line);
                        if (transaction != null) result.Add(transaction);
                    }
                    catch 
                    {
                        // Swallow error for corrupted lines
                    }
                }
            }

            result.Reverse();
            return Ok(result);
        }

        /// <summary>
        /// [POST] Endpoint: api/webhook
        /// Raw notifications arrive here.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            string rawContent;
            
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawContent = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(rawContent)) return Ok();

            // Split into lines (batch processing)
            var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<NotificationDto>(line);
                    if (payload != null)
                    {
                        await ProcessWithAi(payload);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Error processing incoming data: {ex.Message}");
                }
            }

            return Ok();
        }

        /// <summary>
        /// The "Engine": Assembles data, calls AI, saves result.
        /// </summary>
        private async Task ProcessWithAi(NotificationDto payload)
        {
            // Prepare the text for AI analysis
            string fullText = $"App: {payload.AppName}, Title: {payload.Title}, Text: {payload.Text}";
            Console.WriteLine($"🤖 AI Analysis in progress: \"{payload.Title}\"...");

            var aiResult = await CallOpenRouter(fullText);

            if (aiResult != null)
            {
                // Filter: If AI says "none" (e.g. ad, 2FA code), skip saving.
                if (aiResult.Type == "none")
                {
                    Console.WriteLine($"   🗑️  Skipped (Not a financial transaction): {payload.Title}");
                    return;
                }

                var transaction = new
                {
                    Date = DateTime.Now,
                    App = payload.AppName,
                    Shop = aiResult.Shop,
                    RawText = payload.Text,
                    Amount = aiResult.Amount,
                    Category = aiResult.Category,
                    Type = aiResult.Type
                };

                var options = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false 
                };

                string jsonLine = JsonSerializer.Serialize(transaction, options);
                await System.IO.File.AppendAllTextAsync(_filePath, jsonLine + Environment.NewLine);

                // English console feedback
                string icon = aiResult.Type == "income" ? "💰 INCOME" : "💸 EXPENSE";
                Console.WriteLine($"   ✅ {icon}: {transaction.Shop} | {transaction.Amount:N0} $ | ({transaction.Category})");
            }
            else
            {
                Console.WriteLine("   ⚠️ AI found no meaningful data, or a call error occurred.");
            }
        }

        /// <summary>
        /// HTTP call to OpenRouter API.
        /// </summary>
        private async Task<AiExtractionResult?> CallOpenRouter(string text)
        {
            string apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) return null;

            // UPDATED PROMPT (Now in English)
            var prompt = $@"
                You are a financial assistant. Analyze the following bank notification.
                Input: ""{text}""
                
                Task:
                1. Determine the type:
                   - ""expense"": purchase or money sent.
                   - ""income"": salary, incoming transfer, refund.
                   - ""none"": advertisement, login code, system message, or no amount involved.
                2. Identify the Shop/Partner (shop).
                3. Extract the Amount (amount): Number only, no currency.
                4. Categorize it (category): e.g., Groceries, Transport, Salary, Entertainment.
                
                Response Format (Strict JSON only):
                {{
                    ""shop"": ""Shop Name (or 'Unknown')"",
                    ""amount"": 1000, (Can be in euros or local currency, convert it to dollars if needed)
                    ""category"": ""Category Name"",
                    ""type"": ""expense"" OR ""income"" OR ""none""
                }}
            ";

            var requestBody = new
            {
                model = AI_MODEL,
                messages = new[]
                {
                    new { role = "system", content = "You are a financial API. Respond only with valid JSON." },
                    new { role = "user", content = prompt }
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            requestMessage.Headers.Add("HTTP-Referer", "http://localhost:5000");
            requestMessage.Content = jsonContent;

            try
            {
                var response = await _httpClient.SendAsync(requestMessage);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"API Error Code: {response.StatusCode}. Message: {responseString}");
                    return null;
                }

                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    
                    // Clean markdown if present
                    content = content?.Replace("```json", "").Replace("```", "").Trim();
                    
                    return JsonSerializer.Deserialize<AiExtractionResult>(content!);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ API Call Exception: {ex.Message}");
                return null;
            }
        }

        // --- Data Models ---

        public class AiExtractionResult
        {
            [JsonPropertyName("shop")] public string Shop { get; set; } = "Unknown";
            [JsonPropertyName("amount")] public decimal Amount { get; set; }
            [JsonPropertyName("category")] public string Category { get; set; } = "Other";
            [JsonPropertyName("type")] public string Type { get; set; } = "none";
        }

        public class NotificationDto
        {
            [JsonPropertyName("appName")] public string AppName { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("text")] public string Text { get; set; } = "";
        }
    }
}