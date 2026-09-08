import { DerivedImage } from "./DerivedImage";
import { EntryInformation } from "./EntryDetails";
import { ErrorState, LoadingState, Modal } from "./WorkspacePrimitives";
import type { ImageRequests } from "./imageRequests";
import type { useEntry } from "./hooks/useResource";
import type { EntryDetail } from "./types";

export function ImagePreview({
  detail,
  requests,
  enabled,
  quick,
  close,
  locate,
  openDirectory,
}: {
  detail: ReturnType<typeof useEntry>;
  requests: ImageRequests;
  enabled: boolean;
  quick: boolean;
  close: () => void;
  locate: (value: EntryDetail) => void;
  openDirectory: (value: EntryDetail) => void;
}) {
  return (
    <Modal label={quick ? "快速查看" : "图片预览"} className="image-preview-dialog" close={close}>
      {detail.status === "loading" && <LoadingState label="正在读取文件信息" />}
      {detail.status === "error" && <ErrorState message={detail.message} retry={detail.reload} />}
      {detail.value && (
        <>
          <h3 className="preview-name">{detail.value.entry.name}</h3>
          <DerivedImage value={detail.value} requests={requests} variant="preview" enabled={enabled} />
          <p className="preview-caption">显示服务端生成的图片，最长边 1600 像素。原文件保持不变。</p>
          <details className="preview-information" open={detail.value.entry.kind !== "file"}>
            <summary>文件信息</summary>
            <EntryInformation value={detail.value} locate={locate} openDirectory={openDirectory} />
          </details>
        </>
      )}
    </Modal>
  );
}
