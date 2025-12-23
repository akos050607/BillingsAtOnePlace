using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;

namespace BillingsAtOnePlace.Controllers
{
    // A Controller felelős a kérések fogadásáért.
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        // Ebben a fájlban tároljuk a tranzakciókat.
        // A kiterjesztés .jsonl (JSON Lines), ami azt jelenti, hogy minden sor egy érvényes JSON objektum 
        // (azért kell, mert a telefon értesítése ha nincs net azokat tömbökbe vezetem, és ha lesz net json line-okba küldi lásd: README.md).
        private readonly string _filePath = "transactions.jsonl";

        // Az AI modell neve. Lecserélhető pl. "google/gemini-2.0-flash-exp"-re is.
        private const string AI_MODEL = "openai/gpt-4o-mini";
        private static readonly HttpClient _httpClient = new HttpClient();
        private string GetApiKey()
        {
            // A secrets.json fájlt a .gitignore-ban kizártuk, így nem kerül fel a GitHubra, illeszd be a saját openrouter api kulcsod!.
            const string secretFile = "secrets.json";
            
            if (!System.IO.File.Exists(secretFile))
            {
                Console.WriteLine("❌ HIBA: Nem található a secrets.json fájl! Hozd létre a projekt gyökerében.");
                return "";
            }

            try
            {
                var content = System.IO.File.ReadAllText(secretFile);
                using var doc = JsonDocument.Parse(content);
                
                // Megkeressük az OpenRouterApiKey tulajdonságot a JSON-ben
                return doc.RootElement.GetProperty("OpenRouterApiKey").GetString() ?? "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ HIBA a secrets.json olvasásakor: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// [GET] Végpont: api/webhook
        /// Ezt hívja meg a Frontend (Angular/React/Mobile), hogy lekérje a mentett adatokat. (nincs megvalósítva a frontend rész)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTransactions()
        {
            // Ha még nincs fájl (első futtatás), üres listával térünk vissza, ne legyen hiba.
            if (!System.IO.File.Exists(_filePath))
            {
                return Ok(new List<object>());
            }

            var result = new List<object>();
            
            // Aszinkron módon beolvassuk az összes sort
            var lines = await System.IO.File.ReadAllLinesAsync(_filePath);

            foreach (var line in lines)
            {
                // Üres sorokat átugorjuk
                if (!string.IsNullOrWhiteSpace(line))
                {
                    try
                    {
                        // Visszaalakítjuk a szöveget objektummá
                        var transaction = JsonSerializer.Deserialize<object>(line);
                        if (transaction != null) result.Add(transaction);
                    }
                    catch 
                    {
                        // Ha egy sor sérült, "lenyeljük" a hibát, hogy a többi adat megjelenjen
                    }
                }
            }

            // Megfordítjuk a sorrendet: a legfrissebb tranzakció legyen elöl
            result.Reverse();

            return Ok(result);
        }

        /// <summary>
        /// [POST] Végpont: api/webhook
        /// Ide érkeznek a mobilról/alkalmazásból a nyers értesítések.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            string rawContent;
            
            // Beolvassuk a teljes HTTP kérés törzsét (Body)
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawContent = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(rawContent)) return Ok();

            // Feldaraboljuk sorokra, ha esetleg több értesítés jönne egyszerre (batch)
            var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                try
                {
                    // Megpróbáljuk a bejövő adatot NotificationDto formátumra alakítani
                    var payload = JsonSerializer.Deserialize<NotificationDto>(line);
                    if (payload != null)
                    {
                        // Ha sikerült, elindítjuk az AI feldolgozást
                        await ProcessWithAi(payload);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Hiba a bejövő adat feldolgozásakor: {ex.Message}");
                }
            }

            return Ok();
        }

        /// <summary>
        /// A "Motor": Ez a függvény rakja össze az adatokat, küldi el az AI-nak és menti le.
        /// </summary>
        private async Task ProcessWithAi(NotificationDto payload)
        {
            // Összefűzzük az értesítés minden releváns részét egy stringgé az AI számára
            string fullText = $"App: {payload.AppName}, Cím: {payload.Title}, Szöveg: {payload.Text}";
            Console.WriteLine($"🤖 AI Elemzése folyamatban: \"{payload.Title}\"...");

            // Meghívjuk az OpenRouter API-t
            var aiResult = await CallOpenRouter(fullText);

            if (aiResult != null)
            {
                // Szűrés: Ha az AI szerint ez nem pénzügyi tétel (pl. reklám), nem mentjük.
                if (aiResult.Type == "none")
                {
                    Console.WriteLine($"   🗑️  Kihagyva (Nem pénzmozgás): {payload.Title}");
                    return;
                }

                // Létrehozzuk a végleges adatszerkezetet
                var transaction = new
                {
                    Date = DateTime.Now,            // Mentés ideje
                    App = payload.AppName,          // Forrás app
                    Shop = aiResult.Shop,           // AI által felismert bolt
                    RawText = payload.Text,         // Eredeti szöveg (debug)
                    Amount = aiResult.Amount,       // Összeg
                    Category = aiResult.Category,   // Kategória
                    Type = aiResult.Type            // expense / income
                };

                // JSON szerializálási beállítások
                var options = new JsonSerializerOptions
                {
                    // "UnsafeRelaxedJsonEscaping": Fontos! Ez engedi, hogy az ékezetes betűk (á, é)
                    // olvashatóan maradjanak meg, ne kódolva (\u00E1).
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    
                    // "WriteIndented = false": Mindent egy sorba írunk, hogy valid JSONL maradjon.
                    WriteIndented = false 
                };

                // Objektum átalakítása JSON stringgé
                string jsonLine = JsonSerializer.Serialize(transaction, options);

                // Hozzáfűzzük a fájl végére (+ sortörés)
                await System.IO.File.AppendAllTextAsync(_filePath, jsonLine + Environment.NewLine);

                // Visszajelzés a konzolra (Szép, olvasható formában)
                string icon = aiResult.Type == "income" ? "💰 BEVÉTEL" : "💸 KIADÁS";
                Console.WriteLine($"   ✅ {icon}: {transaction.Shop} | {transaction.Amount:N0} Ft | ({transaction.Category})");
            }
            else
            {
                Console.WriteLine("   ⚠️ Az AI nem talált értelmes adatot, vagy hiba történt a híváskor.");
            }
        }

        /// <summary>
        /// A tényleges HTTP hívás az OpenRouter API felé.
        /// </summary>
        private async Task<AiExtractionResult?> CallOpenRouter(string text)
        {
            // Biztonságos kulcslekérés
            string apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) return null; // Ha nincs kulcs, megállunk

            // A Prompt (Utasítás) az AI számára
            var prompt = $@"
                Te egy pénzügyi asszisztens vagy. Elemezd az alábbi banki értesítést.
                Bemenet: ""{text}""
                
                Feladat:
                1. Típus (type): ""expense"" (kiadás), ""income"" (bevétel), vagy ""none"" (ha irreleváns).
                2. Bolt (shop): A tranzakció helye.
                3. Összeg (amount): Csak a szám, pénznem nélkül.
                4. Kategória (category): Pl. Élelmiszer, Szórakozás, Utazás.
                
                Válaszformátum (szigorúan JSON):
                {{
                    ""shop"": ""..."",
                    ""amount"": 0,
                    ""category"": ""..."",
                    ""type"": ""...""
                }}
            ";

            // A kérés törzse, amit az OpenRouternek küldünk
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
            
            // HTTP kérés összeállítása
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            
            // Fejlécek beállítása (Hitelesítés)
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            requestMessage.Headers.Add("HTTP-Referer", "http://localhost:5000");
            requestMessage.Content = jsonContent;

            try
            {
                // Kérés elküldése
                var response = await _httpClient.SendAsync(requestMessage);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"API Hiba kód: {response.StatusCode}. Üzenet: {responseString}");
                    return null;
                }

                // Válasz feldolgozása
                using (JsonDocument doc = JsonDocument.Parse(responseString))
                {
                    // A válasz mélyen van a JSON-ben: choices -> 0 -> message -> content
                    var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    
                    // Tisztítás: Ha az AI véletlenül Markdown blokkba tenné a választ (```json ... ```), azt levágjuk.
                    content = content?.Replace("```json", "").Replace("```", "").Trim();
                    
                    // Visszaalakítás C# objektummá
                    return JsonSerializer.Deserialize<AiExtractionResult>(content!);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ API Hívás közbeni kivétel: {ex.Message}");
                return null;
            }
        }

        // --- Adatmodellek ---

        // Ebbe olvassuk be az AI válaszát
        public class AiExtractionResult
        {
            [JsonPropertyName("shop")] public string Shop { get; set; } = "Ismeretlen";
            [JsonPropertyName("amount")] public decimal Amount { get; set; }
            [JsonPropertyName("category")] public string Category { get; set; } = "Egyéb";
            [JsonPropertyName("type")] public string Type { get; set; } = "none";
        }

        // Ebbe olvassuk be a telefonról érkező értesítést
        public class NotificationDto
        {
            [JsonPropertyName("appName")] public string AppName { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("text")] public string Text { get; set; } = "";
        }
    }
}