const OFFLINE_ICON = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
    <path d="M7 18a4 4 0 1 1 .6-7.96A6 6 0 0 1 19 9a4.5 4.5 0 0 1-2 8.5Z" />
    <path d="m4 20 16-16" />
  </svg>
);

const EMPTY_ICON = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7">
    <path d="M3 7a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z" />
    <path d="M3 11h5l1.5 2h5L16 11h5" />
  </svg>
);

const RETRY_ICON = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9">
    <path d="M21 12a9 9 0 1 1-2.6-6.4" />
    <path d="M21 3v6h-6" />
  </svg>
);

export function Skeleton({ className = "", style }) {
  return <span className={"sk" + (className ? " " + className : "")} style={style} aria-hidden="true" />;
}

export function OfflineCard({ title = "后端服务未连接", description = "正在自动重连，当前数据可能为缓存或模拟值", onRetry, retrying = false }) {
  return (
    <div className="offline-card reveal enter" role="alert">
      <span className="oc-ic">{OFFLINE_ICON}</span>
      <span className="oc-text">
        <b>{title}</b>
        <small>{description}</small>
      </span>
      {onRetry && (
        <button className="btn ghost" onClick={onRetry} disabled={retrying}>
          {RETRY_ICON}
          {retrying ? "重试中..." : "重试"}
        </button>
      )}
    </div>
  );
}

export function EmptyState({ title = "暂无数据", description, action }) {
  return (
    <div className="card empty-state reveal enter">
      <span className="es-ic">{EMPTY_ICON}</span>
      <b>{title}</b>
      {description && <p>{description}</p>}
      {action && <span className="es-actions">{action}</span>}
    </div>
  );
}
