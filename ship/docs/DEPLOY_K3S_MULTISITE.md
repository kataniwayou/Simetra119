# SnmpCollector Offline Deployment Guide (K3s Multi-Site)

## Overview

This guide deploys the SnmpCollector system across **3 sites** using a single K3s cluster
spanning 3 nodes. Each site runs a Linux VM on a Windows Server 2019 host via Hyper-V.
All required files are pre-included in the `ship/` folder — no internet access is needed.

**Architecture:**

```
Site A — Windows Server 2019 → Linux VM → K3s (server) → pod (leader)
Site B — Windows Server 2019 → Linux VM → K3s (agent)  → pod (follower)
Site C — Windows Server 2019 → Linux VM → K3s (agent)  → pod (follower)
```

**Key design:**
- One K3s cluster, three nodes (one per site)
- Pod anti-affinity ensures one pod per node
- Preferred leader election: the pod on Site A (closest to SNMP devices) gets leadership priority
- `PHYSICAL_HOSTNAME` env var identifies each pod's node for preferred leader logic
- `POD_NAMESPACE` env var provides namespace for metric labeling

**Target environment (per site):**
- Host: Windows Server 2019 Standard
- Kubernetes: K3s v1.31.13+k3s1 (lightweight, production-grade, CNCF certified)
- Runtime: Hyper-V VM running Ubuntu Server 22.04 LTS

**Components deployed in K3s:**
- OTel Collector (receives metrics/logs from snmp-collector, exports to external infrastructure)
- SnmpCollector (3 replicas with leader election and preferred leader)

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
└── snmp-collector-v2.6.tar                            # SnmpCollector container image
```

**Network requirements (between the 3 Linux VMs):**

| Port | Protocol | Purpose |
|------|----------|---------|
| 6443 | TCP | K3s API server (agents → server) |
| 8472 | UDP | Flannel VXLAN (pod-to-pod networking) |
| 10250 | TCP | Kubelet metrics |

---

## Part 1: Build SnmpCollector Image (Develop Machine)

### 1.1 Build SnmpCollector image

From the `ship/` folder root:

```bash
docker build -t snmp-collector:v2.6 .
```

### 1.2 Save SnmpCollector image to tar

```bash
docker save snmp-collector:v2.6 -o snmp-collector-v2.6.tar
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

Repeat on each site. **Use a different VM name and IP for each site.**

### Site naming plan

| Site | VM Name | VM IP | Ubuntu Server Name | K3s Role |
|------|---------|-------|--------------------|----------|
| A | simetra-k3s-a | 172.20.0.10 | simetra-k3s-a | server |
| B | simetra-k3s-b | 172.20.0.10 | simetra-k3s-b | agent |
| C | simetra-k3s-c | 172.20.0.10 | simetra-k3s-c | agent |

> Each VM gets IP `172.20.0.10` on its local Hyper-V switch (each host has its own isolated virtual switch).
> Cross-site communication uses the host network — the VMs must be routable to each other.
> The **Ubuntu server name** becomes the K3s **node name** and appears in metrics as `service_instance_id`.

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

### 3.3 Install Ubuntu Server

Start the VM and connect to console:

```powershell
Start-VM -Name $VMName
vmconnect localhost $VMName
```

During Ubuntu installation:
1. Select **Ubuntu Server (minimized)** — no GUI needed
2. Configure network manually:
   - IP: `172.20.0.10`
   - Subnet: `255.255.255.0` (`/24`)
   - Gateway: `172.20.0.1`
   - DNS: `172.20.0.1`
3. On the **Profile setup** screen:
   - Server name: use site-specific name (`simetra-k3s-a`, `simetra-k3s-b`, or `simetra-k3s-c`)
   - **This name becomes the K3s node name** and appears in metrics as `service_instance_id`
   - Set username and password
4. Enable **OpenSSH server** when prompted
5. Complete installation and reboot

### 3.4 Remove DVD drive after install

```powershell
Remove-VMDvdDrive -VMName "simetra-k3s-a" -ControllerNumber 0 -ControllerLocation 1
```

### 3.5 Verify SSH access from Windows host

```powershell
ssh <username>@172.20.0.10
```

---

## Part 4: Install K3s

### Part 4A: Install K3s Server (Site A only — one-time)

#### 4A.1 Copy K3s files into the VM

From the Windows host PowerShell:

```powershell
scp C:\Simetra\ship\infra\k3s\k3s <username>@172.20.0.10:~/
scp C:\Simetra\ship\infra\k3s\k3s-airgap-images-amd64.tar.zst <username>@172.20.0.10:~/
scp C:\Simetra\ship\infra\k3s\install.sh <username>@172.20.0.10:~/
```

#### 4A.2 Install K3s server (airgap mode)

SSH into the Site A VM:

```bash
ssh <username>@172.20.0.10
```

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
sudo cp /etc/rancher/k3s/k3s.yaml ~/.kube/config
sudo chown $(id -u):$(id -g) ~/.kube/config
kubectl get nodes
```

#### 4A.5 Get the node token (needed for agents)

```bash
sudo cat /var/lib/rancher/k3s/server/node-token
```

Save this token — you will need it for Site B and Site C.

#### 4A.6 Get the server IP

The server API URL is `https://<Site-A-VM-routable-IP>:6443`. This is the IP that
Site B and Site C VMs can reach over the network (not the local 172.20.0.10).

#### 4A.7 Clean up install files

```bash
rm ~/k3s ~/k3s-airgap-images-amd64.tar.zst ~/install.sh
```

### Part 4B: Install K3s Agent (Site B and Site C — one-time)

Repeat on each agent site.

#### 4B.1 Copy K3s files into the VM

From the Windows host PowerShell:

```powershell
scp C:\Simetra\ship\infra\k3s\k3s <username>@172.20.0.10:~/
scp C:\Simetra\ship\infra\k3s\k3s-airgap-images-amd64.tar.zst <username>@172.20.0.10:~/
scp C:\Simetra\ship\infra\k3s\install.sh <username>@172.20.0.10:~/
```

#### 4B.2 Install K3s agent (airgap mode)

SSH into the agent VM:

```bash
ssh <username>@172.20.0.10
```

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

Run the installer as **agent** pointing to the Site A server:

```bash
INSTALL_K3S_SKIP_DOWNLOAD=true K3S_URL=https://<Site-A-IP>:6443 K3S_TOKEN=<node-token> ~/install.sh
```

> Replace `<Site-A-IP>` with the routable IP of Site A's VM.
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

---

## Part 5: Deploy Application (from Site A)

All kubectl commands run from the Site A VM (the K3s server).

### 5.1 Copy application files into the Site A VM

From the Site A Windows host:

```powershell
scp C:\Simetra\ship\snmp-collector-v2.6.tar <username>@172.20.0.10:~/
scp C:\Simetra\ship\otel-collector-0.120.0.tar <username>@172.20.0.10:~/
scp -r C:\Simetra\ship\deploy\ <username>@172.20.0.10:~/deploy/
```

### 5.2 Load container images into K3s (all 3 nodes)

The container images must be imported on **every node** (K3s has no shared registry).

**Site A** (SSH into Site A VM):

```bash
sudo k3s ctr images import ~/snmp-collector-v2.6.tar
sudo k3s ctr images import ~/otel-collector-0.120.0.tar
```

**Site B and Site C** — copy the tar files to each agent VM and import:

```bash
# From Site A Windows host (or wherever the tars are):
scp C:\Simetra\ship\snmp-collector-v2.6.tar <username>@<Site-B-IP>:~/
scp C:\Simetra\ship\otel-collector-0.120.0.tar <username>@<Site-B-IP>:~/

# SSH into Site B VM:
sudo k3s ctr images import ~/snmp-collector-v2.6.tar
sudo k3s ctr images import ~/otel-collector-0.120.0.tar
rm ~/snmp-collector-v2.6.tar ~/otel-collector-0.120.0.tar
```

Repeat for Site C.

Verify on Site A:

```bash
sudo k3s ctr images ls | grep -E "snmp-collector|otel"
```

### 5.3 Create namespace and service account

```bash
kubectl create namespace simetra-site-a
kubectl -n simetra-site-a create serviceaccount simetra-sa
```

### 5.4 Apply RBAC

Update the namespace in `~/deploy/rbac.yaml` before applying:

```bash
sed -i 's|namespace: simetra|namespace: simetra-site-a|g' ~/deploy/rbac.yaml
kubectl apply -f ~/deploy/rbac.yaml
```

### 5.5 Deploy OTel Collector

Update the namespace in OTel manifests:

```bash
sed -i 's|namespace: simetra|namespace: simetra-site-a|g' ~/deploy/otel-collector-configmap.yaml
sed -i 's|namespace: simetra|namespace: simetra-site-a|g' ~/deploy/otel-collector-deployment.yaml
kubectl apply -f ~/deploy/otel-collector-configmap.yaml
kubectl apply -f ~/deploy/otel-collector-deployment.yaml
```

Wait for OTel Collector to be ready:

```bash
kubectl -n simetra-site-a rollout status deployment/otel-collector
```

### 5.6 Update namespace in all SnmpCollector manifests

```bash
for f in snmp-collector-config.yaml simetra-oid-metric-map.yaml simetra-oid-command-map.yaml simetra-devices.yaml simetra-tenants.yaml service.yaml deployment.yaml; do
  sed -i 's|namespace: simetra|namespace: simetra-site-a|g' ~/deploy/$f
done
```

### 5.7 Apply SnmpCollector ConfigMaps

```bash
kubectl apply -f ~/deploy/snmp-collector-config.yaml
kubectl apply -f ~/deploy/simetra-oid-metric-map.yaml
kubectl apply -f ~/deploy/simetra-oid-command-map.yaml
kubectl apply -f ~/deploy/simetra-devices.yaml
kubectl apply -f ~/deploy/simetra-tenants.yaml
```

### 5.8 Configure PreferredNode and Lease namespace

Edit the ConfigMap to set the preferred leader node and correct lease namespace:

```bash
kubectl edit configmap snmp-collector-config -n simetra-site-a
```

Update the `"Lease"` section:

```json
"Lease": {
    "Name": "snmp-collector-leader",
    "Namespace": "simetra-site-a",
    "DurationSeconds": 15,
    "RenewIntervalSeconds": 10,
    "PreferredNode": "simetra-k3s-a"
}
```

> `Namespace` must match the K8s namespace you created (`simetra-site-a`).
> `PreferredNode` must match the Ubuntu server name from Part 3.3 exactly (case-sensitive).
> This is the value that `PHYSICAL_HOSTNAME` env var resolves to via `spec.nodeName`.
> When set, the pod running on this node gets leadership priority.
> When absent or empty, standard fair election applies (backward compatible).

### 5.9 Update image tag in deployment.yaml

```bash
sed -i 's|image: snmp-collector:local|image: snmp-collector:v2.6|' ~/deploy/deployment.yaml
```

`imagePullPolicy: Never` must remain (no registry available).

### 5.10 Deploy SnmpCollector

```bash
kubectl apply -f ~/deploy/service.yaml
kubectl apply -f ~/deploy/deployment.yaml
```

---

## Part 6: Verify

### 6.1 Check pods are spread across nodes

```bash
kubectl -n simetra-site-a get pods -o wide
```

Expected: 3 `snmp-collector` pods, **one on each node** (enforced by pod anti-affinity):

```
NAME                              READY   STATUS    NODE
snmp-collector-xxx-aaa            1/1     Running   simetra-k3s-a
snmp-collector-xxx-bbb            1/1     Running   simetra-k3s-b
snmp-collector-xxx-ccc            1/1     Running   simetra-k3s-c
```

### 6.2 Check startup logs

```bash
kubectl -n simetra-site-a logs -l app=snmp-collector --tail=5 | grep "Startup sequence"
```

Expected output:

```
Startup sequence: OidMapWatcher=145 -> DeviceWatcher=3 -> CommandMapWatcher=13 -> TenantWatcher=4
```

### 6.3 Verify leader election

```bash
kubectl -n simetra-site-a get lease snmp-collector-leader -o jsonpath='{.spec.holderIdentity}'
```

Exactly one pod name must appear. With `PreferredNode` configured, the pod on
`simetra-k3s-a` should hold leadership at steady state.

### 6.4 Verify preferred heartbeat lease

```bash
kubectl -n simetra-site-a get lease snmp-collector-leader-preferred -o jsonpath='{.spec.holderIdentity}'
```

The pod on the preferred node (`simetra-k3s-a`) should be stamping this lease.

### 6.5 Verify node names and namespace in env vars

```bash
for pod in $(kubectl -n simetra-site-a get pods -l app=snmp-collector -o name); do
  echo "$pod:"
  kubectl -n simetra-site-a exec $pod -- printenv PHYSICAL_HOSTNAME POD_NAMESPACE
  echo ""
done
```

Expected: each pod shows its node name and `simetra-site-a` namespace.

### 6.6 Verify poll jobs are firing

```bash
kubectl -n simetra-site-a logs -l app=snmp-collector --tail=50 | grep "metric-poll"
```

Must show poll job execution lines.

### 6.7 Clean up transfer files

```bash
rm ~/snmp-collector-v2.6.tar ~/otel-collector-0.120.0.tar
```

---

## Deployment Manifest Reference

Key sections in `deploy/deployment.yaml` relevant to multi-site operation:

**Pod anti-affinity** — ensures one pod per node:

```yaml
affinity:
  podAntiAffinity:
    requiredDuringSchedulingIgnoredDuringExecution:
    - labelSelector:
        matchLabels:
          app: snmp-collector
      topologyKey: kubernetes.io/hostname
```

**Environment variables** — injected via Kubernetes Downward API:

```yaml
env:
- name: PHYSICAL_HOSTNAME          # Node name → used for preferred leader matching
  valueFrom:
    fieldRef:
      fieldPath: spec.nodeName
- name: POD_NAMESPACE              # Namespace → appears on all metrics as k8s_namespace_name
  valueFrom:
    fieldRef:
      fieldPath: metadata.namespace
```

**ConfigMap Lease section** — preferred leader configuration:

```json
"Lease": {
    "Name": "snmp-collector-leader",
    "Namespace": "simetra-site-a",
    "DurationSeconds": 15,
    "RenewIntervalSeconds": 10,
    "PreferredNode": "simetra-k3s-a"
}
```

> `Namespace` must match the K8s namespace where the deployment runs.
> `PreferredNode` is compared against `PHYSICAL_HOSTNAME` at pod startup.
> The matching pod gets leadership priority via the two-lease mechanism.

---

## Teardown

Remove all SnmpCollector resources:

```bash
kubectl delete deployment snmp-collector -n simetra-site-a
kubectl delete service snmp-collector -n simetra-site-a
kubectl delete configmap snmp-collector-config simetra-oid-metric-map simetra-oid-command-map simetra-devices simetra-tenants -n simetra-site-a
```

Remove OTel Collector:

```bash
kubectl delete deployment otel-collector -n simetra-site-a
kubectl delete service otel-collector -n simetra-site-a
kubectl delete configmap otel-collector-config -n simetra-site-a
```

Remove namespace (removes everything):

```bash
kubectl delete namespace simetra-site-a
```

---

## Maintenance

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
kubectl -n simetra-site-a get lease snmp-collector-leader -o jsonpath='{.spec.holderIdentity}'
```

### Update SnmpCollector

1. Build new image on develop machine with new tag (e.g. `snmp-collector:v2.7`)
2. Save to tar, transfer to **all 3 VMs** via SCP
3. Import on each VM: `sudo k3s ctr images import snmp-collector-v2.7.tar`
4. Update image tag: `sed -i 's|snmp-collector:v2.6|snmp-collector:v2.7|' ~/deploy/deployment.yaml`
5. Apply: `kubectl apply -f ~/deploy/deployment.yaml`

### Change preferred leader to a different site

Edit the ConfigMap:

```bash
kubectl edit configmap snmp-collector-config -n simetra-site-a
```

Change `PreferredNode` to the desired node name (e.g. `simetra-k3s-b`).
Then restart the pods to pick up the config change:

```bash
kubectl rollout restart deployment/snmp-collector -n simetra-site-a
```

The pod on the new preferred node will acquire leadership within ~30 seconds.

### VM resource adjustment

Shut down the VM, adjust in Hyper-V Manager, restart:

```powershell
Stop-VM -Name "simetra-k3s-a"
Set-VM -Name "simetra-k3s-a" -MemoryStartupBytes 8GB -ProcessorCount 4
Start-VM -Name "simetra-k3s-a"
```
