import { categoryLabels, formatBytes } from "../libraryMetadata";
import type { DedupLibraryChoice, DedupLimits } from "./dedupTypes";

/**
 * Source selection and the budget of the run about to be started. The budget is stated before the
 * button is pressed, because hitting a ceiling is a normal outcome of a read-only scan and the reader
 * should know the ceiling in advance rather than discover it in the result.
 */
export function DedupAnalysisForm({
  choices,
  selectedId,
  limits,
  scanning,
  canStart,
  onSelect,
  onStart,
}: {
  choices: readonly DedupLibraryChoice[];
  selectedId: string | null;
  limits: DedupLimits;
  scanning: boolean;
  canStart: boolean;
  onSelect: (libraryId: string) => void;
  onStart: () => void;
}) {
  const selected = choices.find((choice) => choice.library_id === selectedId) ?? null;
  return (
    <section className="dedup-scope" aria-labelledby="dedup-scope-heading">
      <h2 id="dedup-scope-heading" className="dedup-section-heading">
        分析来源与预算
      </h2>
      <div className="dedup-scope-controls">
        <label htmlFor="dedup-library">已登记资源库</label>
        <select
          id="dedup-library"
          value={selectedId ?? ""}
          onChange={(event) => onSelect(event.target.value)}
          disabled={scanning}
        >
          <option value="">选择资源库</option>
          {choices.map((choice) => (
            <option key={choice.library_id} value={choice.library_id}>
              {choice.display_name} · {categoryLabels[choice.category]}
              {choice.availability === "offline" ? " · 离线" : ""}
            </option>
          ))}
        </select>
        <button type="button" className="primary" onClick={onStart} disabled={!canStart || scanning}>
          开始分析
        </button>
      </div>
      {selected !== null && selected.availability === "offline" && (
        <p className="dedup-warning" role="status">
          该资源库的存储当前不可访问。分析仍会启动，但读取失败会记为不可读而不是没有重复。
        </p>
      )}
      <dl className="dedup-budget">
        <div>
          <dt>文件数上限</dt>
          <dd>{limits.maximum_files.toLocaleString("zh-CN")}</dd>
        </div>
        <div>
          <dt>读取总量上限</dt>
          <dd>{formatBytes(String(limits.maximum_bytes))}</dd>
        </div>
        <div>
          <dt>单文件上限</dt>
          <dd>{formatBytes(String(limits.maximum_file_bytes))}</dd>
        </div>
        <div>
          <dt>并发读取</dt>
          <dd>{limits.hash_concurrency}</dd>
        </div>
      </dl>
      <p className="dedup-note">
        只做完整的强哈希比较，不会移动、复制或删除任何文件；达到上限或读取失败的部分会单独列出，不会算作没有重复。
      </p>
    </section>
  );
}
