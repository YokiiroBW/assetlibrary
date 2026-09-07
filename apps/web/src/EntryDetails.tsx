import { accessLabel, categoryLabels, entryType, formatBytes, formatDate } from "./libraryMetadata";
import type { EntryDetail } from "./types";
import type { useEntry } from "./hooks/useResource";
import { EmptyState, ErrorState, LoadingState, Modal } from "./WorkspacePrimitives";
import { WorkspaceIcon } from "./WorkspaceIcon";
import { CopyRelativePaths } from "./CopyRelativePaths";

export function EntryDetails({
  detail,
  selectedRows,
  narrow,
  opened,
  close,
  locate,
  openDirectory,
}: {
  detail: ReturnType<typeof useEntry>;
  selectedRows: readonly EntryDetail[];
  narrow: boolean;
  opened: boolean;
  close: () => void;
  locate: (value: EntryDetail) => void;
  openDirectory: (value: EntryDetail) => void;
}) {
  const selectedCount = selectedRows.length;
  const content = (
    <>
      {detail.status === "loading" && <LoadingState label="正在读取条目信息" />}
      {detail.status === "error" && <ErrorState message={detail.message} retry={detail.reload} />}
      {detail.status === "idle" && (
        <EmptyState title={selectedCount > 1 ? `已选择 ${selectedCount} 项` : "选择一个条目"}>
          {selectedCount > 1
            ? "可复制所选条目的相对路径。跨资源库复制时会同时标注库名。"
            : "查看文件信息，或双击目录进入下一层。"}
        </EmptyState>
      )}
      {selectedCount > 1 && detail.status === "idle" && (
        <div className="multi-selection-action">
          <CopyRelativePaths rows={selectedRows} />
        </div>
      )}
      {detail.value && <Details value={detail.value} locate={locate} openDirectory={openDirectory} />}
    </>
  );
  if (narrow)
    return opened ? (
      <Modal label="资产详情" className="details-drawer" close={close}>
        {content}
      </Modal>
    ) : null;
  return (
    <aside className="detail-pane" aria-label="资产详情">
      <div className="detail-heading">
        <h2>资产详情</h2>
        {opened && (
          <button className="icon-button" aria-label="关闭资产详情" onClick={close}>
            <WorkspaceIcon name="close" />
          </button>
        )}
      </div>
      {content}
    </aside>
  );
}

function Details({
  value: { library, entry },
  locate,
  openDirectory,
}: {
  value: EntryDetail;
  locate: (value: EntryDetail) => void;
  openDirectory: (value: EntryDetail) => void;
}) {
  const value = { library, entry };
  return (
    <div className="details">
      <div className={`detail-icon ${entry.kind === "directory" ? "is-folder" : ""}`} aria-hidden="true">
        <WorkspaceIcon name={entry.kind === "directory" ? "folder" : "file"} />
        <span>{entryType(entry)}</span>
      </div>
      <span className="eyebrow">文件信息</span>
      <h3>{entry.name}</h3>
      <p className="detail-path">{entry.relative_path}</p>
      <div className="detail-actions">
        {entry.kind === "directory" && (
          <button className="primary" onClick={() => openDirectory(value)}>
            <WorkspaceIcon name="folder" />
            打开目录
          </button>
        )}
        <button className="secondary" onClick={() => locate(value)}>
          <WorkspaceIcon name="locate" />
          定位所在目录
        </button>
        <CopyRelativePaths rows={[value]} />
      </div>
      {library.availability === "offline" && (
        <p className="storage-notice">资源库离线，以下为上次成功扫描的索引信息。</p>
      )}
      <dl>
        <div>
          <dt>资源库</dt>
          <dd>{library.display_name}</dd>
        </div>
        <div>
          <dt>分类</dt>
          <dd>{categoryLabels[library.category]}</dd>
        </div>
        <div>
          <dt>类型</dt>
          <dd>{entryType(entry)}</dd>
        </div>
        <div>
          <dt>大小</dt>
          <dd>{formatBytes(entry.content_length)}</dd>
        </div>
        <div>
          <dt>修改时间</dt>
          <dd>{formatDate(entry.last_write_time_utc)}</dd>
        </div>
        <div>
          <dt>权限</dt>
          <dd>{accessLabel(library.access_level)}</dd>
        </div>
        <div>
          <dt>相对路径</dt>
          <dd>{entry.relative_path}</dd>
        </div>
      </dl>
      <p className="safety-note">当前仅显示索引中的文件信息，尚未提供内容预览。首次索引为固定快照，原文件保持不变。</p>
    </div>
  );
}
