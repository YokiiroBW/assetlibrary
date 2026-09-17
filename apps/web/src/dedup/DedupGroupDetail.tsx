import { useEffect, useRef } from "react";
import { formatBytes, formatDate } from "../libraryMetadata";
import { WorkspaceIcon } from "../WorkspaceIcon";
import type { DedupGroup, DedupItem } from "./dedupTypes";

/**
 * Detail of one duplicate group: every member with the evidence that put it there. It is rendered
 * inside the list as a block, so opening it neither covers the list nor moves it, and its close button
 * returns focus to the row that opened it.
 */
export function DedupGroupDetail({
  group,
  loading,
  close,
}: {
  group: DedupGroup;
  loading: boolean;
  close: () => void;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
  }, []);
  return (
    <section className="dedup-detail" aria-labelledby="dedup-detail-heading">
      <div className="dedup-detail-heading">
        <h3 id="dedup-detail-heading" tabIndex={-1} ref={heading}>
          重复组详情
        </h3>
        <button type="button" className="icon-button" aria-label="关闭重复组详情" onClick={close}>
          <WorkspaceIcon name="close" />
        </button>
      </div>
      {loading ? (
        <p className="dedup-note" role="status">
          正在读取该组明细…
        </p>
      ) : (
        <>
          <dl className="dedup-facts">
            <div>
              <dt>文件数</dt>
              <dd>{group.member_count.toLocaleString("zh-CN")}</dd>
            </div>
            <div>
              <dt>单文件大小</dt>
              <dd>{formatBytes(String(group.length))}</dd>
            </div>
          </dl>
          <p className="dedup-note">
            组内文件的完整强哈希一致。这只能说明内容相同；是否属于同一资产、哪一份保留，仍需人工确认，本页不会替你决定。
          </p>
          <ul className="dedup-member-list">
            {group.members.map((member) => (
              <MemberRow key={`${member.source_id}:${member.relative_path}`} member={member} />
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

function MemberRow({ member }: { member: DedupItem }) {
  return (
    <li className="dedup-member">
      <div className="dedup-member-heading">
        <span className="dedup-item-name">{member.name}</span>
        <span className="dedup-item-size">{formatBytes(String(member.length))}</span>
      </div>
      <dl className="dedup-member-facts">
        <div>
          <dt>相对路径</dt>
          <dd>{member.relative_path}</dd>
        </div>
        <div>
          <dt>内容强哈希</dt>
          <dd className="dedup-hash">{member.sha256 ?? "本次未计算"}</dd>
        </div>
        <div>
          <dt>修改时间</dt>
          <dd>{formatDate(member.last_write_time_utc)}</dd>
        </div>
        <div>
          <dt>读取状态</dt>
          <dd>{member.read_state_text}</dd>
        </div>
      </dl>
      {member.relation_notes.length > 0 && <p className="dedup-note">{member.relation_notes.join("；")}</p>}
    </li>
  );
}
