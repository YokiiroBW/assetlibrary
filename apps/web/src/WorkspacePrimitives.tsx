import { useEffect, useRef, type ReactNode } from "react";
import { WorkspaceIcon } from "./WorkspaceIcon";
import { routeHref, type WorkspaceRoute } from "./workspaceRoutes";

export type Navigate = (route: WorkspaceRoute, replace?: boolean) => void;

export function WorkspaceLink({
  route,
  navigate,
  children,
  className = "",
  current = false,
  onFollow,
}: {
  route: WorkspaceRoute;
  navigate: Navigate;
  children: ReactNode;
  className?: string;
  current?: boolean;
  onFollow?: () => void;
}) {
  return (
    <a
      href={routeHref(route)}
      className={className}
      aria-current={current ? "page" : undefined}
      onClick={(event) => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        navigate(route);
        onFollow?.();
      }}
    >
      {children}
    </a>
  );
}

export function LoadingState({ label = "正在读取" }: { label?: string }) {
  return (
    <div className="inline-state" role="status">
      <span className="spinner" aria-hidden="true" />
      <p>{label}…</p>
    </div>
  );
}
export function EmptyState({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="inline-state is-empty">
      <WorkspaceIcon name="folder" />
      <h3>{title}</h3>
      <p>{children}</p>
    </div>
  );
}
export function ErrorState({ message, retry }: { message: string | null; retry?: () => void }) {
  return (
    <div className="inline-state is-error" role="alert">
      <WorkspaceIcon name="info" />
      <h3>暂时无法读取</h3>
      <p>{message ?? "请稍后重试。"}</p>
      {retry && (
        <button className="secondary" onClick={retry}>
          重试
        </button>
      )}
    </div>
  );
}

export function Modal({
  label,
  className = "",
  close,
  children,
}: {
  label: string;
  className?: string;
  close: () => void;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const element = ref.current;
    const previous = document.activeElement;
    element?.showModal();
    return () => {
      element?.close();
      if (previous instanceof HTMLElement && previous.isConnected) previous.focus();
      else document.querySelector<HTMLElement>('[data-entry-row][tabindex="0"], #workspace-content')?.focus();
    };
  }, []);
  return (
    <dialog
      ref={ref}
      className={className}
      aria-label={label}
      onKeyDown={(event) => {
        if (event.key !== "Tab") return;
        const targets = Array.from(
          event.currentTarget.querySelectorAll<HTMLElement>(
            'button:not([disabled]), a[href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary, [tabindex]:not([tabindex="-1"])',
          ),
        ).filter((target) => {
          const collapsed = target.closest("details:not([open])");
          return (
            target.getClientRects().length > 0 && (!collapsed || target === collapsed.querySelector(":scope > summary"))
          );
        });
        const first = targets[0];
        const last = targets.at(-1);
        if (!first || !last) {
          event.preventDefault();
          return;
        }
        if (event.shiftKey && (document.activeElement === first || document.activeElement === event.currentTarget)) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first.focus();
        }
      }}
      onCancel={(event) => {
        event.preventDefault();
        close();
      }}
    >
      <div className="dialog-heading">
        <h2>{label}</h2>
        <button className="icon-button" aria-label={`关闭${label}`} onClick={close}>
          <WorkspaceIcon name="close" />
        </button>
      </div>
      {children}
    </dialog>
  );
}
