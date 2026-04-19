// Minimal Fluent-style icon set (Lucide-ish SVGs — stand-in for Fluent System Icons).
const Icon = ({ d, size = 16, stroke = "currentColor", sw = 1.8, fill = "none", children, style }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill={fill} stroke={stroke} strokeWidth={sw} strokeLinecap="round" strokeLinejoin="round" style={style}>
    {d && <path d={d} />}
    {children}
  </svg>
);

const Icons = {
  Desktop:   (p)=><Icon {...p}><rect x="2" y="4" width="20" height="14" rx="2"/><path d="M8 22h8M12 18v4"/></Icon>,
  Folder:    (p)=><Icon {...p}><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></Icon>,
  FolderOpen:(p)=><Icon {...p}><path d="M3 7h6l2 2h10v10H3z"/><path d="M3 9h18"/></Icon>,
  Key:       (p)=><Icon {...p}><path d="M15 7a4 4 0 0 1 0 8l-5 5-3-3 5-5a4 4 0 0 1 3-5z"/></Icon>,
  Plug:      (p)=><Icon {...p}><path d="M8 3h8v4M6 7v14l6-3 6 3V7"/></Icon>,
  Settings:  (p)=><Icon {...p}><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 0 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 0 1 0-4h.1A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 0 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 0 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></Icon>,
  Search:    (p)=><Icon {...p}><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/></Icon>,
  Add:       (p)=><Icon {...p}><path d="M12 5v14M5 12h14"/></Icon>,
  Play:      (p)=><Icon {...p} fill="currentColor" sw={0}><path d="M8 5v14l11-7z"/></Icon>,
  Edit:      (p)=><Icon {...p}><path d="M3 21l4-1 13-13-3-3L4 17l-1 4z"/></Icon>,
  Copy:      (p)=><Icon {...p}><rect x="8" y="8" width="12" height="12" rx="2"/><path d="M4 16V4h12"/></Icon>,
  Delete:    (p)=><Icon {...p}><path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14"/></Icon>,
  Move:      (p)=><Icon {...p}><path d="M5 12h14M13 5l7 7-7 7"/></Icon>,
  FolderAdd: (p)=><Icon {...p}><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><path d="M12 11v6M9 14h6"/></Icon>,
  Dismiss:   (p)=><Icon {...p}><path d="M6 6l12 12M18 6L6 18"/></Icon>,
  Chevron:   (p)=><Icon {...p}><path d="M3 5l5 5 5-5" transform="translate(4,2)"/></Icon>,
  CaretRight:(p)=><Icon {...p} fill="currentColor" sw={0}><path d="M9 6l6 6-6 6V6z"/></Icon>,
  CheckCircle:(p)=><Icon {...p}><circle cx="12" cy="12" r="10"/><path d="M8 12l3 3 5-6"/></Icon>,
  ErrorCircle:(p)=><Icon {...p}><circle cx="12" cy="12" r="10"/><path d="M15 9l-6 6M9 9l6 6"/></Icon>,
  Warning:   (p)=><Icon {...p}><path d="M12 3l10 18H2z"/><path d="M12 9v5M12 18h.01"/></Icon>,
  Info:      (p)=><Icon {...p}><circle cx="12" cy="12" r="10"/><path d="M12 8h.01M11 12h1v5h1"/></Icon>,
  Download:  (p)=><Icon {...p}><path d="M12 3v12m-5-5l5 5 5-5M4 21h16"/></Icon>,
  Import:    (p)=><Icon {...p}><path d="M12 3v12m-5-5l5 5 5-5M4 21h16"/></Icon>,
  Export:    (p)=><Icon {...p}><path d="M12 21V9m-5 5l5-5 5 5M4 3h16"/></Icon>,
  Minimize:  (p)=><Icon {...p}><path d="M4 12h16"/></Icon>,
  Maximize:  (p)=><Icon {...p}><rect x="4" y="4" width="16" height="16" rx="1"/></Icon>,
  Close:     (p)=><Icon {...p}><path d="M6 6l12 12M18 6L6 18"/></Icon>,
};

window.Icons = Icons;
