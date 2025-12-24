# 💸 AI Pénzügyi Asszisztens (BillingsAtOnePlace) ASP.NET Web Core API

> **Automatikus költségkövetés banki értesítések alapján, .NET és Mesterséges Intelligencia segítségével.**

![.NET](https://img.shields.io/badge/.NET-9.0-purple?style=flat-square&logo=dotnet)
![MacroDroid](https://img.shields.io/badge/Mobile-MacroDroid-green?style=flat-square&logo=android)
![AI](https://img.shields.io/badge/AI-OpenRouter-orange?style=flat-square&logo=openai)
![Status](https://img.shields.io/badge/Status-Active-success?style=flat-square)

Ez a projekt egy saját fejlesztésű, helyi hálózaton futó rendszer ("Home Lab" jellegű), amely **automatikusan rögzíti a pénzügyi tranzakciókat**. 

Nem kell többé Excel táblákat töltögetni: amint a telefonodon pittyen egy banki SMS vagy Push értesítés (Revolut, OTP), a rendszer elkapja, elküldi a PC-dnek/Szerverednek, ahol az AI elemzi, kategorizálja és elmenti.

---

## ⚙️ Működési Elv

A rendszer három fő komponensből áll, amelyek szinkronban dolgoznak:

```mermaid
graph LR
    A[📱 Telefon (MacroDroid)] -- 1. Értesítés JSON --> B(📡 .NET Webhook)
    B -- 2. Nyers szöveg --> C{🧠 OpenRouter AI}
    C -- 3. Strukturált Adat --> B
    B -- 4. Hozzáfűzés --> D[(💾 transactions.jsonl)]

MacroDroid (Android): Figyeli a bejövő értesítéseket. Ha pénzügyi apptól jön, azonnal továbbítja a szervernek.

Backend (.NET 8): Fogadja az adatot, és továbbítja egy LLM-nek (Large Language Model).

AI (GPT-4o-mini): Értelmezi a szöveget (pl. "Vásárlás SPAR 4500 Ft" -> Bolt: Spar, Kategória: Élelmiszer, Típus: Kiadás).

🚀 Telepítés és Futtatás
1. Előfeltételek
.NET 9.0 SDK telepítve a gépen.

MacroDroid alkalmazás az Android telefonon.

OpenRouter API kulcs (vagy OpenAI kulcs).

2. Biztonsági beállítások (Secrets)
Mivel a kódot verziókezeljük, az API kulcsot nem írjuk a kódba. Hozz létre egy secrets.json fájlt a projekt gyökerében (a .csproj mellett):

JSON

{
  "OpenRouterApiKey": "sk-or-v1-IDE_MASOLD_A_KULCSODAT"
}
(Megjegyzés: Ez a fájl bekerült a .gitignore-ba, így biztonságos.)

3. Szerver indítása
Nyiss egy terminált a projekt mappájában:

Bash

dotnet run
A szerver elindul (alapértelmezetten: http://localhost:5000 vagy 5001).

📱 MacroDroid Beállítása (Android)
Ez a rendszer "füle". Így konfiguráld a telefonodon:

Új Makró hozzáadása.

Trigger (Indító): Értesítés érkezett -> Válaszd ki a banki appokat (pl. OTP, Revolut, Wallet).

Action (Művelet): HTTP Request (HTTP Kérés).

URL: http://[A_GEPED_HELYI_IP_CIME]:5000/api/webhook

Fontos: Ne a localhost-ot írd ide, hanem a géped LAN IP címét (pl. 192.168.1.15), és legyenek egy Wi-Fi-n!

Method: POST

Content Type: application/json

Body:

JSON

{
  "appName": "[notification_app_name]",
  "title": "[notification_title]",
  "text": "[notification_text]"
}
Mentés: Nevezd el (pl. "Bank to PC") és teszteld!

🛠️ Technikai Kihívások és Megoldások (The "Hard Parts")
A fejlesztés során több érdekes akadályba ütköztem, amikből sokat tanultam:

1. "Localhost" vs. Android 🌐
Probléma: A telefonról a localhost hívás nem a PC-t éri el, hanem magát a telefont.

Megoldás: A számítógép Helyi IP címét (Local LAN IP) kellett használni. Emellett a tűzfalon engedélyezni kellett a bejövő kapcsolatot az adott porton.

2. Adatbázis helyett: A JSONL formátum ereje 📄
Döntés: SQL adatbázis vagy sima JSON helyett .jsonl-t használok.

Miért? Ha a program futás közben leáll, vagy egyszerre írunk a fájlba, a sima JSON tömb ([...]) könnyen megsérülhet (lemarad a zárójel).

Megoldás: JSONL (JSON Lines). Minden sor egy önálló, teljes JSON objektum. Ha az egyik sor sérült, a többi attól még olvasható marad. Ráadásul Append (hozzáfűzés) művelettel erőforrás-kímélő.

3. Az AI "hallucinációi" és tisztítása 🧹
Probléma: Az AI válasza néha tartalmazott Markdown kereteket (```json), vagy extra szöveget, amitől a kód elszállt.

Megoldás: 1. Szigorú System Prompt: "Respond only with valid JSON." 2. Code-side Cleaning: A C# kódban manuálisan eltávolítjuk a Markdown jelöléseket a deszerializálás előtt (Replace logika).

4. Karakterkódolás (UTF-8) 🔡
Probléma: A mentett fájlban \u00E1 jelent meg á helyett.

Megoldás: A JsonSerializerOptions-ben be kellett állítani az UnsafeRelaxedJsonEscaping opciót, így a fájl emberi szemmel is tökéletesen olvasható maradt.

🔮 Jövőbeli tervek
[ ] Havi statisztikák és grafikonok generálása.

[ ] Egyszerű Web UI (Angular/React) a transactions.jsonl megjelenítésére.

[ ] Docker konténerizáció a könnyebb futtatáshoz.