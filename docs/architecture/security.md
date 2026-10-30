# Khylo Server — Security Architecture & Hardening Guide

**Document Version:** 1.0.0  
**Date:** September 2026  
**Status:** Security Baseline & Policies

---

## 1. Threat Model & Security Principles

As a self-hosted media server frequently exposed to the public internet via reverse proxies or port forwarding, Khylo treats security as a fundamental infrastructure requirement.

### Core Security Rules
1. **Never Trust Client-Supplied Paths:** All filesystem paths passed in API requests or metadata queries must be strictly validated against canonical library mount points using `Path.GetFullPath` boundary checks.
2. **Reverse Proxy Trust Boundaries:** Only parse `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host` headers from explicitly configured trusted proxies or CIDR subnets.
3. **Safe Process Execution:** External tool invocations (FFmpeg, FFprobe, emulator runners) must use structured process start info with explicit argument arrays, preventing command-injection attacks.
4. **Credential & Secret Redaction:** API keys, session tokens, user passwords, and private certificates must never appear in server log files or diagnostics dumps.

---

## 2. Hardened Subsystem Protections

### 2.1. Network & Origin Validation
- **CORS Configuration:** Default policy restricts origins; administrators configure explicit whitelist domains for remote web access.
- **Host Header Poisoning Prevention:** Enforces known server hostnames and base URLs.
- **Rate Limiting & Brute-Force Defense:** Built-in rate-limiting middleware throttles failed login attempts per IP address with progressive delays and IP lockouts.

### 2.2. Authentication & Session Security
- **Header Authentication:** Secure token validation using cryptographically generated keys stored in `ApiKeys` and `Devices` tables.
- **Immediate Session Invalidation:** When a user logs out or an administrator revokes a session from the dashboard, the token is instantly blacklisted and purged from memory.
- **Password Storage:** Salted and hashed using strong cryptography (PBKDF2 / Argon2id).

### 2.3. Media Processing & Archive Extraction Sandboxing
- **Zip-Slip & Path Traversal Prevention:** When inspecting comic archives (CBZ) or extracting temporary data, every archive entry path is validated to ensure it cannot extract outside the intended scratch directory.
- **Image Bomb Mitigation:** Image dimensions are inspected in file headers before allocating full decompression buffers in SkiaSharp.

---

## 3. Administrator Diagnostics & Redaction Policy

All log entries passing through the Serilog enrichment pipeline are filtered through a `SensitiveDataEnricher` that masks:
- Authorization tokens (`X-Emby-Token`, `Authorization: Bearer ...`)
- User passwords in query strings or request bodies
- API keys in external provider requests
- Local filesystem paths containing sensitive user home directories
