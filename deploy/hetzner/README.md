# Deploying OpenMU to a Hetzner VPS (Singapore) with auto-deploy

This sets up a single small VPS that runs the whole all-in-one OpenMU server
(game + Postgres) close to Southeast-Asian players, and **auto-builds + restarts
every time you push to the `production` branch**.

How it works:

```
git push origin production
        │
        ▼
GitHub Actions  ──build image──►  GHCR (ghcr.io/foyo-ai/openmu:production)
        │
        └──ssh──►  VPS:  docker compose pull && up -d openmu-startup
```

The heavy .NET build runs on GitHub's free runners — **never** on the 4 GB box —
so a deploy can't OOM your live server. Postgres data lives in a named volume and
is never rebuilt.

---

## 0. One-time prerequisites

- A Hetzner Cloud account (the website is slow from Asia — be patient through signup,
  or use a VPN to an EU/US exit; it doesn't affect the server itself).
- An SSH keypair on your machine. If you don't have one:
  `ssh-keygen -t ed25519 -C "openmu-deploy"`

---

## 1. Create the server

**Recommended spec: `CPX21` in `Singapore` — 3 vCPU, 4 GB RAM, 80 GB, ~€7.55/mo.**
(`CPX11` / 2 GB works to start but is tight once player data grows.)

### Option A — `hcloud` CLI (skips the slow web console)

```bash
# install: https://github.com/hetznercloud/cli   (scoop install hcloud / brew install hcloud)
hcloud context create openmu          # paste an API token from the Hetzner console
hcloud ssh-key create --name mykey --public-key-from-file ~/.ssh/id_ed25519.pub

hcloud server create \
  --name openmu \
  --type cpx21 \
  --image ubuntu-24.04 \
  --location sin \
  --ssh-key mykey
```

Note the server's **public IPv4** from the output.

### Option B — web console

New Server → Location **Singapore** → Image **Ubuntu 24.04** → Type **CPX21** →
add your SSH key → Create.

---

## 2. Firewall (do NOT skip — this is the same TCP problem that ruled out Render)

The game ports must be open from the outside or clients can't connect. Use the
**Hetzner Cloud Firewall** (it sits outside the host, so Docker's iptables rules
can't accidentally bypass it).

Inbound TCP rules to allow (source `0.0.0.0/0` + `::/0`):

| Port(s)        | Purpose                         |
|----------------|---------------------------------|
| 22             | SSH (admin + deploy)            |
| 44405, 44406   | Connect server                  |
| 55901–55910    | Game servers                    |
| 55980          | Sub-server (RPC/chat)           |

**Do NOT open 8080 or 5432.** The admin panel is reached by SSH tunnel (step 8),
and Postgres has no published port at all.

```bash
hcloud firewall create --name openmu-fw
hcloud firewall add-rule openmu-fw --direction in --protocol tcp --port 22         --source-ips 0.0.0.0/0 --source-ips ::/0
hcloud firewall add-rule openmu-fw --direction in --protocol tcp --port 44405-44406 --source-ips 0.0.0.0/0 --source-ips ::/0
hcloud firewall add-rule openmu-fw --direction in --protocol tcp --port 55901-55910 --source-ips 0.0.0.0/0 --source-ips ::/0
hcloud firewall add-rule openmu-fw --direction in --protocol tcp --port 55980       --source-ips 0.0.0.0/0 --source-ips ::/0
hcloud firewall apply-to-resource openmu-fw --type server --server openmu
```

---

## 3. Prepare the box (Docker, swap, deploy user)

SSH in as root (`ssh root@<vps-ip>`) and run:

```bash
# Docker engine + compose plugin
curl -fsSL https://get.docker.com | sh

# 2 GB swap — cheap insurance against memory spikes on a 4 GB box
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab

# A non-root user for deploys, allowed to run docker
adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy
mkdir -p /home/deploy/.ssh && chmod 700 /home/deploy/.ssh
```

---

## 4. Set up the deploy SSH key (used by GitHub Actions)

On **your machine**, make a dedicated keypair for CI (separate from your personal key):

```bash
ssh-keygen -t ed25519 -f ~/.ssh/openmu_deploy -C "github-actions" -N ""
```

Add the **public** half to the VPS `deploy` user:

```bash
# paste the contents of ~/.ssh/openmu_deploy.pub into:
#   /home/deploy/.ssh/authorized_keys   (on the VPS, owned by deploy, chmod 600)
```

Keep the **private** half (`~/.ssh/openmu_deploy`) for step 7.

---

## 5. Put the compose files on the VPS

```bash
# as the deploy user
sudo mkdir -p /opt/openmu && sudo chown deploy:deploy /opt/openmu
cd /opt/openmu
git clone https://github.com/foyo-ai/openmu.git .
git checkout production           # create this branch first (step 7) or use master then switch
cd deploy/hetzner
cp .env.example .env
nano .env                         # set a strong DB_ADMIN_PW (openssl rand -base64 24)
```

> Only `deploy/hetzner/` is actually used at runtime — the clone is just a convenient
> way to get the compose + .env onto the box. The image itself comes from GHCR, not the clone.

---

## 6. Let the VPS pull from GHCR

The image GitHub builds is private by default. Easiest: make the GHCR package
**public** (one click, no secrets on the box):

- After the first successful Actions run, go to
  `https://github.com/foyo-ai/openmu/pkgs/container/openmu` → Package settings →
  Change visibility → Public.

*(Prefer to keep it private? Instead run on the VPS:
`echo <GHCR_PAT> | docker login ghcr.io -u <github-user> --password-stdin`
with a PAT that has `read:packages`.)*

---

## 7. Wire up GitHub → create the `production` branch

In the repo: **Settings → Secrets and variables → Actions → New repository secret**:

| Secret name   | Value                                            |
|---------------|--------------------------------------------------|
| `VPS_HOST`    | the VPS public IPv4                              |
| `VPS_USER`    | `deploy`                                         |
| `VPS_SSH_KEY` | full contents of `~/.ssh/openmu_deploy` (private)|

Then create and push the production branch:

```bash
git checkout -b production
git push -u origin production
```

That push triggers `.github/workflows/deploy.yml`: it builds the image, pushes it
to GHCR, and SSHes into the VPS to `pull && up -d`. Watch it under the repo's
**Actions** tab. (First build is slow — later builds are cached.)

> The very first time, the app boots against an empty volume, so OpenMU
> **initializes a fresh Season 6 database** automatically (it runs with `-autostart`).

---

## 8. Reach the admin panel (SSH tunnel — no public exposure)

```bash
ssh -L 8080:localhost:8080 deploy@<vps-ip>
# then open http://localhost:8080 in your browser
```

Default login: `admin` / `openmu` — **change it immediately.**

### Enable non-interactive schema updates (REQUIRED for auto-deploy)

In the admin panel → **System Configuration** → turn on **Auto Update Schema**, save.

Why this matters: when a future code change alters the data model, the server checks
the schema on startup. Without this flag it would prompt `y/n` on the console — but a
detached container has no console input, so the server would fail to start and your
auto-deploy would silently break. With the flag on, migrations apply automatically.

---

## 9. Point clients at the server

In the admin panel, set the **Connect Server / Game Server public address** to your
VPS public IPv4 (the connect server hands this address to clients, so it must be the
public IP, not `127.0.0.1`). Then configure your MU client's `ServerList`/host to the
same IP on port `44405`.

---

## 10. ⚠️ Prove the database survives a deploy (do this before trusting it)

A deploy pipeline that drops the DB on the second push would be a data-loss pipeline.
Verify persistence explicitly:

```bash
cd /opt/openmu/deploy/hetzner

# 1. Create a marker (e.g. register a test account in-game, or:)
docker compose exec database psql -U postgres -d openmu -c \
  "CREATE TABLE IF NOT EXISTS persist_check(t text); INSERT INTO persist_check VALUES('survived');"

# 2. Simulate an app deploy
docker compose pull openmu-startup && docker compose up -d openmu-startup

# 3. Simulate a full recreate (worse case)
docker compose down && docker compose up -d

# 4. Confirm the marker is still there
docker compose exec database psql -U postgres -d openmu -c "SELECT * FROM persist_check;"
# -> must print 'survived'.  Clean up:  DROP TABLE persist_check;
```

If the row survives both, your volume is correctly wired and real player data is safe.

---

## Day-to-day

- **Deploy:** merge/push to `production` → it builds and restarts automatically.
- **Manual deploy / re-run:** Actions tab → Deploy to Hetzner → Run workflow.
- **Roll back:** every build is also tagged with its commit SHA. To pin an old build,
  set `IMAGE_TAG=<sha>` in `.env` on the VPS and run `docker compose up -d openmu-startup`.
- **Logs:** `docker compose logs -f openmu-startup`
- **Backups (do this!):** schedule `pg_dump` via cron, e.g.
  `docker compose exec -T database pg_dump -U postgres openmu | gzip > /opt/openmu/backups/$(date +\%F).sql.gz`

---

## Notes / caveats

- **Postgres major version is pinned to 17** in `docker-compose.yml`. Don't bump it
  casually — a major upgrade needs `pg_dump`/restore (Postgres won't start on an older
  major's data dir).
- The DB has **no published port** and the admin panel is **localhost-only**; the only
  public surface is the game TCP ports. Keep it that way.
- To make the admin panel publicly reachable later (with HTTPS), use the
  `deploy/all-in-one` nginx + certbot setup with a domain instead of the SSH tunnel.
