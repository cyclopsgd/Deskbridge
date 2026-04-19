// App — Deskbridge shell prototype
const { useState, useEffect, useCallback } = React;

const TREE = [
  { id: "g1", kind: "group", name: "Production", hasCreds: true, children: [
    { id: "c1", kind: "conn", name: "srv-prod-01", host: "srv-prod-01.corp.local" },
    { id: "c2", kind: "conn", name: "srv-prod-02", host: "srv-prod-02.corp.local" },
    { id: "c3", kind: "conn", name: "db-prod-eu",  host: "db-prod-eu.corp.local"  },
  ]},
  { id: "g2", kind: "group", name: "Staging", children: [
    { id: "c4", kind: "conn", name: "db-staging-02", host: "db-staging-02.corp.local" },
    { id: "c5", kind: "conn", name: "bastion-01",    host: "bastion-01.corp.local" },
  ]},
  { id: "g3", kind: "group", name: "Lab", children: [
    { id: "c6", kind: "conn", name: "jump-eu-west",  host: "jump-eu-west.lab" },
    { id: "c7", kind: "conn", name: "failed-host",   host: "failed.corp.local" },
  ]},
];

const flat = (nodes, acc = []) => {
  nodes.forEach(n => {
    if (n.kind === "conn") acc.push(n);
    else if (n.children) flat(n.children, acc);
  });
  return acc;
};
const ALL_CONNS = flat(TREE);

function App() {
  const [panel, setPanel]   = useState("conn");
  const [selected, setSel]  = useState("c1");
  const [tabs, setTabs]     = useState([
    { id: "t1", connId: "c1", title: "srv-prod-01", host: "srv-prod-01.corp.local", status: "connected" },
  ]);
  const [activeTab, setAT]  = useState("t1");
  const [toasts, setToasts] = useState([
    { id: "tst1", kind: "success", title: "Saved", body: "Your changes have been saved." },
  ]);
  const [palOpen, setPalOpen] = useState(false);
  const [reqPwd, setReqPwd]   = useState(true);
  const [autoLock, setAL]     = useState(10);
  const [lockOnMin, setLOM]   = useState(false);

  const dismissToast = id => setToasts(t => t.filter(x => x.id !== id));
  const pushToast = t => setToasts(x => [...x.slice(-2), { ...t, id: "t" + Date.now() }]);

  const openConn = useCallback((connId) => {
    const c = ALL_CONNS.find(x => x.id === connId);
    if (!c) return;
    const existing = tabs.find(t => t.connId === connId);
    if (existing) { setAT(existing.id); return; }
    const newTab = { id: "t" + Date.now(), connId, title: c.name, host: c.host, status: "connecting" };
    setTabs(t => [...t, newTab]);
    setAT(newTab.id);
    setTimeout(() => setTabs(ts => ts.map(t => t.id === newTab.id ? { ...t, status: c.name === "failed-host" ? "error" : "connected" } : t)), 1600);
    if (c.name === "failed-host") setTimeout(() => pushToast({ kind: "error", title: "Connection failed", body: `${c.host} · 0x00000005` }), 1700);
  }, [tabs]);

  const closeTab = id => {
    setTabs(ts => {
      const nxt = ts.filter(t => t.id !== id);
      if (activeTab === id) setAT(nxt[nxt.length - 1]?.id || null);
      return nxt;
    });
  };

  // keyboard shortcuts
  useEffect(() => {
    const h = e => {
      if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key.toLowerCase() === "p") { e.preventDefault(); setPalOpen(true); }
      if (e.key === "Escape") setPalOpen(false);
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "w" && activeTab) { e.preventDefault(); closeTab(activeTab); }
    };
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  }, [activeTab]);

  const selectedConn = ALL_CONNS.find(c => c.id === selected);
  const activeTabObj = tabs.find(t => t.id === activeTab);

  const palItems = [
    ...ALL_CONNS.map(c => ({ id: c.id, kind: "conn", label: c.name, hint: "Connect", onPick: () => openConn(c.id) })),
    { id: "new", kind: "cmd", label: "New Connection", hint: "Ctrl+N" },
    { id: "imp", kind: "cmd", label: "Import Connections…", hint: "" },
    { id: "lock", kind: "cmd", label: "Lock Application", hint: "Ctrl+L" },
  ];

  return (
    <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column",
      background: "rgba(32,32,32,0.7)", backdropFilter: "blur(40px)", borderRadius: 8, overflow: "hidden",
      border: "1px solid rgba(255,255,255,0.06)" }}>
      <TitleBar title="Deskbridge — srv-prod-01" />
      <div style={{ flex: 1, display: "flex", minHeight: 0, position: "relative" }}>
        <IconRail panel={panel} setPanel={setPanel} />

        {panel && (
          <div style={{
            width: 240, flex: "0 0 240px",
            background: "var(--control-fill-default)",
            borderRight: "1px solid var(--stroke-default)",
            display: "flex", flexDirection: "column",
          }}>
            <SectionLabel>{panel === "conn" ? "Connections" : panel === "search" ? "Search" : "Settings"}</SectionLabel>
            {panel === "conn" && (
              <ConnectionTree data={TREE} selected={selected} onSelect={setSel} onConnect={openConn} />
            )}
            {panel === "search" && (
              <div style={{ padding: 12, fontSize: 13, color: "var(--text-tertiary)" }}>
                Use Ctrl+Shift+P for the full command palette.
              </div>
            )}
            {panel === "settings" && (
              <div style={{ padding: "4px 12px 12px", overflowY: "auto" }}>
                <SectionLabel style={{ padding: "8px 0 4px" }}>Security</SectionLabel>
                <Card>
                  <div style={{ fontSize: 14, marginBottom: 4 }}>Require password/PIN</div>
                  <Toggle on={reqPwd} onChange={setReqPwd} />
                  <div style={{ height: 1, background: "var(--stroke-default)", margin: "12px 0" }}/>
                  <div style={{ opacity: reqPwd ? 1 : 0.4, pointerEvents: reqPwd ? "auto" : "none" }}>
                    <div style={{ fontSize: 14, marginBottom: 4 }}>Auto-lock timeout</div>
                    <input type="number" min="1" max="1440" value={autoLock}
                      onChange={e => setAL(+e.target.value)}
                      style={{
                        width: "100%", boxSizing: "border-box",
                        height: 28, background: "var(--control-fill-input-active)",
                        border: "1px solid var(--stroke-surface)", borderRadius: 4,
                        color: "var(--text-primary)", fontSize: 14, padding: "0 8px", fontFamily: "inherit",
                        marginBottom: 12,
                      }}/>
                    <div style={{ fontSize: 14, marginBottom: 4 }}>Lock on minimise</div>
                    <Toggle on={lockOnMin} onChange={setLOM} />
                    <button style={{
                      marginTop: 12, width: "100%", height: 30,
                      background: "var(--control-fill-secondary)",
                      border: "1px solid var(--stroke-default)",
                      color: "var(--text-primary)", borderRadius: 4, fontSize: 14, cursor: "pointer",
                      fontFamily: "inherit",
                    }}>Change Password…</button>
                  </div>
                </Card>
                <SectionLabel style={{ padding: "16px 0 4px" }}>Data</SectionLabel>
                <Card>
                  {[
                    { label: "Import Connections…", Icon: Icons.Import },
                    { label: "Export as JSON",     Icon: Icons.Export },
                    { label: "Export as CSV",      Icon: Icons.Export },
                  ].map(b => (
                    <button key={b.label} style={{
                      width: "100%", marginBottom: 8, padding: "6px 10px",
                      background: "var(--control-fill-secondary)",
                      border: "1px solid var(--stroke-default)",
                      color: "var(--text-primary)", borderRadius: 4, fontSize: 14,
                      display: "flex", alignItems: "center", gap: 8, cursor: "pointer",
                      fontFamily: "inherit",
                    }}>
                      <b.Icon size={14}/> {b.label}
                    </button>
                  ))}
                </Card>
              </div>
            )}
          </div>
        )}

        <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
          <TabStrip tabs={tabs} activeId={activeTab} onActivate={setAT} onClose={closeTab}/>
          <Viewport tab={activeTabObj}/>
        </div>

        <ToastStack toasts={toasts} dismiss={dismissToast}/>
        {palOpen && <CommandPalette items={palItems} onClose={() => setPalOpen(false)}
          onPick={it => { setPalOpen(false); it.onPick?.(); }}/>}
      </div>
      <StatusBar
        status={`${tabs.length} active${activeTabObj ? ` · ${activeTabObj.host}` : ""} · Ctrl+Shift+P for commands`}
        secondary="v1.3.2 · Ready"
      />
    </div>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<App/>);
