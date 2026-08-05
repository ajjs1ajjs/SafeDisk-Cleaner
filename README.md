# SafeDisk Cleaner

Безпечний інструмент для аналізу та очищення дисків Windows із пріоритетом на безпеку даних, прозорість рішень та повний контроль користувача.

## 🚀 Основний функціонал

- **Scanner Engine**: багатопотоковий обхід файлової системи та пошук безпечних кандидатів на видалення (Temp, Crash Dumps, Browser Cache, Logs, Package Cache, Windows Update cache).
- **Confidence System**: кожен файл отримує рейтинг безпеки `0-100%` із поясненням причини.
- **Safety Engine**: ніколи не чіпає системні шляхи, захищені розширення та файли, що використовуються.
- **Режими роботи**: Analyze / Interactive / Auto / Dry Run.
- **Recovery System**: малі файли переміщуються в Кошик, великі — у Карантин (`C:\ProgramData\SafeDisk\Quarantine`) із терміном зберігання.
- **Audit Log**: повний журнал усіх дій (JSONL).
- **Tauri UI** (React + TypeScript) та CLI.

## 🔒 Основні принципи безпеки

Програма **ніколи** не видаляє:
- Windows, Program Files, ProgramData, System32, Drivers, EFI, Recovery, Boot
- файли з розширеннями `.dll .sys .exe .cat .inf .msi .msp`
- файли з атрибутом SYSTEM, відкриті процесами або використані за останні N днів
- файли з цифровим підписом Microsoft (Advanced категорії)

## 🛠️ Стек технологій

- **Core**: Tauri v2, Rust Stable, rayon, walkdir
- **Windows Integration**: windows-sys (SHFileOperation, Recycle Bin, диски)
- **Frontend**: React (TypeScript), Vite
- **Дані**: JSON-звіти, JSONL audit log, quarantine manifest

## 💻 Розробка та запуск (Onboarding)

### Системні вимоги

- **Node.js** (v20+)
- **Rust & Cargo** (v1.75+)
- **C++ Build Tools** (MSVC)

### Перший запуск проєкту

```bash
# 1. Встановіть залежності
npm install

# 2. Запуск у режимі розробки (Tauri + Vite)
npm run tauri dev
```

### CLI (без UI)

```bash
npm run tauri dev -- --  # не потрібно — використовуйте збірку CLI:

# Debug-збірка CLI (запуск із src-tauri)
cargo run -- analyze
cargo run -- clean --dry-run
cargo run -- clean --auto
cargo run -- drives
cargo run -- audit
cargo run -- quarantine list
```

### Збірка релізної версії

```bash
# Інсталятори (NSIS + MSI)
npm run build:installer

# Portable ZIP
npm run build:portable

# Все одразу: NSIS + MSI + Portable, копіювання в BUILD\
npm run build:all
```

Готові файли знаходяться у `src-tauri/target/release/bundle/`, портативна збірка — у `dist-portable/` та `BUILD/`.

### Портативна версія та WebView2

Портативна збірка **вбудовує фіксовану версію WebView2 Runtime** поруч з `safedisk-cleaner.exe`, тому працює навіть на машинах, де runtime не встановлено системно. Під час збірки:

1. Скрипт автоматично визначає найновішу доступну фіксовану версію WebView2 (x64) з сайту Microsoft і завантажує її (`~300 МБ`, кешується у `src-tauri/.webview2-runtime/`).
2. Розпакована папка runtime (наприклад, `Microsoft.WebView2.FixedVersionRuntime.*.x64`) копіюється в портативний каталог.
3. При запуску програма виявляє папку runtime поруч з exe і використовує її (через змінну середовища `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER`).

Поведінка:

- Якщо поруч з `safedisk-cleaner.exe` є папка `Microsoft.WebView2.FixedVersionRuntime.*` — вбудований runtime буде використано.
- В інших випадках (інстальована версія) — використовується системний WebView2 Runtime.

Параметри скрипта:

```powershell
# Використати конкретну версію runtime (замість авто-визначення)
npm run build:portable -- --  # (передати аргументи можна напряму через powershell)
powershell -ExecutionPolicy Bypass -File scripts\build-portable.ps1 -CabUrl "https://.../Microsoft.WebView2.FixedVersionRuntime.XXX.x64.cab"

# Зібрати без вбудовування runtime (потребує системного WebView2)
powershell -ExecutionPolicy Bypass -File scripts\build-portable.ps1 -SkipWebView2
```

### Механіка релізів

Версія береться з `src-tauri/tauri.conf.json` (єдине джерело істини).

```powershell
# Повний реліз: build → push → tag v<version> → GitHub Release з нотами з CHANGELOG.md
.\scripts\release.ps1

# Або з власними нотами
.\scripts\release.ps1 -ReleaseNotes "Текст нотаток"
```

Перед релізом:
1. Оновіть версію у `src-tauri/tauri.conf.json` (і `package.json`).
2. Додайте запис у верх `CHANGELOG.md`.
3. Закомітьте всі зміни (скрипт вимагає чистий `git status`).
4. Запустіть `.\scripts\release.ps1`.

Авто-оновлення в додатку перевіряє останній реліз через GitHub API.

## 📁 Де зберігаються дані?

- **Audit Log**: `C:\ProgramData\SafeDisk\audit\audit.log.jsonl`
- **Карантин**: `C:\ProgramData\SafeDisk\Quarantine\`
- **Звіти**: `C:\ProgramData\SafeDisk\reports\`

Якщо `C:\ProgramData` недоступний для запису — використовується `%LOCALAPPDATA%\SafeDisk`.

## 📦 Структура проєкту

```
src-tauri/src/
├── lib.rs          # Tauri команди та точка входу
├── main.rs         # CLI entry
├── cli.rs          # CLI: analyze/clean/drives/audit/quarantine
├── models.rs       # Структури даних (Candidate, ScanResult, AuditEntry...)
├── scanner.rs      # Scanner Engine (walkdir + rayon)
├── rules.rs        # Rules Engine (класифікація файлів)
├── confidence.rs   # Confidence System
├── safety.rs       # Safety Engine (валідація перед видаленням)
├── cleanup.rs      # Cleanup Engine (pipeline)
├── audit.rs        # Audit Log (JSONL)
├── quarantine.rs   # Recovery System (карантин)
├── paths.rs        # Каталоги даних
├── windows_utils.rs# Windows API (Recycle Bin, диски, атрибути)
└── update.rs       # Перевірка оновлень (GitHub API)

src/                # React UI (TypeScript)
scripts/            # build/release PowerShell скрипти
```
