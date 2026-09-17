import { formatBytes, formatDate } from "../libraryMetadata";
import type { DedupGroup, DedupItem } from "./dedupTypes";

/**
 * One duplicate group's summary row. The member count and the hash evidence are both stated, so a
 * group is never presented as a verdict: the reader decides whether the members are the same asset.
 */
function GroupRow({ group, selected, onOpen }: { group: DedupGroup; selected: boolean; onOpen: () => void }) {
  return (
    <button
      type="button"
      className="dedup-group-row"
      aria-expanded={selected}
      data-dedup-group={group.group_key}
      onClick={() => (selected ? undefined : onOpen())}
      disabled={selected}
    >
      <span className="dedup-group-count">{group.member_count} 个文件</span>
      <span className="dedup-group-size">{formatBytes(String(group.length))}</span>
      <span className="dedup-group-hash" title={group.evidence_hash}>
        强哈希一致 · {group.evidence_hash.slice(0, 16)}…
      </span>
      <span className="dedup-group-state">
        {group.identity_merge_proposed ? "同内容，建议人工确认是否为同一资产" : "同内容，身份待人工确认"}
      </span>
    </button>
  );
}

/**
 * Rows for the sections that list individual files. Each row repeats the reason it is listed, because
 * "not read" and "read failed" call for different follow-up from the reader.
 */
function ItemRow({ item }: { item: DedupItem }) {
  return (
    <li className="dedup-item-row">
      <span className="dedup-item-name" title={item.relative_path}>
        {item.name}
      </span>
      <span className="dedup-item-path">{item.relative_path}</span>
      <span className="dedup-item-size">{formatBytes(String(item.length))}</span>
      <span className="dedup-item-state">{item.read_state_text}</span>
      <span className="dedup-item-time">{formatDate(item.last_write_time_utc)}</span>
    </li>
  );
}

/**
 * The result list of the open section, with its own paging. Paging walks a server-signed cursor, so a
 * page cannot be read out of a different report version than the one it was issued for.
 */
export function DedupResultList({
  groups,
  items,
  kindLabel,
  kindNote,
  total,
  offset,
  nextCursor,
  truncated,
  selectedGroup,
  loadingMore,
  busy,
  onOpenGroup,
  onMore,
}: {
  groups: readonly DedupGroup[];
  items: readonly DedupItem[];
  kindLabel: string;
  kindNote: string;
  total: number;
  offset: number;
  nextCursor: string | null;
  truncated: boolean;
  selectedGroup: string | null;
  loadingMore: boolean;
  busy: boolean;
  onOpenGroup: (group: DedupGroup) => void;
  onMore: () => void;
}) {
  const shown = groups.length > 0 ? groups.length : items.length;
  return (
    <div className="dedup-results" role="region" aria-label={`${kindLabel}结果`}>
      <p className="dedup-note">{kindNote}</p>
      {shown === 0 ? (
        <div className="inline-state is-empty">
          <h3>本区块没有条目</h3>
          <p>本次分析中该区块为空。这不代表资源库没有重复文件：请同时查看“本次分析范围”和自己的读取失败项。</p>
        </div>
      ) : (
        <>
          <p className="dedup-result-count" role="status">
            共 {total.toLocaleString("zh-CN")} 条，当前从第 {(offset + 1).toLocaleString("zh-CN")} 条开始显示{" "}
            {shown.toLocaleString("zh-CN")} 条。{busy ? "正在读取更新的结果…" : ""}
          </p>
          {truncated && <p className="dedup-warning">结果条目超过保留上限，仅保存了最前面的一部分。</p>}
          {groups.length > 0 ? (
            <ul className="dedup-group-list">
              {groups.map((group) => (
                <li key={group.group_key}>
                  <GroupRow
                    group={group}
                    selected={selectedGroup === group.group_key}
                    onOpen={() => onOpenGroup(group)}
                  />
                </li>
              ))}
            </ul>
          ) : (
            <ul className="dedup-item-list">
              {items.map((item) => (
                <ItemRow key={`${item.source_id}:${item.relative_path}`} item={item} />
              ))}
            </ul>
          )}
        </>
      )}
      {nextCursor !== null && (
        <button type="button" className="secondary" onClick={onMore} disabled={loadingMore}>
          载入更多
        </button>
      )}
    </div>
  );
}
