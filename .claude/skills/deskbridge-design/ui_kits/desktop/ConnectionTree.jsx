const TreeRow = ({ item, depth = 0, selected, onSelect, onToggle, expanded }) => {
  const isGroup = item.kind === "group";
  const active = selected === item.id;
  const [hover, setHover] = React.useState(false);
  return (
    <>
      <div
        onClick={() => { onSelect(item.id); if (isGroup) onToggle(item.id); }}
        onMouseEnter={() => setHover(true)}
        onMouseLeave={() => setHover(false)}
        style={{
          position: "relative",
          height: 28, display: "flex", alignItems: "center",
          paddingLeft: 8 + depth * 19, paddingRight: 8, gap: 6,
          cursor: "default", borderRadius: 4,
          background: active ? "var(--accent-fill-tertiary)" : hover ? "var(--subtle-fill-secondary)" : "transparent",
        }}
      >
        {active && (
          <div style={{
            position: "absolute", left: 2, top: "50%", transform: "translateY(-50%)",
            width: 2, height: 16, background: "var(--accent-primary)", borderRadius: 1,
          }}/>
        )}
        {isGroup ? (
          <span style={{ width: 12, display: "inline-flex", justifyContent: "center" }}>
            <Icons.CaretRight size={10} stroke="var(--text-tertiary)" style={{ transform: expanded ? "rotate(90deg)" : "none" }}/>
          </span>
        ) : <span style={{ width: 12 }}/>}
        {isGroup
          ? (expanded ? <Icons.FolderOpen size={16}/> : <Icons.Folder size={16}/>)
          : <Icons.Desktop size={16}/>
        }
        <span style={{ fontSize: 14, fontWeight: isGroup ? 600 : 400, color: "var(--text-primary)" }}>
          {item.name}
        </span>
        {item.hasCreds && <Icons.Key size={12} stroke="var(--text-secondary)" style={{ marginLeft: 4 }}/>}
      </div>
      {isGroup && expanded && item.children?.map(c => (
        <TreeRow key={c.id} item={c} depth={depth + 1}
          selected={selected} onSelect={onSelect}
          onToggle={onToggle} expanded={expanded}
        />
      ))}
    </>
  );
};

const ConnectionTree = ({ data, selected, onSelect, onConnect }) => {
  const [query, setQuery] = React.useState("");
  const [expanded, setExpanded] = React.useState({ g1: true, g2: true });
  const toggle = id => setExpanded(e => ({ ...e, [id]: !e[id] }));
  const filter = items => items.map(it => {
    if (it.kind === "group") {
      const kids = filter(it.children || []);
      if (kids.length || it.name.toLowerCase().includes(query.toLowerCase())) return { ...it, children: kids };
      return null;
    }
    return it.name.toLowerCase().includes(query.toLowerCase()) ? it : null;
  }).filter(Boolean);
  const visible = query ? filter(data) : data;
  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      <div style={{ padding: "4px 8px" }}>
        <div style={{
          display: "flex", alignItems: "center", gap: 6,
          background: "var(--control-fill-input-active)",
          border: "1px solid var(--stroke-surface)",
          borderRadius: 4, padding: "0 8px", height: 28,
        }}>
          <Icons.Search size={14} stroke="var(--text-tertiary)" />
          <input
            placeholder="Filter connections…"
            value={query} onChange={e => setQuery(e.target.value)}
            style={{
              flex: 1, background: "transparent", border: "none",
              outline: "none", color: "var(--text-primary)", fontSize: 14, fontFamily: "inherit",
            }}
          />
        </div>
      </div>
      <div style={{ flex: 1, overflowY: "auto", padding: "0 4px" }}
        onDoubleClick={() => selected && onConnect(selected)}>
        {visible.length ? visible.map(it => (
          <TreeRow key={it.id} item={it} depth={0}
            selected={selected}
            onSelect={onSelect}
            onToggle={toggle}
            expanded={expanded[it.id]}
          />
        )) : (
          <div style={{ textAlign: "center", padding: 24 }}>
            <div style={{ fontSize: 14, color: "var(--text-secondary)" }}>No connections</div>
            <div style={{ fontSize: 12, color: "var(--text-tertiary)", marginTop: 4 }}>Try a different filter</div>
          </div>
        )}
      </div>
    </div>
  );
};

window.ConnectionTree = ConnectionTree;
