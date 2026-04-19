const TabStrip = ({ tabs, activeId, onActivate, onClose }) => (
  <div style={{
    height: 30, flex: "0 0 30px",
    background: "var(--control-fill-default)",
    borderBottom: "1px solid var(--stroke-default)",
    display: "flex", alignItems: "stretch", overflowX: "auto",
  }}>
    {tabs.map(t => {
      const active = t.id === activeId;
      return (
        <div key={t.id}
          onClick={() => onActivate(t.id)}
          style={{
            height: 30, padding: "0 8px", display: "flex", alignItems: "center", gap: 8,
            borderTop: `2px solid ${active ? "var(--accent-primary)" : "transparent"}`,
            boxSizing: "border-box", cursor: "default",
            background: active ? "transparent" : "transparent",
            minWidth: 112, maxWidth: 240,
          }}
          onMouseEnter={e => { if (!active) e.currentTarget.style.background = "var(--subtle-fill-secondary)"; }}
          onMouseLeave={e => { if (!active) e.currentTarget.style.background = "transparent"; }}
        >
          {t.status === "connecting" && (
            <div style={{
              width: 12, height: 12, borderRadius: "50%",
              border: "2px solid var(--accent-primary)", borderTopColor: "transparent",
              animation: "spin 1s linear infinite",
            }}/>
          )}
          {t.status === "reconnecting" && (
            <span style={{ width: 8, height: 8, borderRadius: "50%", background: "var(--deskbridge-warning)" }}/>
          )}
          {t.status === "error" && (
            <span style={{ width: 8, height: 8, borderRadius: "50%", background: "var(--deskbridge-error)" }}/>
          )}
          <span style={{
            fontSize: 14,
            fontWeight: active ? 600 : 400,
            color: active ? "var(--text-primary)" : "var(--text-secondary)",
            whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis",
            flex: 1,
          }}>{t.title}</span>
          <button onClick={e => { e.stopPropagation(); onClose(t.id); }}
            style={{
              width: 16, height: 16, border: "none", background: "transparent",
              color: "var(--text-secondary)", cursor: "pointer", padding: 0,
              display: "flex", alignItems: "center", justifyContent: "center", borderRadius: 2,
            }}
            onMouseEnter={e => e.currentTarget.style.background = "var(--subtle-fill-secondary)"}
            onMouseLeave={e => e.currentTarget.style.background = "transparent"}
          ><Icons.Dismiss size={12}/></button>
        </div>
      );
    })}
    <style>{`@keyframes spin{to{transform:rotate(360deg)}}`}</style>
  </div>
);

window.TabStrip = TabStrip;
