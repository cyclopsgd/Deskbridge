// Shell chrome: TitleBar, IconRail, SlideoutPanel, StatusBar.
const { useState, useEffect, useRef } = React;

const TitleBar = ({ title }) => (
  <div style={{
    height: 32, flex: "0 0 32px", display: "flex", alignItems: "center",
    justifyContent: "space-between", padding: "0 0 0 12px",
    userSelect: "none", WebkitAppRegion: "drag",
  }}>
    <span style={{ fontSize: 12, color: "var(--text-secondary)" }}>{title}</span>
    <div style={{ display: "flex" }}>
      {[Icons.Minimize, Icons.Maximize, Icons.Close].map((I, i) => (
        <div key={i} style={{
          width: 46, height: 32, display: "flex", alignItems: "center", justifyContent: "center",
          cursor: "pointer",
        }}
          onMouseEnter={e => e.currentTarget.style.background = i === 2 ? "#C42B1C" : "rgba(255,255,255,0.06)"}
          onMouseLeave={e => e.currentTarget.style.background = "transparent"}
        >
          <I size={10} stroke="var(--text-primary)" sw={1.2} />
        </div>
      ))}
    </div>
  </div>
);

const RailButton = ({ icon: Icon, active, onClick, label }) => (
  <button onClick={onClick} title={label} style={{
    width: 36, height: 36, background: "transparent", color: "var(--text-primary)",
    border: "none", borderLeft: `2px solid ${active ? "var(--accent-primary)" : "transparent"}`,
    cursor: "pointer", display: "flex", alignItems: "center", justifyContent: "center",
  }}
    onMouseEnter={e => { if (!active) e.currentTarget.style.background = "var(--subtle-fill-secondary)"; }}
    onMouseLeave={e => e.currentTarget.style.background = "transparent"}
  >
    <Icon size={20} stroke={active ? "var(--text-primary)" : "var(--text-secondary)"} />
  </button>
);

const IconRail = ({ panel, setPanel }) => (
  <div style={{
    width: 36, flex: "0 0 36px", background: "var(--control-fill-default)",
    borderRight: "1px solid var(--stroke-default)",
    display: "flex", flexDirection: "column", justifyContent: "space-between",
  }}>
    <div>
      <RailButton icon={Icons.Plug}   active={panel === "conn"}    onClick={() => setPanel(panel === "conn" ? null : "conn")} label="Connections" />
      <RailButton icon={Icons.Search} active={panel === "search"} onClick={() => setPanel(panel === "search" ? null : "search")} label="Search" />
    </div>
    <div>
      <RailButton icon={Icons.Settings} active={panel === "settings"} onClick={() => setPanel(panel === "settings" ? null : "settings")} label="Settings" />
    </div>
  </div>
);

const SectionLabel = ({ children, style }) => (
  <div style={{
    fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.06em",
    color: "var(--text-secondary)", padding: "8px 12px", ...style,
  }}>{children}</div>
);

const Card = ({ children, style }) => (
  <div style={{
    background: "var(--control-fill-default)",
    border: "1px solid var(--stroke-default)",
    borderRadius: 4, padding: 12, ...style,
  }}>{children}</div>
);

const Toggle = ({ on, onChange }) => (
  <div onClick={() => onChange(!on)} style={{
    width: 40, height: 20, borderRadius: 999, cursor: "pointer",
    background: on ? "var(--accent-fill-default)" : "transparent",
    border: on ? "1px solid transparent" : "1px solid var(--text-secondary)",
    padding: 2, display: "flex", justifyContent: on ? "flex-end" : "flex-start",
    transition: "all .15s ease-out",
  }}>
    <div style={{
      width: on ? 14 : 12, height: on ? 14 : 12, borderRadius: "50%",
      background: on ? "#000" : "var(--text-secondary)",
    }} />
  </div>
);

const StatusBar = ({ status, secondary }) => (
  <div style={{
    height: 22, flex: "0 0 22px", background: "var(--accent-secondary)",
    color: "var(--statusbar-fg)", display: "flex", alignItems: "center",
    padding: "0 8px", fontSize: 12, justifyContent: "space-between",
  }}>
    <span>{status}</span>
    <span style={{ opacity: 0.85 }}>{secondary}</span>
  </div>
);

const Toast = ({ kind, title, body, onDismiss }) => {
  const colors = {
    success: "var(--deskbridge-success)",
    warning: "var(--deskbridge-warning)",
    error:   "var(--deskbridge-error)",
    info:    "var(--accent-primary)",
  };
  const IconMap = { success: Icons.CheckCircle, warning: Icons.Warning, error: Icons.ErrorCircle, info: Icons.Info };
  const I = IconMap[kind] || Icons.Info;
  return (
    <div style={{
      background: "rgba(40,40,40,0.97)",
      border: `1px solid ${colors[kind]}66`,
      borderLeft: `3px solid ${colors[kind]}`,
      borderRadius: 8, padding: "10px 12px",
      display: "flex", gap: 10, alignItems: "flex-start",
      boxShadow: "0 4px 14px rgba(0,0,0,0.45)",
      width: 320,
    }}>
      <I size={18} stroke={colors[kind]} />
      <div style={{ flex: 1 }}>
        <div style={{ fontSize: 14, fontWeight: 600 }}>{title}</div>
        {body && <div style={{ fontSize: 12, color: "var(--text-secondary)" }}>{body}</div>}
      </div>
      <button onClick={onDismiss} style={{ background: "none", border: "none", color: "var(--text-secondary)", cursor: "pointer", padding: 2 }}>
        <Icons.Dismiss size={12} />
      </button>
    </div>
  );
};

const ToastStack = ({ toasts, dismiss }) => (
  <div style={{
    position: "absolute", bottom: 34, right: 16, zIndex: 100,
    display: "flex", flexDirection: "column-reverse", gap: 8,
  }}>
    {toasts.map(t => <Toast key={t.id} {...t} onDismiss={() => dismiss(t.id)} />)}
  </div>
);

Object.assign(window, { TitleBar, IconRail, SectionLabel, Card, Toggle, StatusBar, ToastStack, Toast });
