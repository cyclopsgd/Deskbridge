const CommandPalette = ({ items, onPick, onClose }) => {
  const [q, setQ] = React.useState("");
  const [idx, setIdx] = React.useState(0);
  const input = React.useRef(null);
  React.useEffect(() => { input.current?.focus(); }, []);
  const filtered = items.filter(i => i.label.toLowerCase().includes(q.toLowerCase())).slice(0, 8);
  const keydown = e => {
    if (e.key === "Escape") onClose();
    if (e.key === "ArrowDown") { setIdx(i => Math.min(i + 1, filtered.length - 1)); e.preventDefault(); }
    if (e.key === "ArrowUp")   { setIdx(i => Math.max(i - 1, 0)); e.preventDefault(); }
    if (e.key === "Enter" && filtered[idx]) onPick(filtered[idx]);
  };
  const highlight = (text, q) => {
    if (!q) return text;
    const i = text.toLowerCase().indexOf(q.toLowerCase());
    if (i < 0) return text;
    return <>{text.slice(0, i)}<mark style={{ background: "transparent", color: "var(--accent-primary)", fontWeight: 600 }}>{text.slice(i, i + q.length)}</mark>{text.slice(i + q.length)}</>;
  };
  return (
    <div style={{
      position: "absolute", inset: 0, zIndex: 200,
      display: "flex", justifyContent: "center", paddingTop: 80,
      background: "rgba(0,0,0,0.2)",
    }} onClick={onClose}>
      <div onClick={e => e.stopPropagation()} style={{
        width: 480, background: "rgba(40,40,40,0.98)",
        border: "1px solid var(--stroke-default)", borderRadius: 8,
        boxShadow: "0 12px 40px rgba(0,0,0,0.55)", overflow: "hidden",
        height: "fit-content",
      }}>
        <div style={{ padding: "10px 12px", borderBottom: "1px solid var(--stroke-default)", display: "flex", alignItems: "center", gap: 8 }}>
          <Icons.Search size={14} stroke="var(--text-tertiary)"/>
          <input ref={input} value={q} onChange={e => { setQ(e.target.value); setIdx(0); }}
            onKeyDown={keydown}
            placeholder="Type a command or search…"
            style={{ flex: 1, background: "transparent", border: "none", outline: "none",
              color: "var(--text-primary)", fontSize: 14, fontFamily: "inherit" }}/>
        </div>
        <div style={{ padding: "4px 0", maxHeight: 320, overflowY: "auto" }}>
          {filtered.length ? filtered.map((it, i) => (
            <div key={it.id} onClick={() => onPick(it)}
              style={{
                height: 32, padding: "0 12px", display: "flex", alignItems: "center", gap: 8,
                background: i === idx ? "var(--accent-fill-tertiary)" : "transparent",
                cursor: "default",
              }}>
              {it.kind === "conn" ? <Icons.Desktop size={14}/> : <Icons.CaretRight size={14}/>}
              <span style={{ fontSize: 14 }}>{highlight(it.label, q)}</span>
              <span style={{ marginLeft: "auto", fontSize: 11, color: "var(--text-tertiary)", fontFamily: "var(--font-mono)" }}>{it.hint}</span>
            </div>
          )) : (
            <div style={{ padding: 16, fontSize: 13, color: "var(--text-tertiary)", textAlign: "center" }}>No matches</div>
          )}
        </div>
      </div>
    </div>
  );
};

window.CommandPalette = CommandPalette;
