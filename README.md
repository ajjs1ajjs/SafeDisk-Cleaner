# SafeDisk Cleaner

Безпечний інструмент для аналізу та очищення дисків Windows із пріоритетом на безпеку даних, прозорість рішень та повний контроль користувача.

<p>
  <img alt="Version" src="https://img.shields.io/badge/version-1.0.0-7B2FFF?style=for-the-badge">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-00E5FF?style=for-the-badge">
  <img alt="Framework" src="https://img.shields.io/badge/.NET-10_LTS-512BD4?style=for-the-badge">
  <img alt="Tests" src="https://img.shields.io/badge/tests-93_passing-00C853?style=for-the-badge">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-26A69A?style=for-the-badge">
  <img alt="CI" src="https://img.shields.io/github/actions/workflow/status/ajjs1ajjs/SafeDisk-Cleaner/ci.yml?style=for-the-badge&label=CI">
</p>

> **v1.0.0** — стабільний реліз на **WPF + .NET 10 LTS** (раніше: Tauri / Rust + React).

## 🚀 Основний функціонал

- **Scanner Engine**: багатопотоковий обхід файлової системи та пошук безпечних кандидатів на видалення (Temp, Crash Dumps, Browser Cache, Logs, Package Cache, Windows Update cache, Windows.old).
- **Confidence System**: кожен файл отримує рейтинг безпеки `0-100%` із поясненням причини.
- **Safety Engine**: ніколи не чіпає системні шляхи, захищені розширення та файли, що використовуються; перевіряє цифрові підписи Microsoft для Advanced-категорій.
- **Режими роботи**: Analyze / Interactive / Auto / Dry Run — від аналізу до повністю автоматичного очищення.
- **Recovery System**: малі файли переміщуються в Кошик, великі — у Карантин із терміном зберігання, відновленням та журналом.
- **Audit Log**: повний журнал усіх дій (EF Core + SQLite).
- **Дублікати**: пошук копій за BLAKE3-хешем.
- **WPF UI** (Material Design 3, тема Neon Clean) та **CLI**.
- **Автооновлення**: перевірка GitHub-релізів при запуску та в один клік.

## 🔒 Основні принципи безпеки

Програма **ніколи** не видаляє:
- Windows, Program Files, ProgramData, System32, Drivers, EFI, Recovery, Boot
- файли з розширеннями `.dll .sys .exe .cat .inf .msi .msp`
- файли з атрибутом SYSTEM, відкриті процесами або використані за останні N днів
- файли з цифровим підписом Microsoft (Advanced категорії)
- пакети драйверів у Windows DriverStore (навіть якщо файл схожий на кеш)

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
├── Directory.Build.props       # спільна версія 1.0.0
├── src/
│   ├── SafeDiskCleaner.Core/           # домен: моделі, rules, confidence, safety, scanner, Windows interop
│   ├── SafeDiskCleaner.Infrastructure/ # EF Core, Refit+Polly, Serilog, сервіси, DI
│   ├── SafeDiskCleaner.App/            # WPF UI (MaterialDesign, MVVM) — портативна збірка
│   └── SafeDiskCleaner.Cli/            # консольний застосунок
└── tests/
    └── SafeDiskCleaner.Tests/          # xUnit + FluentAssertions + Moq (93 тести, включно з інтеграційними на SQLite)
```

## 📝 Ліцензія

MIT © [ajjs1ajjs](https://github.com/ajjs1ajjs) — див. [LICENSE](LICENSE).

## 🗺️ Дорожня карта (після 1.0)

- Інсталятор (MSI) та автозапуск при старті Windows
- Планувальник автоматичного очищення
- Підписка на події та сповіщення про завершення
- Локалізація (EN/UA/PL)
