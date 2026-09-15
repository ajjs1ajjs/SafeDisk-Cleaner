<div align="center">

# SafeDisk Cleaner — Source Code

[![Deployed to](https://img.shields.io/badge/Deployed_to-SafeDisk--Cleaner-blue)](https://github.com/ajjs1ajjs/SafeDisk-Cleaner)
[![Website](https://img.shields.io/badge/Website-ajjs1ajjs.github.io%2FSafeDisk--Cleaner-green)]
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![CI](https://img.shields.io/github/actions/workflow/status/ajjs1ajjs/SafeDisk-Cleaner/ci.yml?label=CI)](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/actions)

> **Це репозиторій з вихідним кодом SafeDisk Cleaner disk analysis tool.**
> Готовий продукт деплоїться в: **https://github.com/ajjs1ajjs/SafeDisk-Cleaner**
> Офіційний сайт: **https://ajjs1ajjs.github.io/SafeDisk-Cleaner/**

# 🛡️ SafeDisk Cleaner

**Безпечний аналіз, очищення та пошук дублікатів для Windows, Linux та macOS (Intel + Apple Silicon)**

<img src="docs/banner.svg" width="100%" alt="SafeDisk Cleaner">

[![Release](https://img.shields.io/github/v/release/ajjs1ajjs/SafeDisk-Cleaner?label=release&color=7B2FFF)](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/ajjs1ajjs/SafeDisk-Cleaner/total?label=downloads&color=00E5FF)](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases)
[![CI](https://img.shields.io/github/actions/workflow/status/ajjs1ajjs/SafeDisk-Cleaner/ci.yml?label=CI)](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/actions)
[![Tests](https://img.shields.io/badge/tests-226%20passing-00C853)](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/actions)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS%20(Intel%20%2B%20ARM)-00E5FF)]()
[![.NET](https://img.shields.io/badge/.NET-10%20LTS-512BD4)]()
[![License: MIT](https://img.shields.io/badge/license-MIT-26A69A)](LICENSE)

**WPF · MVVM · Material Design 3 · .NET 10 LTS** — захищає ваші дані, дає повний контроль над кожним кроком очищення.

<a href="https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest"><img src="https://img.shields.io/badge/Download-latest-00A0C6"></a>

</div>
---

## ✨ Чому SafeDisk Cleaner?

Інші «чистильники» видаляють файли наосліп. SafeDisk Cleaner будує **рейтинг безпеки для кожного файлу** та **ніколи не чіпає те, що може зламати систему**.

| | |
|---|---|
| 🧠 **Confidence System** | Кожен файл отримує оцінку `0–100%` із поясненням, чому його можна (чи не можна) видаляти. |
| 🛡️ **Safety Engine** | Не чіпає системні шляхи, захищені розширення, файли в роботі та підписи Microsoft. |
| 🔍 **Прозорість** | Ви бачите весь список, категорії, розміри, ризики та причини — **до** будь-якого видалення. |
| ♻️ **Recovery System** | Малі файли → Кошик, великі → Карантин з відновленням і журналом. |
| 📋 **Audit Log** | Повний журнал кожного очищення (SQLite). |
| 🎨 **Неоновий інтерфейс** | 2 теми × 4 акценти, темна та світла — перемикається миттєво. |

---

## 📸 Інтерфейс

<div align="center">

**Огляд (Dashboard)**

<img src="docs/screenshots/dashboard.png" width="620" alt="SafeDisk Cleaner — Огляд">

| | |
|---|---|
| <img src="docs/screenshots/scan.png" width="440" alt="Сканування"> | <img src="docs/screenshots/duplicates.png" width="440" alt="Дублікати"> |
| **Сканування** — фільтри, ризики, експорт | **Дублікати** — BLAKE3-хеші, keep-one-per-group |
| <img src="docs/screenshots/quarantine.png" width="440" alt="Карантин"> | <img src="docs/screenshots/settings.png" width="440" alt="Налаштування"> |
| **Карантин** — відновлення та очищення | **Налаштування** — тема та акцент |

</div>

---

## 🚀 Можливості

- **Scanner Engine** — багатопотоковий обхід і пошук сміття: Temp, Crash Dumps, Browser Cache, Logs, Package Cache, Windows Update cache, Windows.old, **Delivery Optimization**, **Error Reporting**, **Prefetch** та інші.
- **Пошук дублікатів** за BLAKE3-хешем; «Вибрати все» завжди залишає **одну найновішу копію** кожної групи.
- **4 режими очищення**: Analyze, Interactive, Auto, Dry Run.
- **Фільтри кандидатів** — пошук за шляхом, категорія, рівень ризику, «Тільки безпечні».
- **Експорт звіту** в CSV/JSON.
- **Очищення кошика Windows** одним кліком.
- **Автооновлення** з GitHub-релізів в один клік.
- **CLI** для сценаріїв автоматизації.

---

## 🔒 Основні принципи безпеки

Програма **ніколи** не видаляє:

- 📁 `Windows`, `Program Files`, `ProgramData`, `System32`, `Drivers`, `EFI`, `Recovery`, `Boot`
- 📄 файли з розширеннями `.dll .sys .exe .cat .inf .msi .msp`
- 🔐 файли з атрибутом `SYSTEM`, зайняті іншими процесами або використані за останні N днів
- ✍️ файли з цифровим підписом Microsoft (Advanced-категорії)
- 🚫 пакети драйверів у Windows DriverStore — навіть якщо файл схожий на кеш

> Помилка під час очищення ніколи не перериває весь процес: невдалий файл логується в Audit, решта обробляється. Будь-яке очищення можна скасувати.

---

## 📥 Встановлення

### 🪟 Windows

**Системні вимоги:**
- Windows 10 або Windows 11 (x64)
- .NET 10 Desktop Runtime (не потрібен для готових збірок; потрібен лише для розробки)

**Встановлення:**
1. Завантажте `SafeDiskCleaner-<ver>-setup-win64.exe` з [сторінки релізів](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest).
2. Запустіть інсталятор — він встановить програму в `%LOCALAPPDATA%\Programs\SafeDisk Cleaner`, створить ярлик у меню «Пуск» та зареєструє видалення через «Програми та компоненти».
3. Після встановлення запустіть «SafeDisk Cleaner» з меню «Пуск».

**Альтернативно (портативний режим):**
1. Завантажте `SafeDiskCleaner-<ver>-portable-win64.exe`.
2. Розмістіть його у зручному місці.
3. Запустіть напряму — дані зберігатимуться в `%LOCALAPPDATA%\SafeDisk`.

**Запуск з джерела (розробка):**
```powershell
git clone https://github.com/ajjs1ajjs/SafeDisk-Cleaner.git
cd SafeDisk-Cleaner
dotnet restore
dotnet run --project src/SafeDiskCleaner.App
```

**Базова перевірка:**
```powershell
dotnet test
```

---

### 🍎 macOS (Apple Silicon M1+)

**Системні вимоги:**
- macOS 13 (Ventura) або новіша
- Apple Silicon: M1, M2, M3, M4 та новіші (ARM64 / aarch64)
- Rosetta 2 **не потрібен** — збірка нативна для ARM64

**Встановлення:**
1. Завантажте `SafeDiskCleaner-<ver>-macos-arm64.tar.gz` з [сторінки релізів](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest).
2. Розпакуйте архів:
   ```bash
   tar -xzf SafeDiskCleaner-<ver>-macos-arm64.tar.gz
   ```
3. Перемістіть `SafeDiskCleaner` у `/Applications` або іншу зручну теку.
4. При першому запуску macOS може попередити про невідомий розробника — клацніть «Open» у системних налаштуваннях або виконайте:
   ```bash
   xattr -cr /Applications/SafeDiskCleaner
   ```

**Запуск з джерела (розробка):**
```bash
git clone https://github.com/ajjs1ajjs/SafeDisk-Cleaner.git
cd SafeDisk-Cleaner
dotnet restore
dotnet run --project src/SafeDiskCleaner.Avalonia
```

**Збірка для macOS (ARM64):**
```bash
chmod +x scripts/build-macos.sh
./scripts/build-macos.sh 1.7.4
```

**Базова перевірка:**
```bash
dotnet test
```

---

### 🐧 Linux (Ubuntu/Debian)

**Системні вимоги:**
- Ubuntu 22.04+ / Debian 12+ (x64)
- Готові збірки самодостатні, runtime не потрібен

**Встановлення:**
1. Завантажте `SafeDiskCleaner-<ver>-linux-x64.tar.gz` з [сторінки релізів](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest).
2. Розпакуйте і запустіть:
   ```bash
   tar -xzf SafeDiskCleaner-<ver>-linux-x64.tar.gz
   ./SafeDiskCleaner
   ```

---

### 🍎 macOS (Intel x64)

**Системні вимоги:**
- macOS 13 (Ventura) або новіша, Intel x64

**Встановлення:**
1. Завантажте `SafeDiskCleaner-<ver>-macos-x64.tar.gz` з [сторінки релізів](https://github.com/ajjs1ajjs/SafeDisk-Cleaner/releases/latest).
2. Розпакуйте:
   ```bash
   tar -xzf SafeDiskCleaner-<ver>-macos-x64.tar.gz
   ```
3. Перемістіть `SafeDiskCleaner` у `/Applications`. При першому запуску за потреби виконайте `xattr -cr /Applications/SafeDiskCleaner`.

---

## 💻 Розробка

### Системні вимоги

- **Windows 10/11**, **Ubuntu 22.04+/Debian 12+**, **macOS 13+** (Intel + Apple Silicon)
- **.NET 10 SDK** (для розробки; готові збірки не потребують встановленого runtime)

### Збірка та запуск

**Windows (WPF UI):**
```powershell
dotnet restore
dotnet run --project src/SafeDiskCleaner.App
```

**macOS (Avalonia UI):**
```bash
dotnet restore
dotnet run --project src/SafeDiskCleaner.Avalonia
```

**Тести (обидві платформи):**
```bash
dotnet test
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

### Реліз

Тег `v*` запускає CI: тести → Windows-інсталятор (Inno Setup) + збірки Linux/macOS (Avalonia) → автоматичне опублікування релізу.

```bash
git tag v1.7.4 && git push origin v1.7.4
```

---

## 🛠️ Стек

| Шар | Технологія |
|-----|------------|
| Платформа | **WPF (Windows)** · **Avalonia (Linux/macOS Intel/ARM64)** · **.NET 10 LTS** |
| UI | MaterialDesignInXamlToolkit (Material Design 3), темна/світла тема + 4 акценти |
| Архітектура | MVVM (CommunityToolkit.Mvvm), Dependency Injection, Generic Host |
| Дані | Entity Framework Core + SQLite |
| HTTP | HttpClientFactory + Refit + Polly (Retry/Timeout/Circuit Breaker) |
| Логування | Serilog (async sink, rolling file) |
| Валідація | FluentValidation |
| Хешування | BLAKE3 |
| Тести | xUnit + FluentAssertions + Moq |
| Релізи | GitHub Actions + Inno Setup |

---

## 📁 Де зберігаються дані?

| Що | Windows | Linux | macOS |
|----|---------|-------|-------|
| SQLite база (audit, карантин) | `C:\ProgramData\SafeDisk\SafeDisk.db` | `~/.local/share/SafeDisk/SafeDisk.db` | `~/Library/Application Support/SafeDisk/SafeDisk.db` |
| Карантин | `C:\ProgramData\SafeDisk\quarantine\` | `~/.local/share/SafeDisk/quarantine/` | `~/Library/Application Support/SafeDisk/quarantine/` |
| Звіти | `C:\ProgramData\SafeDisk\reports\` | `~/.local/share/SafeDisk/reports/` | `~/Library/Application Support/SafeDisk/reports/` |
| Логи (Serilog) | `C:\ProgramData\SafeDisk\logs\` | `~/.local/share/SafeDisk/logs/` | `~/Library/Application Support/SafeDisk/logs/` |
| Налаштування | `C:\ProgramData\SafeDisk\settings.json` | `~/.local/share/SafeDisk/settings.json` | `~/Library/Application Support/SafeDisk/settings.json` |

> Якщо головна тека недоступна — використовується `%LOCALAPPDATA%\SafeDisk` (Windows) або `~/.local/share/SafeDisk` (Linux/macOS fallback).

---

## 📦 Структура проєкту

```
SafeDiskCleaner.slnx
├── Directory.Build.props            # спільна версія
├── src/
│   ├── SafeDiskCleaner.Core/        # домен: моделі, rules, confidence, safety, scanner, platform interop
│   ├── SafeDiskCleaner.Infrastructure/  # EF Core, Refit+Polly, Serilog, сервіси, DI
│   ├── SafeDiskCleaner.ViewModels/  # спільні ViewModels для WPF і Avalonia
│   ├── SafeDiskCleaner.App/         # WPF UI (Windows)
│   ├── SafeDiskCleaner.Avalonia/    # Avalonia UI (Linux/macOS)
│   └── SafeDiskCleaner.Cli/         # консольний застосунок
├── scripts/
│   ├── build-release.ps1            # Windows-збірка (Inno Setup)
│   ├── build-macos.sh               # macOS ARM64-збірка
│   └── installer.iss                # Inno Setup-сценарій
└── tests/
    └── SafeDiskCleaner.Tests/       # xUnit + FluentAssertions + Moq
```

---

## 🗺️ Дорожня карта

- [ ] Автозапуск при старті Windows
- [ ] Планувальник автоматичного очищення
- [ ] Сповіщення про завершення
- [ ] Локалізація (EN/UA/PL)

---

<div align="center">

**SafeDisk Cleaner** — MIT © [ajjs1ajjs](https://github.com/ajjs1ajjs)

⭐ Сподобалось? Поставте зірочку!

</div>
