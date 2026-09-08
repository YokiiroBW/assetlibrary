import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from "react";
import type { ImageRequests, ImageState } from "./imageRequests";
import type { ImageVariant } from "./imageResponses";
import type { EntryDetail } from "./types";
import { WorkspaceIcon } from "./WorkspaceIcon";

export function DerivedImage({
  value,
  requests,
  variant,
  enabled,
  scrollRoot,
}: {
  value: EntryDetail;
  requests: ImageRequests;
  variant: ImageVariant;
  enabled: boolean;
  scrollRoot?: RefObject<HTMLDivElement | null>;
}) {
  const element = useRef<HTMLSpanElement>(null);
  const release = useRef<(() => void) | null>(null);
  const [visible, setVisible] = useState(scrollRoot === undefined);
  const [retry, setRetry] = useState(0);
  const [result, setResult] = useState<{ key: string; requests: ImageRequests; state: ImageState } | null>(null);
  const key = `${value.library.library_id}:${value.entry.entry_id}:${variant}:${retry}`;
  const eligible = value.entry.kind === "file" && value.library.availability === "online";
  const active = visible && enabled && eligible;
  const state: ImageState =
    active && result?.key === key && result.requests === requests ? result.state : { status: "idle" };
  useEffect(() => {
    if (!scrollRoot || !element.current) return;
    const observer = new IntersectionObserver(([entry]) => setVisible(entry?.isIntersecting === true), {
      root: scrollRoot.current,
    });
    observer.observe(element.current);
    return () => observer.disconnect();
  }, [scrollRoot]);
  useLayoutEffect(() => {
    if (!active) return;
    const cancel = requests.acquire(value.library.library_id, value.entry.entry_id, variant, (next) =>
      setResult({ key, requests, state: next }),
    );
    release.current = cancel;
    return () => {
      cancel();
      release.current = null;
      setResult(null);
    };
  }, [active, requests, key, value.library.library_id, value.entry.entry_id, variant]);
  const fallback =
    value.library.availability === "offline"
      ? "资源库离线，图片暂不可用。"
      : value.entry.kind !== "file"
        ? "此条目仅提供文件信息。"
        : !enabled
          ? "图片已隐藏，正在等待连接核验。"
          : "正在读取图片…";
  return (
    <span
      ref={element}
      className={`derived-image ${variant === "thumbnail" ? "image-thumbnail" : "image-preview"}`}
      data-image-state={state.status}
    >
      {state.status === "ready" ? (
        <img
          src={state.url}
          width={state.width}
          height={state.height}
          alt={variant === "preview" ? value.entry.name : ""}
          draggable={false}
          onError={() => {
            release.current?.();
            setResult({
              key,
              requests,
              state: { status: "error", message: "图片无法显示，请重试。", statusCode: null },
            });
          }}
        />
      ) : (
        <span className="image-fallback" title={state.status === "error" ? state.message : fallback}>
          <WorkspaceIcon name={value.entry.kind === "directory" ? "folder" : "file"} />
          <span role={variant === "preview" ? "status" : undefined}>
            {state.status === "error"
              ? state.message
              : variant === "preview"
                ? fallback
                : state.status === "loading"
                  ? "载入中"
                  : ""}
          </span>
          {variant === "preview" && state.status === "error" && ![401, 403, 404].includes(state.statusCode ?? 0) && (
            <button className="secondary" onClick={() => setRetry((value) => value + 1)}>
              重试图片
            </button>
          )}
        </span>
      )}
    </span>
  );
}
