# Changelog

## v0.1.0

- Initial release: SafeDisk Cleaner MVP
- Scanner Engine: багатопотоковий обхід файлової системи (Temp, Crash Dumps, Browser Cache, Logs, Package Cache, Thumbnail Cache, Old Windows Install, Windows Update cache)
- Пошук дублікатів файлів (BLAKE3) з підтримкою в UI та CLI (`duplicates`)
- Реальний час сканування: прогрес по кожному кореню через Tauri events
- Confidence System: рейтинг безпеки кожного файлу (95-100 Safe / 80-94 Probably Safe / 50-79 Needs Review / <50 Do Not Touch)
- Safety Engine: захист системних шляхів, розширень (.dll/.sys/.exe/.cat/.inf/.msi/.msp), атрибутів, заблокованих файлів, рецентності використання
- Cleanup Engine: pipeline Find → Validate → Safety Check → Confirmation → Delete → Report
- Режими роботи: Analyze, Interactive, Auto, Dry Run
- Recovery System: малі файли → Кошик, великі файли → Карантин (C:\ProgramData\SafeDisk\Quarantine)
- Audit Log: повний журнал дій у JSONL
- Recycle Bin: підрахунок та очищення через Windows API (SHFileOperation)
- Tauri UI (React + TypeScript): аналіз, категорії, кандидати, дублікати, карантин, audit log
- CLI: `analyze`, `clean`, `duplicates`, `drives`, `audit`, `quarantine`, `update`
- 46 unit tests, нуль compiler warnings
- Перевірка оновлень через GitHub API
