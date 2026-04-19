// Viewport — the RDP session placeholder. In the real app this is the ActiveX host.
const Viewport = ({ tab }) => {
  if (!tab) {
    return (
      <div style={{
        flex: 1, display: "flex", flexDirection: "column",
        alignItems: "center", justifyContent: "center",
        background: "var(--app-bg)",
      }}>
        <div style={{ fontSize: 20, fontWeight: 600, color: "var(--text-secondary)" }}>Deskbridge</div>
        <div style={{ fontSize: 14, color: "var(--text-tertiary)", marginTop: 8 }}>
          Ctrl+N to create a connection
        </div>
      </div>
    );
  }
  if (tab.status === "connecting") {
    return (
      <div style={{ flex: 1, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", background: "#0a0a0a" }}>
        <div style={{
          width: 36, height: 36, borderRadius: "50%",
          border: "3px solid var(--accent-primary)", borderTopColor: "transparent",
          animation: "spin 1s linear infinite",
        }}/>
        <div style={{ marginTop: 16, fontSize: 14, color: "var(--text-secondary)", fontFamily: "var(--font-mono)" }}>
          Connecting to {tab.host}…
        </div>
      </div>
    );
  }
  if (tab.status === "error") {
    return (
      <div style={{ flex: 1, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", background: "#0a0a0a", padding: 24 }}>
        <Icons.ErrorCircle size={48} stroke="var(--deskbridge-error)" />
        <div style={{ marginTop: 12, fontSize: 16, fontWeight: 600 }}>Connection failed</div>
        <div style={{ marginTop: 4, fontSize: 13, color: "var(--text-secondary)", fontFamily: "var(--font-mono)" }}>
          {tab.host} · 0x00000005
        </div>
        <button style={{
          marginTop: 16, background: "var(--accent-fill-default)", color: "#000",
          border: "none", borderRadius: 4, padding: "6px 16px", fontWeight: 600, cursor: "pointer",
        }}>Retry</button>
      </div>
    );
  }
  // connected — fake desktop
  return (
    <div style={{
      flex: 1, background: "linear-gradient(135deg, #2d5a9e 0%, #1e3a6e 100%)",
      position: "relative", overflow: "hidden",
    }}>
      <div style={{
        position: "absolute", top: 24, left: 24, right: 24,
        color: "rgba(255,255,255,0.85)", fontFamily: "var(--font-mono)", fontSize: 12,
      }}>
        <div>Remote desktop · {tab.host}</div>
        <div style={{ opacity: 0.6 }}>Windows Server 2022 · 1920×1080 · 32-bit · 142ms</div>
      </div>
      <div style={{
        position: "absolute", bottom: 0, left: 0, right: 0, height: 40,
        background: "rgba(0,0,0,0.4)", backdropFilter: "blur(8px)",
        display: "flex", alignItems: "center", paddingLeft: 12, gap: 8,
      }}>
        <div style={{ width: 28, height: 28, background: "rgba(255,255,255,0.1)", borderRadius: 2 }}/>
        <div style={{ width: 28, height: 28, background: "rgba(255,255,255,0.1)", borderRadius: 2 }}/>
        <div style={{ flex: 1 }}/>
        <div style={{ fontSize: 11, color: "rgba(255,255,255,0.7)", paddingRight: 12, fontFamily: "var(--font-mono)" }}>
          14:32
        </div>
      </div>
    </div>
  );
};

window.Viewport = Viewport;
