# SnmpCollector Offline Deployment Guide (K3s Multi-Site)

## Overview

This guide deploys the SnmpCollector system across **3 sites** using a single K3s cluster
spanning 3 nodes. Each site runs a Linux VM on a Windows Server 2019 host via Hyper-V.
Multiple site namespaces share a single OTel Collector DaemonSet (one per node).
All required files are pre-included in the `ship/` folder — no internet access is needed.

**Architecture:**

```
Node: simetra-k3s-a (Site A)
├── OTel Collector pod (DaemonSet, simetra-infra) — hostPort 4317
├── SnmpCollector pod (simetra-site-a) — leader, monitors Site A devices
└── SnmpCollector pod (simetra-site-b) — follower

Node: simetra-k3s-b (Site B)
├── OTel Collector pod (DaemonSet, simetra-infra) — hostPort 4317
├── SnmpCollector pod (simetra-site-a) — follower
└── SnmpCollector pod (simetra-site-b) — leader, monitors Site B devices

Node: simetra-k3s-c (Site C)
├── OTel Collector pod (DaemonSet, simetra-infra) — hostPort 4317
├── SnmpCollector pod (simetra-site-a) — follower
└── SnmpCollector pod (simetra-site-b) — follower
```

**3 namespaces, 9 pods total (3 per node).**

**Key design:**
- One K3s cluster, three nodes (one per site)
- Each site namespace has 3 SnmpCollector pods with pod anti-affinity (one per node)
- Preferred leader election: each site's preferred node is closest to its SNMP devices
- OTel Collector DaemonSet with hostPort — every pod sends to its local node's collector
- `k8s_namespace_name` label distinguishes site metrics in Prometheus/Grafana
- `service_instance_id` label identifies which physical node each pod runs on

**Namespaces:**

| Namespace | Contents | Scope |
|-----------|----------|-------|
| `simetra-infra` | OTel Collector DaemonSet (1 pod per node, hostPort 4317) | Shared infrastructure |
| `simetra-site-a` | SnmpCollector (3 pods) + ConfigMaps + RBAC for Site A | Site A devices |
| `simetra-site-b` | SnmpCollector (3 pods) + ConfigMaps + RBAC for Site B | Site B devices |

**Target environment (per site):**
- Host: Windows Server 2019 Standard
- Kubernetes: K3s v1.31.13+k3s1 (lightweight, production-grade, CNCF certified)
- Runtime: Hyper-V VM running Ubuntu Server 22.04 LTS

**Provided by your organization (not deployed here):**
- Prometheus
- Grafana
- Elasticsearch / Kibana

> The OTel Collector export addresses are configured in `deploy/otel-collector-configmap.yaml`.
> Update the `prometheusremotewrite` and `elasticsearch` endpoints to match your organization's infrastructure.

**Pre-included files:**

```
ship/
├── infra/
│   ├── ubuntu/ubuntu-22.04.5-live-server-amd64.iso   # VM OS
│   └── k3s/
│       ├── k3s                                        # K3s binary (amd64)
│       ├── k3s-airgap-images-amd64.tar.zst            # K3s container images
│       └── install.sh                                 # K3s install script
├── deploy/                                            # K8s manifests
├── otel-collector-0.120.0.tar                         # OTel container image
└── snmp-collector-v3.0.tar                            # SnmpCollector container image
```

**Network requirements (between the 3 Linux VMs):**

| Port | Protocol | Purpose |
|------|----------|---------|
| 6443 | TCP | K3s API server (agents → server) |
| 8472 | UDP | Flannel VXLAN (pod-to-pod networking) |
| 10250 | TCP | Kubelet metrics |

---

## Site Configuration

Before starting, fill in the values for each site deployment.

**SnmpCollector (repeat Part 7 per site):**

| Parameter | Site A | Site B | Convention |
|-----------|--------|--------|------------|
| `{NAMESPACE}` | `simetra-site-a` | `simetra-site-b` | `simetra-site-{letter}` |
| `{PREFERRED_NODE}` | `simetra-k3s-a` | `simetra-k3s-b` | Ubuntu server name from Part 3 |
| `{TRAP_PORT}` | `10162` | `10163` | `10162` + site offset (a=0, b=1, c=2) |

**Derived values (automatic):**

| Value | Source | Example |
|-------|--------|---------|
| OTel endpoint | `http://<node-ip>:4317` via `HOST_IP` env var | Auto-resolved per pod |
| `k8s_namespace_name` metric label | `POD_NAMESPACE` env var | `simetra-site-a` |
| `service_instance_id` metric label | `PHYSICAL_HOSTNAME` env var | `simetra-k3s-a` |

---

## Part 1: Build SnmpCollector Image (Develop Machine)

### 1.1 Build SnmpCollector image

Navigate to the `ship/` folder (where the `Dockerfile` is located):

```bash
cd C:\Simetra\ship
```

```bash
docker build -t snmp-collector:v3.0 .
```

### 1.2 Save SnmpCollector image to tar

```bash
docker save snmp-collector:v3.0 -o snmp-collector-v3.0.tar
```

### 1.3 Transfer to all 3 sites

Copy the entire `ship/` folder to each prod machine via RDP.
Place it at `C:\Simetra\ship\` on each machine.

All paths in this guide assume `C:\Simetra\ship\` as the root location.

---

## Part 2: Enable Hyper-V (All 3 Sites — one-time)

Repeat on each Windows Server host (Site A, B, C).

### 2.1 Enable Hyper-V role

Open PowerShell as Administrator:

```powershell
Install-WindowsFeature -Name Hyper-V -IncludeManagementTools -Restart
```

The server will restart.

### 2.2 Verify Hyper-V is enabled

After restart, open PowerShell as Administrator:

```powershell
Get-WindowsFeature Hyper-V
```

Expected: `Install State` shows `Installed`.

---

## Part 3: Create Ubuntu Server VM (All 3 Sites — one-time)

**Setup order:**

1. **Site A:** 3.1 → 3.2 → 3.3 → 3.4 → 3.5 → 4A.1 → 4A.2 → 4A.3 → 4A.4 → 4A.5 → 4A.6
2. **Site B:** 3.2 → 3.3 → 3.4 → 3.5 → 4B.1 → 4B.2 → 4B.3 → 4B.4 (needs Site A running first)
3. **Site C:** 3.2 → 3.3 → 3.4 → 3.5 → 4B.1 → 4B.2 → 4B.3 → 4B.4 (needs Site A running first)

Step 3.1 (virtual switch) is done once. Each additional site repeats 3.2-3.5 (VM creation) then 4B (agent install).

### Site naming plan

| Site | VM Name | VM IP | Ubuntu Server Name | K3s Role |
|------|---------|-------|--------------------|----------|
| A | simetra-k3s-a | 172.20.0.10 | simetra-k3s-a | server |
| B | simetra-k3s-b | 172.20.0.10 | simetra-k3s-b | agent |
| C | simetra-k3s-c | 172.20.0.10 | simetra-k3s-c | agent |

> Each VM gets IP `172.20.0.10` on its local Hyper-V switch (each host has its own isolated virtual switch).
> Cross-site communication uses the host network — the VMs must be routable to each other.
> The **Ubuntu server name** becomes the K3s **node name** and appears in metrics as `service_instance_id`.
>
> **Testing on one host:** VMs share one switch — use different IPs per VM
> (e.g. `172.20.0.10` for Site A, `172.20.0.11` for Site B, `172.20.0.12` for Site C).

### 3.1 Create virtual switch

Open PowerShell as Administrator:

```powershell
New-VMSwitch -Name "SimetraSwitch" -SwitchType Internal
```

Configure NAT for the switch (allows VM to reach host network):

```powershell
New-NetIPAddress -IPAddress 172.20.0.1 -PrefixLength 24 -InterfaceAlias "vEthernet (SimetraSwitch)"
New-NetNat -Name "SimetraNAT" -InternalIPInterfaceAddressPrefix 172.20.0.0/24
```

Verify:

```powershell
Get-VMSwitch -Name "SimetraSwitch"
Get-NetNat -Name "SimetraNAT"
```

Expected: switch and NAT rule listed.

### 3.2 Create the VM

Replace `$VMName` with the site-specific name from the table above.

```powershell
$VMName = "simetra-k3s-a"   # Change per site: simetra-k3s-a, simetra-k3s-b, simetra-k3s-c
$ISOPath = "C:\Simetra\ship\infra\ubuntu\ubuntu-22.04.5-live-server-amd64.iso"
$VHDPath = "C:\VMs\$VMName\$VMName.vhdx"

# Create VM directory
New-Item -ItemType Directory -Path "C:\VMs\$VMName" -Force

# Create VM
New-VM -Name $VMName -MemoryStartupBytes 4GB -Generation 2 -NewVHDPath $VHDPath -NewVHDSizeBytes 40GB -SwitchName "SimetraSwitch"

# Configure VM
Set-VM -Name $VMName -ProcessorCount 2 -AutomaticStartAction Start -AutomaticStopAction ShutDown
Set-VMFirmware -VMName $VMName -EnableSecureBoot Off

# Mount Ubuntu ISO
Add-VMDvdDrive -VMName $VMName -Path $ISOPath

# Set boot order: DVD first (for install), then hard drive
$dvd = Get-VMDvdDrive -VMName $VMName
$hdd = Get-VMHardDiskDrive -VMName $VMName
Set-VMFirmware -VMName $VMName -BootOrder $dvd, $hdd
```

Verify the VM was created:

```powershell
Get-VM -Name $VMName
```

Expected: VM listed with `State: Off`.

### 3.3 Install Ubuntu Server

Start the VM and connect to console:

```powershell
Start-VM -Name $VMName
vmconnect localhost $VMName
```

Ubuntu installer steps (navigate with arrow keys, Enter to confirm, Tab to move between fields):

1. **Language** — select `English` → Done
2. **Keyboard** — select your layout (default: `English (US)`) → Done
3. **Type of installation** — select **Ubuntu Server (minimized)** → Done
4. **Network** — select the ethernet interface (e.g. `eth0`) → **Edit IPv4** → set method to **Manual**:
   - Subnet: `172.20.0.0/24`
   - Address: `172.20.0.10`
   - Gateway: `172.20.0.1`
   - Name servers: `172.20.0.1`
   - Search domains: (leave empty)
   - → Save → Done
5. **Proxy** — leave empty → Done
6. **Ubuntu archive mirror** — leave default → Done (mirror test will run; on offline machines it will fail and continue automatically)
7. **Storage** — select **Use an entire disk** → Done → Done → Confirm destructive action
8. **Profile setup**:
   - Your name: `simetra-k3s-a` (or `simetra-k3s-b`, `simetra-k3s-c` per site)
   - Your server's name: `simetra-k3s-a` (or `simetra-k3s-b`, `simetra-k3s-c` per site)
   - **This name becomes the K3s node name** and appears in metrics as `service_instance_id`
   - Pick a username: `simetra-k3s-a` (or `simetra-k3s-b`, `simetra-k3s-c` per site)
   - Choose a password: `simetra-k3s-a` (or `simetra-k3s-b`, `simetra-k3s-c` per site)
   - Confirm password: (same)
   - → Done
9. **Upgrade to Ubuntu Pro** — select **Skip for now** → Continue
10. **SSH Setup** — check **Install OpenSSH server** → Done
11. **Featured server snaps** — do NOT select any → Done
12. **Installation** — wait for install to complete → **Reboot Now**

### 3.4 Remove DVD drive after install

> After reboot, the VM may show "Failed unmounting /cdrom — please remove installation medium, then press ENTER".
> This is normal. Press **Enter** — the VM will boot from the hard drive.

Once the VM shows a login prompt, remove the DVD from **PowerShell** to prevent this on future reboots:

```powershell
Remove-VMDvdDrive -VMName $VMName -ControllerNumber 0 -ControllerLocation 1
```

Verify VM is running without DVD:

```powershell
Get-VM -Name $VMName | Select-Object Name, State
```

Expected: `State: Running`.

### 3.5 Verify SSH access from Windows host

```powershell
ssh <username>@172.20.0.10
```

> Keep this SSH session open — you will use it in Part 4A.2.

---

## Part 4: Install K3s

### Part 4A: Install K3s Server (Site A only — one-time)

#### 4A.1 Copy K3s files into the VM

Open a **second PowerShell window** on the Windows host (keep SSH session open):

```powershell
scp C:\Simetra\ship\infra\k3s\k3s <username>@172.20.0.10:~/
scp C:\Simetra\ship\infra\k3s\k3s-airgap-images-amd64.tar.zst <username>@172.20.0.10:~/
scp C:\Simetra\ship\infra\k3s\install.sh <username>@172.20.0.10:~/
```

Verify files arrived — switch to the **SSH session** (from step 3.5):

```bash
ls -lh ~/k3s ~/k3s-airgap-images-amd64.tar.zst ~/install.sh
```

Expected: 3 files listed with non-zero sizes.

#### 4A.2 Install K3s server (airgap mode)

Continue in the **same SSH session**:

Place files in required locations:

```bash
# Place airgap images where K3s expects them
sudo mkdir -p /var/lib/rancher/k3s/agent/images/
sudo cp ~/k3s-airgap-images-amd64.tar.zst /var/lib/rancher/k3s/agent/images/

# Place K3s binary in PATH
sudo cp ~/k3s /usr/local/bin/k3s
sudo chmod +x /usr/local/bin/k3s

# Make install script executable
chmod +x ~/install.sh
```

Run the installer in airgap mode:

```bash
INSTALL_K3S_SKIP_DOWNLOAD=true ~/install.sh
```

#### 4A.3 Verify K3s server is running

```bash
sudo k3s kubectl get nodes
```

Expected: one node (`simetra-k3s-a`) in `Ready` status.

#### 4A.4 Configure kubectl for your user

```bash
mkdir -p ~/.kube
sudo chmod 644 /etc/rancher/k3s/k3s.yaml
sudo cp /etc/rancher/k3s/k3s.yaml ~/.kube/config
sudo chown $(id -u):$(id -g) ~/.kube/config
```

Verify kubectl works:

```bash
kubectl get nodes
```

Expected: one node (`simetra-k3s-a`) in `Ready` status.

#### 4A.5 Get the node token (needed for agents)

```bash
sudo cat /var/lib/rancher/k3s/server/node-token
```

Save this token — you will need it for Site B and Site C.
The token is stored permanently at `/var/lib/rancher/k3s/server/node-token` — you can retrieve it again anytime by re-running this command.

#### 4A.6 Clean up install files

```bash
rm ~/k3s ~/k3s-airgap-images-amd64.tar.zst ~/install.sh
```

Verify:

```bash
ls ~/k3s ~/k3s-airgap-images-amd64.tar.zst ~/install.sh 2>&1
```

Expected: `No such file or directory` for all three.

### Part 4B: Install K3s Agent (Site B and Site C — one-time)

Repeat on each agent site.

#### 4B.1 Copy K3s files into the VM

SSH into the agent VM first, then open a **second PowerShell window** for SCP:

```bash
ssh <username>@<Site-B-IP>
```

From the **PowerShell window** on the Windows host:

```powershell
scp C:\Simetra\ship\infra\k3s\k3s <username>@172.20.0.10:~/
scp C:\Simetra\ship\infra\k3s\k3s-airgap-images-amd64.tar.zst <username>@172.20.0.10:~/
scp C:\Simetra\ship\infra\k3s\install.sh <username>@172.20.0.10:~/
```

Verify files arrived — switch to the **agent SSH session**:

```bash
ls -lh ~/k3s ~/k3s-airgap-images-amd64.tar.zst ~/install.sh
```

Expected: 3 files listed with non-zero sizes.

#### 4B.2 Install K3s agent (airgap mode)

Continue in the **same agent SSH session**:

Place files in required locations:

```bash
# Place airgap images where K3s expects them
sudo mkdir -p /var/lib/rancher/k3s/agent/images/
sudo cp ~/k3s-airgap-images-amd64.tar.zst /var/lib/rancher/k3s/agent/images/

# Place K3s binary in PATH
sudo cp ~/k3s /usr/local/bin/k3s
sudo chmod +x /usr/local/bin/k3s

# Make install script executable
chmod +x ~/install.sh
```

Run the installer as **agent** pointing to the Site A server.

> **IMPORTANT:** Paste the entire command as **one line**. If it breaks across multiple lines,
> the env vars won't apply and the installer will try to download from the internet (fails on offline machines).

```bash
INSTALL_K3S_SKIP_DOWNLOAD=true K3S_URL=https://<Site-A-IP>:6443 K3S_TOKEN=<node-token> ~/install.sh
```

> Replace `<Site-A-IP>` with the IP of Site A's VM that this agent can reach over the network.
> In production (separate hosts): this is the Windows host IP that NATs into the VM, not `172.20.0.10`.
> When all VMs share the same Hyper-V switch (e.g. testing on one host): use `172.20.0.10` directly.
> Replace `<node-token>` with the token from step 4A.5.

#### 4B.3 Verify the agent joined the cluster

From **Site A** (the server):

```bash
kubectl get nodes
```

Expected: the new agent node appears in `Ready` status. After both agents join:

```
NAME             STATUS   ROLES                  AGE
simetra-k3s-a    Ready    control-plane,master   10m
simetra-k3s-b    Ready    <none>                 2m
simetra-k3s-c    Ready    <none>                 1m
```

#### 4B.4 Clean up install files

```bash
rm ~/k3s ~/k3s-airgap-images-amd64.tar.zst ~/install.sh
```

Verify:

```bash
ls ~/k3s ~/k3s-airgap-images-amd64.tar.zst ~/install.sh 2>&1
```

Expected: `No such file or directory` for all three.

---

## Part 5: Load Container Images (all 3 nodes)

Container images must be imported on **every node** (K3s has no shared registry).

### 5.1 Copy files into the Site A VM

From the **PowerShell window** on the Windows host:

```powershell
scp C:\Simetra\ship\snmp-collector-v3.0.tar <username>@172.20.0.10:~/
scp C:\Simetra\ship\otel-collector-0.120.0.tar <username>@172.20.0.10:~/
scp -r C:\Simetra\ship\deploy\ <username>@172.20.0.10:~/deploy/
```

Verify files arrived — switch to the **Site A SSH session**:

```bash
ls -lh ~/snmp-collector-v3.0.tar ~/otel-collector-0.120.0.tar && ls ~/deploy/
```

Expected: 2 tar files with non-zero sizes and deploy/ contents listed.

### 5.2 Import images on Site A

```bash
sudo k3s ctr images import ~/snmp-collector-v3.0.tar
sudo k3s ctr images import ~/otel-collector-0.120.0.tar
```

Verify:

```bash
sudo k3s ctr images ls | grep -E "snmp-collector|otel"
```

Expected: both images listed.

### 5.3 Import images on Site B and Site C

Copy tar files to each agent VM and import:

```bash
# From Windows host:
scp C:\Simetra\ship\snmp-collector-v3.0.tar <username>@<Site-B-IP>:~/
scp C:\Simetra\ship\otel-collector-0.120.0.tar <username>@<Site-B-IP>:~/

# SSH into Site B VM:
sudo k3s ctr images import ~/snmp-collector-v3.0.tar
sudo k3s ctr images import ~/otel-collector-0.120.0.tar
rm ~/snmp-collector-v3.0.tar ~/otel-collector-0.120.0.tar
```

Repeat for Site C.

### 5.4 Clean up transfer files on Site A

```bash
rm ~/snmp-collector-v3.0.tar ~/otel-collector-0.120.0.tar
```

### 5.5 Verify images on all nodes

From Site A:

```bash
sudo k3s ctr images ls | grep -E "snmp-collector|otel"
```

Expected: both images listed. Repeat on each agent node via SSH to confirm images were imported there too.

### 5.6 Prepare site deploy copies

Create a copy of the deploy manifests for each site **before** modifying anything.
This preserves the original `~/deploy/` as a clean template.

```bash
# Example for Site A:
cp -r ~/deploy ~/deploy-simetra-site-a

# Example for Site B:
cp -r ~/deploy ~/deploy-simetra-site-b
```

Verify:

```bash
ls ~/deploy-simetra-site-a/deployment.yaml ~/deploy-simetra-site-b/deployment.yaml
```

Expected: both files exist.

> Create all site copies now. Parts 6 and 7 will modify files in-place.

---

## Part 6: Deploy OTel Collector (once — simetra-infra)

All kubectl commands run from the Site A VM (the K3s server).

### 6.1 Create infrastructure namespace

```bash
kubectl create namespace simetra-infra
```

### 6.2 Deploy OTel Collector ConfigMap

**Before applying**, edit `~/deploy/otel-collector-configmap.yaml` and update the export
endpoints to match your organization's infrastructure:
- `prometheusremotewrite` endpoint → your Prometheus remote write URL
- `elasticsearch` endpoint → your Elasticsearch URL

Then update the namespace and apply:

```bash
sed -i 's|namespace: simetra|namespace: simetra-infra|g' ~/deploy/otel-collector-configmap.yaml
kubectl apply -f ~/deploy/otel-collector-configmap.yaml
```

> The file `otel-collector-deployment.yaml` in `~/deploy/` is not used — the DaemonSet
> below replaces it for multi-site deployment.

### 6.3 Deploy OTel Collector as DaemonSet

```bash
cat <<'EOF' | kubectl apply -f -
apiVersion: apps/v1
kind: DaemonSet
metadata:
  name: otel-collector
  namespace: simetra-infra
  labels:
    app: otel-collector
spec:
  selector:
    matchLabels:
      app: otel-collector
  template:
    metadata:
      labels:
        app: otel-collector
    spec:
      containers:
      - name: otel-collector
        image: otel/opentelemetry-collector-contrib:0.120.0
        imagePullPolicy: Never
        ports:
        - containerPort: 4317
          hostPort: 4317
          protocol: TCP
          name: otlp-grpc
        volumeMounts:
        - name: config
          mountPath: /etc/otelcol-contrib/config.yaml
          subPath: config.yaml
          readOnly: true
      volumes:
      - name: config
        configMap:
          name: otel-collector-config
EOF
```

### 6.4 Verify OTel Collector (one pod per node)

```bash
kubectl -n simetra-infra get pods -o wide
```

Expected:

```
NAME                   READY   STATUS    NODE
otel-collector-xxxxx   1/1     Running   simetra-k3s-a
otel-collector-yyyyy   1/1     Running   simetra-k3s-b
otel-collector-zzzzz   1/1     Running   simetra-k3s-c
```

---

## Part 7: Deploy SnmpCollector (repeat per site)

Repeat this entire section for each site, substituting the placeholders from the
Site Configuration table.

> **Site A:** `{NAMESPACE}` = `simetra-site-a`, `{PREFERRED_NODE}` = `simetra-k3s-a`, `{TRAP_PORT}` = `10162`
> **Site B:** `{NAMESPACE}` = `simetra-site-b`, `{PREFERRED_NODE}` = `simetra-k3s-b`, `{TRAP_PORT}` = `10163`

### 7.1 Update namespace in all manifests

```bash
# Example for Site A: ~/deploy-simetra-site-a/
# Example for Site B: ~/deploy-simetra-site-b/
for f in rbac.yaml snmp-collector-config.yaml simetra-oid-metric-map.yaml simetra-oid-command-map.yaml simetra-devices.yaml simetra-tenants.yaml service.yaml deployment.yaml; do
  sed -i 's|namespace: simetra|namespace: {NAMESPACE}|g' ~/deploy-{NAMESPACE}/$f
done
```

### 7.2 Create namespace, service account, and RBAC

```bash
# Example for Site A: kubectl create namespace simetra-site-a
kubectl create namespace {NAMESPACE}
kubectl -n {NAMESPACE} create serviceaccount simetra-sa
kubectl apply -f ~/deploy-{NAMESPACE}/rbac.yaml
```

### 7.3 Configure snmp-collector-config.yaml

Edit `~/deploy-{NAMESPACE}/snmp-collector-config.yaml` (e.g. `~/deploy-simetra-site-a/snmp-collector-config.yaml`).

Update the following sections (keep all other sections unchanged).
**Remove the `Otlp.Endpoint` field** — it will be overridden by an env var in the
deployment (see step 7.6):

```json
  "Otlp": {
    "ServiceName": "snmp-collector"
  },
```

Update the trap listener port:

```json
  "SnmpListener": {
    "BindAddress": "0.0.0.0",
    "Port": {TRAP_PORT}
  },
```

Update the lease namespace and add preferred node:

```json
  "Lease": {
    "Name": "snmp-collector-leader",
    "Namespace": "{NAMESPACE}",
    "DurationSeconds": 15,
    "RenewIntervalSeconds": 10,
    "PreferredNode": "{PREFERRED_NODE}"
  },
```

> Only modify these 3 sections. Leave all other sections (CorrelationJob,
> SnmpHeartbeatJob, SnapshotJob, Liveness, Channels, Logging) unchanged.

Example for Site A:
- `{TRAP_PORT}` = `10162`
- `{NAMESPACE}` = `simetra-site-a`
- `{PREFERRED_NODE}` = `simetra-k3s-a`

Example for Site B:
- `{TRAP_PORT}` = `10163`
- `{NAMESPACE}` = `simetra-site-b`
- `{PREFERRED_NODE}` = `simetra-k3s-b`

> `Lease.Namespace` must match `{NAMESPACE}` exactly.
> `PreferredNode` must match the Ubuntu server name from Part 3.3 (case-sensitive).
> `SnmpListener.Port` must be unique per site (10162, 10163, etc.).

### 7.4 Configure site-specific devices and tenants

Each site has its own SNMP hardware with different IP addresses. The device and tenant
configurations must reflect the actual devices at each site.

**`simetra-devices.yaml`** — Edit `~/deploy-{NAMESPACE}/simetra-devices.yaml`
(e.g. `~/deploy-simetra-site-a/simetra-devices.yaml`) with this site's devices only.

- Each device entry has a unique IP address pointing to hardware at this site
- Device names should include site context if the same device type exists at multiple sites
  (e.g. `Simetra.OBP-SiteA` vs `Simetra.OBP-SiteB`)
- Do NOT include devices from other sites — each namespace monitors only its own hardware

**`simetra-tenants.yaml`** — Edit `~/deploy-{NAMESPACE}/simetra-tenants.yaml`
(e.g. `~/deploy-simetra-site-a/simetra-tenants.yaml`) with this site's tenant definitions.

- Tenants reference device IPs and OIDs specific to this site's hardware
- Tenant IDs should include site context to distinguish them in Grafana
  (e.g. `tenant-0-site-a` vs `tenant-0-site-b`)
- Thresholds and command slots may differ per site based on the physical equipment

**`simetra-oid-metric-map.yaml`** and **`simetra-oid-command-map.yaml`** — typically
identical across sites (same device types use the same OIDs). Only customize if sites
have different device models with different OID trees.

> Each site is an independent monitoring domain. Devices, tenants, thresholds, and
> commands are all scoped to the site namespace. Metrics appear in Grafana with
> `k8s_namespace_name` distinguishing which site produced them.

### 7.5 Apply ConfigMaps

```bash
# Example for Site A: ~/deploy-simetra-site-a/
kubectl apply -f ~/deploy-{NAMESPACE}/snmp-collector-config.yaml
kubectl apply -f ~/deploy-{NAMESPACE}/simetra-oid-metric-map.yaml
kubectl apply -f ~/deploy-{NAMESPACE}/simetra-oid-command-map.yaml
kubectl apply -f ~/deploy-{NAMESPACE}/simetra-devices.yaml
kubectl apply -f ~/deploy-{NAMESPACE}/simetra-tenants.yaml
```

### 7.6 Update deployment.yaml

Edit `~/deploy-{NAMESPACE}/deployment.yaml` (e.g. `~/deploy-simetra-site-a/deployment.yaml`):

**1. Update the image tag:**

```bash
# Example for Site A: ~/deploy-simetra-site-a/deployment.yaml
sed -i 's|image: snmp-collector:local|image: snmp-collector:v3.0|' ~/deploy-{NAMESPACE}/deployment.yaml
```

**2. Add `HOST_IP` and `Otlp__Endpoint` env vars.** Add these to the `env:` section:

```yaml
        - name: HOST_IP
          valueFrom:
            fieldRef:
              fieldPath: status.hostIP
        - name: Otlp__Endpoint
          value: "http://$(HOST_IP):4317"
```

> `HOST_IP` resolves to the node's IP via Downward API.
> `Otlp__Endpoint` uses K8s env var expansion `$(HOST_IP)` to route to the node-local OTel Collector.
> This overrides the `Otlp.Endpoint` config value at runtime.

**3. Update the SNMP container port** to match `{TRAP_PORT}`:

```yaml
        - containerPort: {TRAP_PORT}    # Example Site A: 10162, Site B: 10163
          name: snmp
          protocol: UDP
```

### 7.7 Update service.yaml

Edit `~/deploy-{NAMESPACE}/service.yaml` (e.g. `~/deploy-simetra-site-a/service.yaml`).

Update the SNMP trap port to match `{TRAP_PORT}`:

```yaml
  - name: snmp-trap
    port: {TRAP_PORT}           # Example Site A: 10162, Site B: 10163
    protocol: UDP
    targetPort: {TRAP_PORT}     # Must match containerPort in deployment.yaml
```

### 7.8 Pre-apply checklist

**Before applying, verify all files are correct.** Mismatched values cause silent failures
(e.g., wrong `Lease.Namespace` causes the leader election to fail with no error in logs).

**`snmp-collector-config.yaml`:**

```bash
# Example for Site A: ~/deploy-simetra-site-a/snmp-collector-config.yaml
grep -E '"Namespace"|"PreferredNode"|"Port"|"Endpoint"' ~/deploy-{NAMESPACE}/snmp-collector-config.yaml
```

- [ ] `"Namespace": "{NAMESPACE}"` — must match K8s namespace exactly (e.g. `simetra-site-a`)
- [ ] `"PreferredNode": "{PREFERRED_NODE}"` — must match node name exactly (e.g. `simetra-k3s-a`)
- [ ] `"Port": {TRAP_PORT}` — must be unique per site (e.g. `10162`)
- [ ] `"Endpoint"` line is **absent** — OTel endpoint is set via env var, not config

> **If `Lease.Namespace` is wrong:** the LeaderElector will silently fail to create the
> lease. The pod runs as follower forever with no error in logs. This is the most common
> misconfiguration in multi-site deployment.

**`deployment.yaml`:**

```bash
grep -E 'containerPort|HOST_IP|Otlp__Endpoint' ~/deploy-{NAMESPACE}/deployment.yaml
```

- [ ] `containerPort` (snmp) = `{TRAP_PORT}` (e.g. `10162`)
- [ ] `HOST_IP` env var present (fieldRef: `status.hostIP`)
- [ ] `Otlp__Endpoint` env var present (`http://$(HOST_IP):4317`)

**`service.yaml`:**

```bash
grep -E 'port:|targetPort:' ~/deploy-{NAMESPACE}/service.yaml
```

- [ ] `port` and `targetPort` (snmp-trap) = `{TRAP_PORT}` — must match `containerPort` in deployment.yaml

### 7.9 Deploy

```bash
# Example for Site A: ~/deploy-simetra-site-a/
kubectl apply -f ~/deploy-{NAMESPACE}/service.yaml
kubectl apply -f ~/deploy-{NAMESPACE}/deployment.yaml
```

### 7.10 Post-deploy verification

**Immediate check (within 30 seconds):**

```bash
# Wait for pod to start
kubectl -n {NAMESPACE} rollout status deployment/snmp-collector --timeout=60s

# Check lease was created — this is the critical test
kubectl -n {NAMESPACE} get leases
```

- [ ] `snmp-collector-leader` lease exists
- [ ] If `PreferredNode` is configured: `snmp-collector-leader-preferred` lease exists

> **If no lease after 30 seconds:** the most likely cause is `Lease.Namespace` in the
> ConfigMap doesn't match the K8s namespace. Check with:
> ```bash
> kubectl -n {NAMESPACE} get configmap snmp-collector-config -o jsonpath='{.data.appsettings\.k8s\.json}' | grep '"Namespace"'
> ```
> The value must be `{NAMESPACE}` (e.g. `simetra-site-a`), not `simetra`.

**Full verification:**

```bash
# Example for Site A: replace {NAMESPACE} with simetra-site-a

# Pods spread across nodes
kubectl -n {NAMESPACE} get pods -o wide

# Leader identity
kubectl -n {NAMESPACE} get lease snmp-collector-leader -o jsonpath='{.spec.holderIdentity}'

# Preferred heartbeat lease
kubectl -n {NAMESPACE} get lease snmp-collector-leader-preferred -o jsonpath='{.spec.holderIdentity}'

# Env vars
for pod in $(kubectl -n {NAMESPACE} get pods -l app=snmp-collector -o name); do
  echo "$pod:"
  kubectl -n {NAMESPACE} exec $pod -- printenv PHYSICAL_HOSTNAME POD_NAMESPACE HOST_IP Otlp__Endpoint
  echo ""
done

# Startup logs
kubectl -n {NAMESPACE} logs -l app=snmp-collector --tail=5 | grep "Startup sequence"

# Leader confirmed in logs (not stuck on "follower")
kubectl -n {NAMESPACE} logs -l app=snmp-collector --tail=100 | grep "Acquired leadership"

# Poll jobs firing
kubectl -n {NAMESPACE} logs -l app=snmp-collector --tail=50 | grep "metric-poll"
```

Expected:
- 3 pods, one per node
- Leader on `{PREFERRED_NODE}` (e.g. `simetra-k3s-a` for Site A)
- Logs show `Acquired leadership` (not stuck on `follower`)
- Each pod shows its node name, `{NAMESPACE}`, node IP for `HOST_IP`, and `http://<node-ip>:4317` for `Otlp__Endpoint`

---

## Deployment Manifest Reference

**Pod anti-affinity** — ensures one SnmpCollector pod per node per namespace:

```yaml
affinity:
  podAntiAffinity:
    requiredDuringSchedulingIgnoredDuringExecution:
    - labelSelector:
        matchLabels:
          app: snmp-collector
      topologyKey: kubernetes.io/hostname
```

> Anti-affinity is namespace-scoped by default. Site A and Site B pods do not interfere
> with each other — both can run on the same node.

**Environment variables** — injected via Kubernetes Downward API:

```yaml
env:
- name: PHYSICAL_HOSTNAME          # Node name → preferred leader matching
  valueFrom:
    fieldRef:
      fieldPath: spec.nodeName
- name: POD_NAMESPACE              # Namespace → k8s_namespace_name metric label
  valueFrom:
    fieldRef:
      fieldPath: metadata.namespace
- name: HOST_IP                    # Node IP → used by Otlp__Endpoint
  valueFrom:
    fieldRef:
      fieldPath: status.hostIP
- name: Otlp__Endpoint             # Routes to node-local OTel Collector
  value: "http://$(HOST_IP):4317"
```

**Metric labels in Prometheus:**

| Label | Source | Identifies |
|-------|--------|------------|
| `k8s_namespace_name` | `POD_NAMESPACE` env var | Which logical site (simetra-site-a, simetra-site-b) |
| `service_instance_id` | `PHYSICAL_HOSTNAME` env var | Which physical node (simetra-k3s-a, simetra-k3s-b, simetra-k3s-c) |
| `k8s_pod_name` | `HOSTNAME` env var | Which pod instance |

---

## Teardown

### Remove a single site

```bash
# Example: kubectl delete namespace simetra-site-a
kubectl delete namespace {NAMESPACE}
```

This removes all SnmpCollector resources, ConfigMaps, leases, and RBAC for that site.

### Remove OTel Collector

```bash
kubectl delete namespace simetra-infra
```

### Remove everything

```bash
kubectl delete namespace simetra-site-a simetra-site-b simetra-infra
```

---

## Maintenance

### Pause and resume work

**To pause** — shut down VMs gracefully from PowerShell:

```powershell
Stop-VM -Name "simetra-k3s-a"
Stop-VM -Name "simetra-k3s-b"
Stop-VM -Name "simetra-k3s-c"
```

**To resume** — start VMs and verify cluster:

```powershell
Start-VM -Name "simetra-k3s-a"
Start-VM -Name "simetra-k3s-b"
Start-VM -Name "simetra-k3s-c"
```

Wait ~30 seconds, then SSH into Site A and verify:

```bash
kubectl get nodes
```

Expected: all nodes in `Ready` status. Continue from where you left off.

### VM auto-start on server boot

K3s starts automatically inside each VM (systemd service). The VMs are configured
to start automatically on host boot (`AutomaticStartAction: Start` set in Part 3.2).

### Check K3s status

On the server (Site A):

```bash
sudo systemctl status k3s
```

On agents (Site B, C):

```bash
sudo systemctl status k3s-agent
```

### Check cluster health

From Site A:

```bash
kubectl get nodes
kubectl -n simetra-site-a get pods -o wide
kubectl -n simetra-site-b get pods -o wide
kubectl -n simetra-infra get pods -o wide
```

### Update SnmpCollector

1. Build new image on develop machine with new tag (e.g. `snmp-collector:v3.1`)
2. Save to tar, transfer to **all 3 VMs** via SCP
3. Import on each VM: `sudo k3s ctr images import snmp-collector-v3.1.tar`
4. For each site namespace:
   ```bash
   # Example for Site A:
   sed -i 's|snmp-collector:v3.0|snmp-collector:v3.1|' ~/deploy-simetra-site-a/deployment.yaml
   kubectl apply -f ~/deploy-simetra-site-a/deployment.yaml
   ```

### Change preferred leader for a site

Edit the ConfigMap:

```bash
# Example: kubectl edit configmap snmp-collector-config -n simetra-site-a
kubectl edit configmap snmp-collector-config -n {NAMESPACE}
```

Change `PreferredNode` to the desired node name (e.g. `simetra-k3s-b`).
Then restart the pods to pick up the config change:

```bash
kubectl rollout restart deployment/snmp-collector -n {NAMESPACE}
```

The pod on the new preferred node will acquire leadership within ~30 seconds.

### Add a new site

1. Choose next trap port (e.g. `10164` for Site C)
2. Create a new deploy copy: `cp -r ~/deploy ~/deploy-simetra-site-c`
3. Repeat Part 7 with `{NAMESPACE}` = `simetra-site-c`, `{PREFERRED_NODE}` = `simetra-k3s-c`, `{TRAP_PORT}` = `10164`
4. No changes needed to OTel Collector — DaemonSet already runs on all nodes

### VM resource adjustment

Shut down the VM, adjust in Hyper-V Manager, restart:

```powershell
Stop-VM -Name "simetra-k3s-a"
Set-VM -Name "simetra-k3s-a" -MemoryStartupBytes 8GB -ProcessorCount 4
Start-VM -Name "simetra-k3s-a"
```
