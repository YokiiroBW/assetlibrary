import { useEffect, useRef, useState, type FormEvent } from "react";
import { useSession } from "./hooks/useSession";
import { failure, isAbort } from "./hooks/queryState";
import { ReadOnlyWorkspace } from "./ReadOnlyWorkspace";

export function SessionGate() {
  const session = useSession();
  const state = session.state;
  if (state.status === "authenticated") {
    return (
      <ReadOnlyWorkspace
        key={state.generation}
        session={state.session}
        sessionNotice={state.notice}
        imagesAllowed={state.imagesAllowed}
        onReconnect={session.reconnect}
        onSessionExpired={session.expire}
        onSignOut={() => void session.signOut(state.session)}
      />
    );
  }
  if (state.status === "anonymous") {
    return <SignInForm expired={state.expired} onSignIn={session.signIn} onReconnect={session.reconnect} />;
  }
  return (
    <main className="centered-state">
      <span className="eyebrow">AssetLibrary</span>
      <h1>
        {state.status === "unavailable"
          ? "暂时无法连接"
          : state.status === "sign_out_failed"
            ? "退出尚未确认"
            : state.status === "signing_out"
              ? "正在退出登录"
              : "正在连接"}
      </h1>
      {(state.status === "checking" || state.status === "signing_out") && <p role="status">请稍候…</p>}
      {state.status === "unavailable" && (
        <>
          <p role="alert">{state.message}</p>
          <button type="button" onClick={session.reconnect}>
            重试连接
          </button>
        </>
      )}
      {state.status === "sign_out_failed" && (
        <>
          <p role="alert">{state.message} 服务器尚未确认退出，资产内容已隐藏。</p>
          <button type="button" onClick={() => void session.signOut(state.session)}>
            重试退出
          </button>
        </>
      )}
    </main>
  );
}

function SignInForm({
  expired,
  onSignIn,
  onReconnect,
}: {
  expired: boolean;
  onSignIn: (accountName: string, password: string, signal: AbortSignal) => Promise<void>;
  onReconnect: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const active = useRef<AbortController | null>(null);
  const errorElement = useRef<HTMLParagraphElement>(null);
  useEffect(() => () => active.current?.abort(), []);
  useEffect(() => {
    if (message !== null) errorElement.current?.focus();
  }, [message]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (busy) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    const controller = new AbortController();
    active.current?.abort();
    active.current = controller;
    setBusy(true);
    setMessage(null);
    try {
      await onSignIn(String(data.get("account_name") ?? ""), String(data.get("password") ?? ""), controller.signal);
    } catch (error: unknown) {
      if (!isAbort(error)) {
        setMessage(failure(error).message);
        const password = form.elements.namedItem("password");
        if (password instanceof HTMLInputElement) password.value = "";
      }
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  };

  return (
    <main className="centered-state sign-in-screen">
      <form className="sign-in-form" onSubmit={(event) => void submit(event)}>
        <div className="brand-mark" aria-hidden="true">
          AL
        </div>
        <span className="eyebrow">AssetLibrary</span>
        <h1>{expired ? "登录状态已失效" : "登录资源库"}</h1>
        <p>{expired ? "请重新登录，继续浏览你的资源库。" : "连接服务器上的真实目录，安全浏览与搜索。"}</p>
        <label>
          账号
          <input
            name="account_name"
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            required
            maxLength={64}
            disabled={busy}
          />
        </label>
        <label>
          密码
          <input
            name="password"
            type="password"
            autoComplete="current-password"
            required
            maxLength={256}
            disabled={busy}
          />
        </label>
        {message !== null && (
          <p className="form-error" ref={errorElement} tabIndex={-1} role="alert">
            {message}
          </p>
        )}
        <button className="primary" type="submit" disabled={busy}>
          {busy ? "正在登录…" : "登录"}
        </button>
        {expired && (
          <button className="secondary" type="button" disabled={busy} onClick={onReconnect}>
            重试连接
          </button>
        )}
        <p className="form-hint">账号由服务器管理员配置。首次扫描只读取目录信息。</p>
      </form>
    </main>
  );
}
