# 💸 AI Financial Assistant (BillingsAtOnePlace) ASP.NET Web Core API
> **Automatic expense tracking based on bank notifications, powered by .NET and Artificial Intelligence.**

![.NET](https://img.shields.io/badge/.NET-9.0-purple?style=flat-square&logo=dotnet)
![MacroDroid](https://img.shields.io/badge/Mobile-MacroDroid-green?style=flat-square&logo=android)
![AI](https://img.shields.io/badge/AI-OpenRouter-orange?style=flat-square&logo=openai)
![Status](https://img.shields.io/badge/Status-Active-success?style=flat-square)

This is a custom-built, locally-networked system (a "Home Lab" project) that **automatically records financial transactions**. 

No more manual Excel spreadsheets: whenever you receive a bank notification on your phone (Revolut, OTP, etc.), the system captures it, sends it to your PC/Server, where AI analyzes, categorizes, and saves it.

## Demonstration

[Screencast from 2025-12-24 13-30-42.webm](https://github.com/user-attachments/assets/20453cc4-328e-4c52-a28e-753bcdfdb765)

<img width="270" height="500" alt="Screenshot_20251224-133433" src="https://github.com/user-attachments/assets/5e81e22b-9b5f-4259-b478-0b299fce5615" />
<img width="270" height="500" alt="Screenshot_20251224-133445" src="https://github.com/user-attachments/assets/d9a505ec-d131-4d29-a943-aa2d7a1256d5" />

---

## ⚙️ How It Works

The system consists of three main components working in sync:

```
📱 Phone (MacroDroid) 
  ↓ 1. Notification as JSON
📡 .NET Webhook Server
  ↓ 2. Raw text
🧠 OpenRouter AI
  ↓ 3. Structured data
💾 transactions.jsonl (persistent storage)
```

- **MacroDroid (Android)**: Monitors incoming notifications. When a financial app notification arrives, it immediately forwards it to the server.
- **Backend (.NET 9)**: Receives the data and forwards it to an LLM.
- **AI (GPT-4o-mini)**: Interprets the text (e.g., "Purchase SPAR 4500 Ft" → Shop: Spar, Category: Groceries, Type: Expense).

---

## 🚀 Installation and Setup

### 1. Prerequisites

- .NET 9.0 SDK installed
- MacroDroid app on your Android phone
- OpenRouter API key (or OpenAI key)

### 2. Security Setup (Secrets)

Create a `secrets.json` file in the project root (next to `.csproj`):

```json
{
  "OpenRouterApiKey": "PASTE_YOUR_KEY_HERE"
}
```

*(This file is in `.gitignore` for security)*

### 3. Start the Server

```bash
dotnet run
```

The server runs at `http://localhost:5000` or `5001`.

---

## 📱 MacroDroid Configuration (Android)

#### Create a New Macro:

1. **Trigger**: Notification received → Select banking apps (OTP, Revolut, Wallet, etc.)
2. **Action**: HTTP Request
3. **URL**: `http://[YOUR_PC_LOCAL_IP]:5000/api/webhook`
   - *Use your PC's LAN IP (e.g., 192.168.1.15), not localhost*
   - *Ensure both devices are on the same Wi-Fi*
4. **Method**: POST
5. **Content-Type**: application/json
6. **Body**:
   ```json
   {
     "appName": "[notification_app_name]",
     "title": "[notification_title]",
     "text": "[notification_text]"
   }
   ```

7. Save and test!

---

## 🛠️ Technical Challenges & Solutions

### 1. Localhost vs. Android 🌐

**Problem**: Phone requests to localhost don't reach the PC—they loop back to the phone.

**Solution**: Use your computer's LAN IP address and ensure the firewall allows incoming connections on that port.


### 2. JSONL Format Over Databases 📄

**Problem**: Standard JSON arrays corrupt if the program crashes mid-write.

**Solution**: Use JSONL (JSON Lines)—each line is a complete JSON object. If one line corrupts, others remain intact. Append operations are also resource-efficient.
(I used NGROK in this project, it was more maintainable)

### 3. AI Hallucinations & Cleanup

**Problem**: AI responses sometimes include Markdown wrappers (```json) or extra text, breaking parsing.

**Solution**: 
- Strict system prompt: "Respond only with valid JSON."
- Code-side cleaning: Remove Markdown syntax in C# before deserialization.

### 4. UTF-8 Character Encoding

**Problem**: Hungarian characters appeared as `\u00E1` instead of `á`.

**Solution**: Configure `JsonSerializerOptions` with `UnsafeRelaxedJsonEscaping` for human-readable output.

### 5. MacroDroid Macro Execution Issues

**Problem**: Macros fail to execute when device lacks connectivity, and concurrent macro runs cause conflicts preventing simultaneous transactions.

**Solution**: Add "Wait for data access" action before HTTP request, and use local variables to serialize macro execution and prevent race conditions. (It's a very basic demonstration, the real solution was much harder, but the main goal is to tell the basic idea)
