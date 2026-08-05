import { useCallback, useEffect, useMemo, useState } from "react";
import { listen } from "@tauri-apps/api/event";
import { open } from "@tauri-apps/plugin-dialog";
import * as api from "./api";
import {
  CATEGORY_LABELS,
  RISK_LABELS,
  humanSize,
} from "./types";
import type {
  AuditEntry,
  Candidate,
  CleanupResult,
  DriveInfo,
  QuarantineEntry,
  ScanProgress,
  ScanResult,
  UpdateInfo,
} from "./types";

export default function App() {
  const [drives, setDrives] = useState<DriveInfo[]>([]);
  const [dataRoot, setDataRoot] = useState("");
  const [update, setUpdate] = useState<UpdateInfo | null>(null);
  const [scanning, setScanning] = useState(false);
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState<ScanProgress | null>(null);
  const [scanResult, setScanResult] = useState<ScanResult | null>(null);
  const [dupeScanning, setDupeScanning] = useState(false);
  const [dupeResult, setDupeResult] = useState<ScanResult | null>(null);
  const [dupeSelected, setDupeSelected] = useState<Set<string>>(new Set());
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [cleanResult, setCleanResult] = useState<CleanupResult | null>(null);
  const [audit, setAudit] = useState<AuditEntry[]>([]);
  const [quarantine, setQuarantine] = useState<QuarantineEntry[]>([]);
  const [customRoots, setCustomRoots] = useState("");
  const [includeMedium, setIncludeMedium] = useState(false);
  const [includeAdvanced, setIncludeAdvanced] = useState(false);
  const [recencyDays, setRecencyDays] = useState(3);
  const [moveToRecycleBin, setMoveToRecycleBin] = useState(true);
  const [message, setMessage] = useState("");

  const refreshMisc = useCallback(async () => {
    try {
      setDrives(await api.listDrives());
      setDataRoot(await api.getDataRoot());
      setUpdate(await api.checkUpdate());
      setAudit(await api.getAuditLog());
      setQuarantine(await api.getQuarantine());
    } catch (e) {
      setMessage(`Помилка завантаження: ${e}`);
    }
  }, []);

  useEffect(() => {
    refreshMisc();
  }, [refreshMisc]);

  useEffect(() => {
    const unlisten = listen<ScanProgress>("scan-progress", (event) => {
      const p = event.payload;
      if (p.finished) {
        setProgress(null);
      } else {
        setProgress(p);
      }
    });
    return () => {
      unlisten.then((fn) => fn());
    };
  }, []);

  const doScan = async () => {
    setScanning(true);
    setScanResult(null);
    setProgress({ current_root: "", files_scanned: 0, dirs_scanned: 0, candidates_found: 0, percent: 0, finished: false });
    setMessage("");
    try {
      const roots = parseRoots(customRoots);
      const result = await api.scan(
        roots,
        includeMedium,
        includeAdvanced,
        40,
        recencyDays
      );
      setScanResult(result);
      setSelected(new Set());
    } catch (e) {
      setMessage(`Помилка сканування: ${e}`);
    } finally {
      setScanning(false);
      setProgress(null);
    }
  };

  const candidates = useMemo(
    () => scanResult?.candidates ?? [],
    [scanResult]
  );

  const selectable = useMemo(
    () => candidates.filter((c) => c.action !== "keep"),
    [candidates]
  );

  const allSelected = useMemo(
    () => selectable.length > 0 && selectable.every((c) => selected.has(c.path)),
    [selectable, selected]
  );

  const toggleAll = () => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (allSelected) {
        selectable.forEach((c) => next.delete(c.path));
      } else {
        selectable.forEach((c) => next.add(c.path));
      }
      return next;
    });
  };

  const toggleOne = (path: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(path)) {
        next.delete(path);
      } else {
        next.add(path);
      }
      return next;
    });
  };

  const selectedCandidates = useMemo(
    () => candidates.filter((c) => selected.has(c.path)),
    [candidates, selected]
  );

  const selectedSize = useMemo(
    () => selectedCandidates.reduce((s, c) => s + c.size, 0),
    [selectedCandidates]
  );

  const runClean = async (
    cands: Candidate[],
    mode: "dry-run" | "auto" | "interactive",
    after?: () => Promise<void>
  ) => {
    if (cands.length === 0) {
      setMessage("Нічого не вибрано.");
      return;
    }
    setBusy(true);
    setMessage("");
    try {
      const result = await api.cleanup(
        cands,
        mode,
        14,
        moveToRecycleBin,
        95
      );
      setCleanResult(result);
      setMessage(
        `Оброблено ${result.processed}, звільнено ${humanSize(result.freed_bytes)}.`
      );
      await refreshMisc();
      if (after) await after();
    } catch (e) {
      setMessage(`Помилка очищення: ${e}`);
    } finally {
      setBusy(false);
    }
  };

  const doClean = (mode: "dry-run" | "auto" | "interactive") =>
    runClean(selectedCandidates, mode, doScan);

  const doDupes = async () => {
    const roots = parseRoots(customRoots);
    if (roots.length === 0) {
      roots.push(...drives.map((d) => `${d.letter}\\`));
    }
    if (roots.length === 0) {
      setMessage("Вкажіть шляхи для аналізу дублікатів.");
      return;
    }
    setDupeScanning(true);
    setDupeResult(null);
    setDupeSelected(new Set());
    setMessage("");
    try {
      const result = await api.scanDuplicates(roots);
      setDupeResult(result);
    } catch (e) {
      setMessage(`Помилка сканування дублікатів: ${e}`);
    } finally {
      setDupeScanning(false);
    }
  };

  const dupeCandidates = useMemo(
    () => dupeResult?.candidates ?? [],
    [dupeResult]
  );

  const dupeAllSelected = useMemo(
    () =>
      dupeCandidates.length > 0 &&
      dupeCandidates.every((c) => dupeSelected.has(c.path)),
    [dupeCandidates, dupeSelected]
  );

  const toggleDupeAll = () => {
    setDupeSelected((prev) => {
      const next = new Set(prev);
      if (dupeAllSelected) {
        dupeCandidates.forEach((c) => next.delete(c.path));
      } else {
        dupeCandidates.forEach((c) => next.add(c.path));
      }
      return next;
    });
  };

  const toggleDupeOne = (path: string) => {
    setDupeSelected((prev) => {
      const next = new Set(prev);
      if (next.has(path)) {
        next.delete(path);
      } else {
        next.add(path);
      }
      return next;
    });
  };

  const dupeSelectedCandidates = useMemo(
    () => dupeCandidates.filter((c) => dupeSelected.has(c.path)),
    [dupeCandidates, dupeSelected]
  );

  const dupeSelectedSize = useMemo(
    () => dupeSelectedCandidates.reduce((s, c) => s + c.size, 0),
    [dupeSelectedCandidates]
  );

  const doCleanDupes = () =>
    runClean(dupeSelectedCandidates, "interactive", doDupes);

  const parseRoots = (s: string) =>
    s
      .split(/[,;\n]/)
      .map((x) => x.trim())
      .filter((x) => x.length > 0);

  const doPickFolder = async () => {
    try {
      const selected = await open({
        directory: true,
        multiple: true,
        title: "Виберіть папку або диск для аналізу",
      });
      if (!selected) return;
      const paths = Array.isArray(selected) ? selected : [selected];
      setCustomRoots((prev) => {
        const merged = new Set([...parseRoots(prev), ...paths]);
        return [...merged].join(", ");
      });
    } catch (e) {
      setMessage(`Помилка вибору: ${e}`);
    }
  };

  const isDriveSelected = (letter: string) => {
    const root = `${letter}\\`;
    return parseRoots(customRoots).some((r) => r === root || r === letter);
  };

  const toggleDrive = (letter: string) => {
    const root = `${letter}\\`;
    setCustomRoots((prev) => {
      const existing = parseRoots(prev);
      const has = existing.includes(root) || existing.includes(letter);
      const next = has
        ? existing.filter((r) => r !== root && r !== letter)
        : [...existing, root];
      return next.join(", ");
    });
  };

  const doEmptyRecycleBin = async () => {
    setBusy(true);
    try {
      await api.emptyRecycleBin();
      setMessage("Кошик очищено.");
      await refreshMisc();
      await doScan();
    } catch (e) {
      setMessage(`Помилка: ${e}`);
    } finally {
      setBusy(false);
    }
  };

  const quarantineActions = async (
    id: string,
    action: "restore" | "remove"
  ) => {
    try {
      if (action === "restore") {
        await api.restoreQuarantine(id);
      } else {
        await api.removeQuarantine(id);
      }
      setQuarantine(await api.getQuarantine());
    } catch (e) {
      setMessage(`Помилка: ${e}`);
    }
  };

  const doEmptyQuarantine = async () => {
    try {
      await api.emptyQuarantine();
      setQuarantine(await api.getQuarantine());
    } catch (e) {
      setMessage(`Помилка: ${e}`);
    }
  };

  const confidenceClass = (c: number) =>
    c >= 95 ? "conf-high" : c >= 80 ? "conf-mid" : c >= 50 ? "conf-low" : "conf-keep";

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="logo">🛡</span>
          <div>
            <h1>SafeDisk Cleaner</h1>
            <span className="subtitle">v{update?.current_version ?? ""}</span>
          </div>
        </div>
        <div className="topbar-right">
          {update?.available && (
            <a
              className="update-banner"
              href={update.download_url}
              target="_blank"
              rel="noreferrer"
            >
              Доступна версія {update.latest_version}
            </a>
          )}
          <span className="datadir" title={dataRoot}>
            {dataRoot}
          </span>
        </div>
      </header>

      <section className="scan-bar">
        <div className="drives">
          {drives.map((d) => (
            <div
              className={`drive${isDriveSelected(d.letter) ? " selected" : ""}`}
              key={d.letter}
              onClick={() => toggleDrive(d.letter)}
              title={
                isDriveSelected(d.letter)
                  ? "Прибрати диск з аналізу"
                  : "Додати диск до аналізу"
              }
            >
              <strong>{d.letter}</strong>
              <span>{d.kind}</span>
              <span>
                {humanSize(d.free)} / {humanSize(d.total)}
              </span>
            </div>
          ))}
        </div>
        <div className="scan-options">
          <input
            className="roots-input"
            placeholder="Додаткові шляхи для аналізу (через кому)..."
            value={customRoots}
            onChange={(e) => setCustomRoots(e.target.value)}
          />
          <button
            className="ghost"
            onClick={doPickFolder}
            disabled={scanning || dupeScanning || busy}
          >
            Обзор...
          </button>
          <label>
            <input
              type="checkbox"
              checked={includeMedium}
              onChange={(e) => setIncludeMedium(e.target.checked)}
            />
            Medium (Update cache, packages)
          </label>
          <label>
            <input
              type="checkbox"
              checked={includeAdvanced}
              onChange={(e) => setIncludeAdvanced(e.target.checked)}
            />
            Advanced (DriverStore)
          </label>
          <label>
            Не використано днів:{" "}
            <input
              type="number"
              min={0}
              max={90}
              value={recencyDays}
              onChange={(e) => setRecencyDays(Number(e.target.value))}
            />
          </label>
          <button
            className="secondary"
            onClick={doDupes}
            disabled={dupeScanning || scanning || busy}
          >
            {dupeScanning ? "Пошук..." : "Дублікати"}
          </button>
          <button className="primary" onClick={doScan} disabled={scanning || busy || dupeScanning}>
            {scanning ? "Сканування..." : "Аналізувати"}
          </button>
        </div>
      </section>

      {scanning && progress && !progress.finished && (
        <section className="progress">
          <div className="progress-bar">
            <div
              className="progress-fill"
              style={{ width: `${Math.min(100, Math.max(0, progress.percent))}%` }}
            />
          </div>
          <div className="progress-stats">
            <span>
              {progress.files_scanned.toLocaleString()} файлів ·{" "}
              {progress.dirs_scanned.toLocaleString()} папок
            </span>
            <span>{progress.candidates_found} кандидатів</span>
            <span className="progress-percent">
              {Math.round(progress.percent)}%
            </span>
          </div>
          {progress.current_root && (
            <div className="progress-root" title={progress.current_root}>
              {progress.current_root}
            </div>
          )}
        </section>
      )}

      {message && <div className="message">{message}</div>}

      {scanResult && (
        <>
          <section className="summary">
            <div className="summary-grid">
              <div className="stat">
                <span className="stat-value">{humanSize(scanResult.summary.total_potential)}</span>
                <span className="stat-label">Потенційно звільниться</span>
              </div>
              <div className="stat">
                <span className="stat-value">{scanResult.summary.scanned_files.toLocaleString()}</span>
                <span className="stat-label">Файлів проскановано</span>
              </div>
              <div className="stat">
                <span className="stat-value">{scanResult.candidates.length}</span>
                <span className="stat-label">Кандидатів на очищення</span>
              </div>
              <div className="stat">
                <span className="stat-value">{scanResult.summary.elapsed_ms} ms</span>
                <span className="stat-label">Час аналізу</span>
              </div>
            </div>
            <div className="categories">
              {scanResult.summary.categories.map((c) => (
                <div className={`cat-card risk-${c.risk_level}`} key={c.category}>
                  <div className="cat-name">
                    {CATEGORY_LABELS[c.category]}
                    <span className={`badge risk-${c.risk_level}`}>
                      {RISK_LABELS[c.risk_level]}
                    </span>
                  </div>
                  <div className="cat-size">{humanSize(c.potential)}</div>
                  <div className="cat-count">
                    {c.count} файлів ({humanSize(c.size)})
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="candidates">
            <div className="section-head">
              <h2>Кандидати</h2>
              <div className="actions">
                <label className="sel-count">
                  <input type="checkbox" checked={allSelected} onChange={toggleAll} />
                  {selected.size} вибрано · {humanSize(selectedSize)}
                </label>
                <button
                  className="ghost"
                  onClick={() => doClean("dry-run")}
                  disabled={busy || selectedCandidates.length === 0}
                >
                  Dry Run
                </button>
                <button
                  className="secondary"
                  onClick={() => doClean("auto")}
                  disabled={busy || selectedCandidates.length === 0}
                >
                  Авто-очищення
                </button>
                <button
                  className="danger"
                  onClick={() => doClean("interactive")}
                  disabled={busy || selectedCandidates.length === 0}
                >
                  Очистити вибране
                </button>
                <label className="tiny">
                  <input
                    type="checkbox"
                    checked={moveToRecycleBin}
                    onChange={(e) => setMoveToRecycleBin(e.target.checked)}
                  />
                  у кошик
                </label>
              </div>
            </div>

            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th />
                    <th>Шлях</th>
                    <th>Категорія</th>
                    <th>Розмір</th>
                    <th>Останній доступ</th>
                    <th>Confidence</th>
                    <th>Рекомендація</th>
                  </tr>
                </thead>
                <tbody>
                  {selectable.slice(0, 500).map((c) => (
                    <tr key={c.path}>
                      <td>
                        <input
                          type="checkbox"
                          checked={selected.has(c.path)}
                          onChange={() => toggleOne(c.path)}
                        />
                      </td>
                      <td className="path" title={c.path}>
                        {c.path}
                      </td>
                      <td>{CATEGORY_LABELS[c.category]}</td>
                      <td className="num">{humanSize(c.size)}</td>
                      <td className="num">
                        {c.last_access_days !== null && c.last_access_days !== undefined
                          ? `${c.last_access_days} дн.`
                          : "—"}
                      </td>
                      <td className="num">
                        <span className={`conf ${confidenceClass(c.confidence)}`}>
                          {c.confidence}%
                        </span>
                      </td>
                      <td className="reason" title={c.reason}>
                        {c.reason}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {selectable.length > 500 && (
              <div className="hint">Показано перші 500 з {selectable.length} кандидатів.</div>
            )}
          </section>
        </>
      )}

      {dupeResult && (
        <section className="candidates">
          <div className="section-head">
            <h2>Дублікати файлів</h2>
            <div className="actions">
              <label className="sel-count">
                <input type="checkbox" checked={dupeAllSelected} onChange={toggleDupeAll} />
                {dupeSelected.size} вибрано · {humanSize(dupeSelectedSize)}
              </label>
              <button
                className="danger"
                onClick={doCleanDupes}
                disabled={busy || dupeSelectedCandidates.length === 0}
              >
                Очистити вибрані
              </button>
            </div>
          </div>
          {dupeCandidates.length === 0 ? (
            <div className="empty">Дублікатів не знайдено.</div>
          ) : (
            <>
              <div className="hint">
                Показано лише копії — оригінали зберігаються. Очищення виконується у режимі перегляду.
              </div>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th />
                      <th>Шлях</th>
                      <th>Розмір</th>
                      <th>Останній доступ</th>
                      <th>Confidence</th>
                      <th>Оригінал</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dupeCandidates.slice(0, 500).map((c) => (
                      <tr key={c.path}>
                        <td>
                          <input
                            type="checkbox"
                            checked={dupeSelected.has(c.path)}
                            onChange={() => toggleDupeOne(c.path)}
                          />
                        </td>
                        <td className="path" title={c.path}>
                          {c.path}
                        </td>
                        <td className="num">{humanSize(c.size)}</td>
                        <td className="num">
                          {c.last_access_days !== null && c.last_access_days !== undefined
                            ? `${c.last_access_days} дн.`
                            : "—"}
                        </td>
                        <td className="num">
                          <span className={`conf ${confidenceClass(c.confidence)}`}>
                            {c.confidence}%
                          </span>
                        </td>
                        <td className="reason" title={c.reason}>
                          {c.reason}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {dupeCandidates.length > 500 && (
                <div className="hint">
                  Показано перші 500 з {dupeCandidates.length} кандидатів.
                </div>
              )}
            </>
          )}
        </section>
      )}

      {cleanResult && (
        <section className="clean-result">
          <h2>Результат очищення</h2>
          <div className="summary-grid">
            <div className="stat">
              <span className="stat-value">{humanSize(cleanResult.freed_bytes)}</span>
              <span className="stat-label">Звільнено</span>
            </div>
            <div className="stat">
              <span className="stat-value">{cleanResult.deleted}</span>
              <span className="stat-label">Видалено/переміщено</span>
            </div>
            <div className="stat">
              <span className="stat-value">{cleanResult.processed}</span>
              <span className="stat-label">Оброблено</span>
            </div>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Статус</th>
                  <th>Шлях</th>
                  <th>Розмір</th>
                  <th>Деталі</th>
                </tr>
              </thead>
              <tbody>
                {cleanResult.entries.map((e, i) => (
                  <tr key={i}>
                    <td className={`status-${e.status}`}>{e.status}</td>
                    <td className="path" title={e.path}>
                      {e.path}
                    </td>
                    <td className="num">{humanSize(e.size)}</td>
                    <td className="reason">{e.detail}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      <section className="two-col">
        <div className="panel">
          <div className="section-head">
            <h2>Карантин</h2>
            <button className="ghost" onClick={doEmptyQuarantine} disabled={quarantine.length === 0}>
              Очистити карантин
            </button>
          </div>
          {quarantine.length === 0 ? (
            <div className="empty">Порожньо</div>
          ) : (
            <table className="panel-table">
              <thead>
                <tr>
                  <th>Файл</th>
                  <th>Розмір</th>
                  <th>Дата</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {quarantine.map((q) => (
                  <tr key={q.id}>
                    <td className="path" title={q.original_path}>
                      {q.original_path}
                    </td>
                    <td className="num">{humanSize(q.size)}</td>
                    <td>{q.quarantined_at}</td>
                    <td className="row-actions">
                      <button className="mini" onClick={() => quarantineActions(q.id, "restore")}>
                        Відновити
                      </button>
                      <button className="mini danger" onClick={() => quarantineActions(q.id, "remove")}>
                        Видалити
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <div className="panel">
          <div className="section-head">
            <h2>Audit Log</h2>
            <button className="ghost" onClick={api.clearAuditLog}>
              Очистити лог
            </button>
          </div>
          {audit.length === 0 ? (
            <div className="empty">Записів немає</div>
          ) : (
            <div className="audit-list">
              {audit
                .slice()
                .reverse()
                .slice(0, 100)
                .map((e, i) => (
                  <div key={i} className={`audit-row ${e.success ? "ok" : "fail"}`}>
                    <span className="audit-date">{e.date}</span>
                    <span className="audit-action">{e.action}</span>
                    <span className="audit-size">{humanSize(e.size)}</span>
                    <span className="audit-path" title={e.path}>
                      {e.path}
                    </span>
                  </div>
                ))}
            </div>
          )}
        </div>
      </section>

      <footer className="footer">
        <button className="ghost" onClick={doEmptyRecycleBin} disabled={busy}>
          Очистити Кошик
        </button>
        <span>
          SafeDisk Cleaner — безпечний аналіз і очищення дисків Windows.
        </span>
      </footer>
    </div>
  );
}
