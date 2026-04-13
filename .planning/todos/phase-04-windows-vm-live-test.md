---
created: 2026-04-13
source: Plan 04-03 live verification
origin_phase: 04-rdp-integration
type: testing-infrastructure
priority: medium
---

# Live verification against Windows RDP target

Plan 04-03 closed with partial live verification — xrdp on Ubuntu proved the pipeline can render (saw login page once at 20:43:38 on 2026-04-13), but the xrdp + AxMsRdpClient9 codec negotiation is unreliable (codec id 5 ACK stall) and drag/fullscreen caveats were discovered but not all retested end-to-end.

## Deferred items

1. Plan 04-03 Task 8 — full 9-step checklist from PLAN.md line 709+:
   - Drop-reconnect happy path (stop xrdp mid-session, expect overlay + reconnect)
   - Cancel during auto-retry
   - 20-attempt cap (D-05)
   - Manual Reconnect / Close buttons
   - GDI stability across 5 drop-reconnect cycles
   - Window close while reconnecting
2. Airspace drag verification — commit 6abbdb1 fixed the conceptual bug (Hidden → Collapsed) but couldn't retest live because xrdp wasn't stable
3. Fullscreen rendering — known broken (WFH reparent on WindowStyle change not wired)

## Setup for future session

- Windows 11 Pro/Enterprise VM via Hyper-V (free, built into Windows Pro)
  - Enable "Remote Desktop" in VM settings
  - Create test user, add to Remote Desktop Users group
  - Set VM RAM ≥ 4GB
- From Deskbridge host, connect to VM IP at 3389 with Own mode credentials
- Windows RDP target won't hit:
  - 0x708 self-RDP restriction
  - xrdp codec compatibility issues
  - NLA/CredSSP compat issues (Windows-to-Windows has full CredSSP support)

## xrdp compatibility note

Not a product bug. Deskbridge targets Windows RDP (per PROJECT.md). xrdp was a convenience test target. The codec-ack stall is an interaction between AxMsRdpClient9's modern codec advertising and xrdp's lack of Microsoft-proprietary codec support. Fixing would require disabling specific advanced codecs on the client side, which would hurt Windows-target performance for no product benefit.
