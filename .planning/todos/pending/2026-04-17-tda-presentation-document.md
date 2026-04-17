---
created: 2026-04-17T06:00:00Z
title: Create technical design authority presentation document
area: docs
files: []
---

## Problem

Need a formal document to present Deskbridge to the Technical Design Authority (TDA) at work. The audience is enterprise architects evaluating whether this tool is suitable for internal use on managed infrastructure.

## Solution

Generate a comprehensive TDA presentation document covering:

1. **Architecture overview** — .NET 10 + WPF-UI Fluent, 3-project solution structure, DI + event bus, connection pipeline pattern
2. **Technology choices** — rationale for each major dependency (WPF-UI, CommunityToolkit.Mvvm, Velopack, Serilog, AdysTech.CredentialManager), alternatives considered and why rejected
3. **Security model** — PBKDF2 master password/PIN, credential storage (DESKBRIDGE/CONN/ targets in Windows Credential Manager, NOT TERMSRV/ to avoid Credential Guard conflicts), audit logging, lock overlay with airspace mitigation, idle/session auto-lock
4. **Deployment approach** — Velopack per-user install (%LocalAppData%), auto-update from GitHub Releases, no admin rights required, self-contained single-file exe
5. **Enterprise considerations** — Credential Guard compatibility, GPO interactions, no outbound telemetry, local-only data (%AppData%), no cloud dependencies
6. **RDP ActiveX lifecycle** — proper COM disposal, GDI handle management, ~15-20 session practical limit, thread affinity

**Timing:** Generate after v1.0 build is complete (post Phase 7). Use `/gsd-docs-update` or `/gsd-quick` to produce the document.

**Format:** Markdown document suitable for conversion to PDF or PowerPoint. Professional tone, no emojis, structured with clear sections and diagrams where appropriate.
