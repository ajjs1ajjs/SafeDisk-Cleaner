# SafeDisk Cleaner

Безпечний інструмент для аналізу та очищення дисків Windows із пріоритетом на безпеку даних, прозорість рішень та повний контроль користувача.

> **v0.3.0** — повний перепис на **WPF + .NET 10 LTS** (раніше: Tauri / Rust + React).

## 🚀 Основний функціонал

- **Scanner Engine**: багатопотоковий обхід файлової системи та пошук безпечних кандидатів на видалення (Temp, Crash Dumps, Browser Cache, Logs, Package Cache, Windows Update cache, DriverStore, Windows.old).
- **Confidence System**: кожен файл отримує рейтинг безпеки `0-100%` із поясненням причини.
- **Safety Engine**: ніколи не чіпає системні шляхи, захищені розширення та файли, що використовуються.
- **Режими роботи**: Analyze / Interactive / Auto / Dry Run.
- **Recovery System**: малі файли переміщуються в Кошик, великі — у Карантин із терміном зберігання.
- **Audit Log**: повний журнал усіх дій (EF Core + SQLite).
- **Дублікати**: пошук копій за BLAKE3-хешем.
- **WPF UI** (Material Design 3) та **CLI**.

## 🔒 Основні принципи безпеки

Програма **ніколи** не видаляє:
- Windows, Program Files, ProgramData, System32, Drivers, EFI, Recovery, Boot
- файли з розширеннями `.dll .sys .exe .cat .inf .msi .msp`
- файли з атрибутом SYSTEM, відкриті процесами або використані за останні N днів
- файли з цифровим підписом Microsoft (Advanced категорії)

## 🛠️ Стек технологій

| Шар | Технологія |
|-----|------------|
| Платформа | **WPF, .NET 10 LTS** |
| UI | MaterialDesignInXamlToolkit (Material Design 3), темна/світла тема |
| Архітектура | MVVM (CommunityToolkit.Mvvm), Dependency Injection, Generic Host |
| Дані | Entity Framework Core + SQLite |
| HTTP | HttpClientFactory + Refit + Polly (Retry/Timeout/Circuit Breaker) |
| Логування | Serilog (async sink, rolling file) |
| Валідація | FluentValidation |
| Хешування | BLAKE3 |
| Тести | xUnit + FluentAssertions + Moq |

## 💻 Розробка та запуск

### Системні вимоги

- **.NET 10 SDK** (WPF: Windows + Windows Desktop runtime)

### Перший запуск

```bash
dotnet restore
dotnet build -c Release
```

### WPF UI

```bash
dotnet run --project src/SafeDiskCleaner.App
```

### CLI

```bash
dotnet run --project src/SafeDiskCleaner.Cli -- analyze --roots C:\Users\Me\AppData\Local\Temp
dotnet run --project src/SafeDiskCleaner.Cli -- clean --dry-run
dotnet run --project src/SafeDiskCleaner.Cli -- clean --auto
dotnet run --project src/SafeDiskCleaner.Cli -- duplicates --roots D:\
dotnet run --project src/SafeDiskCleaner.Cli -- drives
dotnet run --project src/SafeDiskCleaner.Cli -- audit
dotnet run --project src/SafeDiskCleaner.Cli -- quarantine list
dotnet run --project src/SafeDiskCleaner.Cli -- update
```

### Тести

```bash
dotnet test
```

## 📁 Де зберігаються дані?

- **SQLite база**: `C:\ProgramData\SafeDisk\SafeDisk.db` (audit log, карантин)
- **Карантин**: `C:\ProgramData\SafeDisk\quarantine\`
- **Звіти**: `C:\ProgramData\SafeDisk\reports\`
- **Логи (Serilog)**: `C:\ProgramData\SafeDisk\logs\`
- **Налаштування**: `C:\ProgramData\SafeDisk\settings.json`

Якщо `C:\ProgramData` недоступний для запису — використовується `%LOCALAPPDATA%\SafeDisk`.

## 📦 Структура проєкту

```
SafeDiskCleaner.sln
├── Directory.Build.props       # спільна версія 0.3.0
├── src/
│   ├── SafeDiskCleaner.Core/           # домен: моделі, rules, confidence, safety, scanner, Windows interop
│   ├── SafeDiskCleaner.Infrastructure/ # EF Core, Refit+Polly, Serilog, сервіси, DI
│   ├── SafeDiskCleaner.App/            # WPF UI (MaterialDesign, MVVM)
│   └── SafeDiskCleaner.Cli/            # консольний застосунок
└── tests/
    └── SafeDiskCleaner.Tests/          # xUnit + FluentAssertions + Moq (89 тестів)
```
