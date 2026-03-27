---
task: Hyper-V K3s multi-site HA proof of concept
status: in_progress
last_updated: 2026-03-27
---

<current_state>
Converting from single-server K3s to HA (3-server) setup.
Two VMs created and running. K3s installed but needs reinstall with HA flags.
Paused before Part 5 (load container images).
</current_state>

<completed_work>
- Part 1: Image not yet built from ship/ (still needed)
- Part 2: Hyper-V already enabled (Docker Desktop)
- Part 3.1: SimetraSwitch created with NAT (172.20.0.0/24)
- Part 3.2-3.5 Site A: VM created, Ubuntu installed, SSH verified (172.20.0.10)
- Part 3.2-3.5 Site B: VM created, Ubuntu installed, SSH verified (172.20.0.11)
- Part 4A: K3s server installed on Site A (single-server mode — needs reinstall with --cluster-init)
- Part 4B: K3s agent installed on Site B (agent mode — needs reinstall with --server)
- Both nodes verified: kubectl get nodes shows 2 Ready nodes
- Install files cleaned up on both VMs (4A.6, 4B.4)
</completed_work>

<remaining_work>
CONVERSION TO HA (before continuing with Part 5):
1. Uninstall K3s agent on Site B: sudo /usr/local/bin/k3s-agent-uninstall.sh
2. Uninstall K3s server on Site A: sudo /usr/local/bin/k3s-uninstall.sh
3. Check if /usr/local/bin/k3s binary still exists on both VMs (uninstall may keep it)
4. If binary missing: re-SCP k3s files (4A.1/4B.1) and place them
5. Reinstall Site A: INSTALL_K3S_SKIP_DOWNLOAD=true ~/install.sh --cluster-init
6. Verify Site A: kubectl get nodes (should show control-plane,master)
7. Get token: sudo cat /var/lib/rancher/k3s/server/node-token
8. Reinstall Site B: INSTALL_K3S_SKIP_DOWNLOAD=true K3S_URL=https://172.20.0.10:6443 K3S_TOKEN=<token> ~/install.sh --server
9. Verify both nodes: kubectl get nodes (both should show control-plane,master)

THEN CONTINUE WITH:
- Part 1: Build image from ship/ (docker build -t snmp-collector:v3.0 .)
- Part 1.2: Save to tar (docker save)
- Part 5: SCP tars + deploy/ to VMs, import images on both nodes
- Part 6: Deploy OTel Collector DaemonSet (simetra-infra)
- Part 7: Deploy SnmpCollector Site A (simetra-site-a, port 10162, preferred simetra-k3s-a)
- Part 7: Deploy SnmpCollector Site B (simetra-site-b, port 10163, preferred simetra-k3s-b)
- Verification + failover testing

NODEPORT SERVICES (for Docker Desktop test only):
- Simulators and Prometheus on Docker Desktop need NodePort exposure
- K3s pods reach Docker Desktop services via Windows host IP (172.20.0.1)
- Not yet created — do before Part 7 devices config
</remaining_work>

<decisions_made>
- Using HA guide (DEPLOY_K3S_MULTISITE_HA.md) instead of single-server guide
- 2 VMs on same host for testing (172.20.0.10 and 172.20.0.11)
- Docker Desktop keeps simulators/Prometheus/Grafana, K3s VMs run SnmpCollector + OTel
- replicas: 2 per site (2 nodes available, anti-affinity spreads 1 per node)
- NodePort services needed to bridge Docker Desktop ↔ K3s
</decisions_made>

<blockers>
- K3s currently installed as single-server + agent — needs reinstall as HA (--cluster-init + --server)
- Install files were cleaned up — may need to re-SCP k3s binary and install.sh
- snmp-collector:v3.0 image not yet built from ship/
</blockers>

<context>
Two guides exist:
- ship/docs/DEPLOY_K3S_MULTISITE.md — single server + agents
- ship/docs/DEPLOY_K3S_MULTISITE_HA.md — all servers (HA, production target)

The manual was heavily refined during hands-on testing (Ubuntu installer steps,
verification after every command, SSH vs PowerShell switching, etc.).

Docker Desktop still has the previous test deployment running:
- simetra-infra: OTel Collector DaemonSet
- simetra-site-a: SnmpCollector (1 replica)
- simetra-site-b: SnmpCollector (1 replica)
These can be cleaned up or left running — they don't conflict with K3s.

VM credentials:
- Site A: simetra-k3s-a / simetra-k3s-a @ 172.20.0.10
- Site B: simetra-k3s-b / simetra-k3s-b @ 172.20.0.11
</context>

<next_action>
1. Start VMs: Start-VM -Name "simetra-k3s-a" and Start-VM -Name "simetra-k3s-b"
2. SSH into both VMs
3. Uninstall current K3s on both (agent first, then server)
4. Check if k3s binary exists at /usr/local/bin/k3s
5. Reinstall with HA flags
</next_action>
