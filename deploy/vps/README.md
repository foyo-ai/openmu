# Deploying OpenMU to a cloud VPS (DigitalOcean Singapore) with auto-deploy

This runs the whole all-in-one OpenMU server (game + Postgres) on a single VPS close
to Southeast-Asian players, and **auto-builds + restarts every time you push to the
`production` branch**.

How it works:

```
git push origin production
        │
        ▼
GitHub Actions  ──build image──►  GHCR (ghcr.io/foyo-ai/openmu:production, private)
        │
        └──ssh──►  VPS:  docker login ghcr.io && docker compose pull && up -d
```

The heavy .NET build runs on GitHub's free runners — **never** on the VPS — so a deploy
can't OOM the live server. The image stays **private**: the deploy step logs in to GHCR
with the workflow's own short-lived token (no public package, no long-lived PAT). Postgres
data lives in a named volume (`openmu-dbdata`) and is never rebuilt on an app deploy.

---

## Current deployment (already live)

| | |
|---|---|
| Provider / region | DigitalOcean, **SGP1 (Singapore)** |
| Droplet | `openmu`, size `s-2vcpu-4gb` (2 vCPU, 4 GB, 80 GB), Ubuntu 24.04 — **$24/mo** |
| Public IP | `168.144.37.156` |
| App dir on server | `/opt/openmu/deploy/vps` |
| Admin panel | port 8080, **localhost-only** (reach via SSH tunnel) |
| Game ports (public) | 44405–44406 (connect), 55901–55910 (game), 55980 |
| Firewall | DO Cloud Firewall `openmu-fw` |

> Why DigitalOcean and not Hetzner? Hetzner is the bargain *in Europe*, but its **Singapore**
> prices are premium ($30.99/mo for the 4 GB box), so DO SGP1 (4 GB, $24/mo) was cheaper for
> this region. Everything below is provider-agnostic except the `doctl` commands.

---

## 0. Prerequisites

- A DigitalOcean account.
- `doctl` CLI authenticated: `doctl auth init` (paste an API token from
  **cloud.digitalocean.com → API → Tokens**). On Windows it installs to
  `%LOCALAPPDATA%\doctl\doctl.exe`.
- An SSH keypair for deploys. The CI/admin key here is `~/.ssh/openmu_deploy`
  (passphrase-less, used by both your admin SSH and GitHub Actions). Create one with:
  `ssh-keygen -t ed25519 -f ~/.ssh/openmu_deploy -N ""`

---

## 1. Import the SSH key into DigitalOcean

```bash
doctl compute ssh-key import openmu-deploy --public-key-file ~/.ssh/openmu_deploy.pub
doctl compute ssh-key list   # note the ID
```

---

## 2. Create the droplet (provisions itself via cloud-init)

Save this as `cloud-init.yml` (installs Docker, 2 GB swap, a `deploy` user, and the app dir):

```yaml
#cloud-config
package_update: true
write_files:
  - path: /home/deploy/.ssh/authorized_keys
    permissions: '0600'
    defer: true
    content: |
      <PASTE CONTENTS OF ~/.ssh/openmu_deploy.pub HERE>
runcmd:
  - curl -fsSL https://get.docker.com | sh
  - fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  - bash -c "echo '/swapfile none swap sw 0 0' >> /etc/fstab"
  - id -u deploy >/dev/null 2>&1 || useradd -m -s /bin/bash deploy
  - usermod -aG docker deploy
  - chown -R deploy:deploy /home/deploy
  - chmod 700 /home/deploy/.ssh
  - mkdir -p /opt/openmu/deploy/vps
  - chown -R deploy:deploy /opt/openmu
  - touch /opt/openmu/.cloudinit-done
```

Create it (replace `<KEY_ID>`):

```bash
doctl compute droplet create openmu \
  --region sgp1 \
  --size s-2vcpu-4gb \
  --image ubuntu-24-04-x64 \
  --ssh-keys <KEY_ID> \
  --user-data-file cloud-init.yml \
  --tag-name openmu \
  --wait \
  --format ID,Name,PublicIPv4,Status
```

Note the **public IP** and droplet ID. Cloud-init takes ~1–2 min; it's done when this prints `DONE`:

```bash
ssh -i ~/.ssh/openmu_deploy root@<IP> 'test -f /opt/openmu/.cloudinit-done && echo DONE || echo PENDING'
```

---

## 3. Firewall (do NOT skip — the game ports must be open or clients can't connect)

Use the DO Cloud Firewall (sits outside the host, so Docker's iptables can't bypass it).
Open **only** SSH + the game ports; admin (8080) and Postgres (5432) stay closed.

```bash
doctl compute firewall create --name openmu-fw \
  --droplet-ids <DROPLET_ID> \
  --inbound-rules "protocol:tcp,ports:22,address:0.0.0.0/0,address:::/0 protocol:tcp,ports:44405-44406,address:0.0.0.0/0,address:::/0 protocol:tcp,ports:55901-55910,address:0.0.0.0/0,address:::/0 protocol:tcp,ports:55980,address:0.0.0.0/0,address:::/0 protocol:icmp,address:0.0.0.0/0,address:::/0" \
  --outbound-rules "protocol:tcp,ports:all,address:0.0.0.0/0,address:::/0 protocol:udp,ports:all,address:0.0.0.0/0,address:::/0 protocol:icmp,address:0.0.0.0/0,address:::/0"
```

---

## 4. Put the compose file + secrets on the server

```bash
# copy the compose file up
scp -i ~/.ssh/openmu_deploy docker-compose.yml deploy@<IP>:/opt/openmu/deploy/vps/

# generate a strong DB password ON the server (never transits your shell history)
ssh -i ~/.ssh/openmu_deploy deploy@<IP> '
  cd /opt/openmu/deploy/vps
  PW=$(tr -dc "A-Za-z0-9" </dev/urandom | head -c 32)
  cat > .env <<EOF
DB_ADMIN_USER=postgres
DB_ADMIN_PW=$PW
OPENMU_IMAGE=ghcr.io/foyo-ai/openmu
IMAGE_TAG=production
EOF
  chmod 600 .env
'
```

---

## 5. Wire up GitHub → trigger the first deploy

Set repo secrets (**Settings → Secrets and variables → Actions**). Set `VPS_SSH_KEY` via
**bash/`gh`**, NOT PowerShell (PowerShell 5.1 re-encodes the pipe as UTF-16 and corrupts the
key, producing `ssh: no key found`):

```bash
gh secret set VPS_HOST    -R foyo-ai/openmu -b "<IP>"
gh secret set VPS_USER    -R foyo-ai/openmu -b "deploy"
cat ~/.ssh/openmu_deploy | gh secret set VPS_SSH_KEY -R foyo-ai/openmu
```

Then push the production branch:

```bash
git checkout -b production   # first time only
git push -u origin production
```

That triggers `.github/workflows/deploy.yml`: build → push to GHCR → SSH to the VPS →
`docker compose pull && up -d`. Watch it under the repo's **Actions** tab. On first boot the
app initializes a fresh Season 6 database automatically (it runs with `-autostart`).

---

## 6. Reach the admin panel (SSH tunnel — no public exposure)

```bash
ssh -L 8080:localhost:8080 -i ~/.ssh/openmu_deploy deploy@<IP>
# then open http://localhost:8080
```

Default login `admin` / `openmu` — **change it immediately.**

### Enable non-interactive schema updates (REQUIRED for auto-deploy)

Admin panel → **System Configuration** → turn on **Auto Update Schema**, save.

Without it, a future code change that alters the data model makes the server prompt `y/n`
on the console at startup — but a detached container has no console input, so the server
fails to start and auto-deploy silently breaks. With it on, migrations apply automatically.

---

## 7. Point clients at the server

The connect server auto-detects the droplet's public IP and hands it to clients. If you ever
need to override it, set the Connect/Game Server public address in the admin panel. Configure
your MU client's host to `<IP>` on port `44405`.

---

## 8. ⚠️ Prove the database survives a deploy (before trusting it with players)

```bash
cd /opt/openmu/deploy/vps
# create a marker
docker compose exec database psql -U postgres -d openmu -c \
  "CREATE TABLE IF NOT EXISTS persist_check(t text); INSERT INTO persist_check VALUES('survived');"
# simulate an app deploy + a full recreate
docker compose pull openmu-startup && docker compose up -d openmu-startup
docker compose down && docker compose up -d
# confirm it's still there
docker compose exec database psql -U postgres -d openmu -c "SELECT * FROM persist_check;"   # -> 'survived'
```

(Already verified on this server: real account data survived a live auto-deploy.)

---

## Day-to-day

- **Deploy:** merge/push to `production` → it builds and restarts automatically.
- **Manual deploy / re-run:** Actions tab → "Deploy to VPS" workflow → Run workflow.
- **Roll back:** every build is also tagged with its commit SHA. Pin an old build by setting
  `IMAGE_TAG=<sha>` in `.env` on the VPS and running `docker compose up -d openmu-startup`.
- **Logs:** `docker compose logs -f openmu-startup`
- **Backups (do this!):** cron a `pg_dump`, e.g.
  `docker compose exec -T database pg_dump -U postgres openmu | gzip > /opt/openmu/backups/$(date +\%F).sql.gz`
- **Stop billing:** `doctl compute droplet delete openmu` (⚠️ destroys the server; back up first).

---

## Notes / caveats

- **Compose project name is pinned** (`name: openmu` in `docker-compose.yml`) so containers/
  volumes are stable regardless of the directory name.
- The **server's compose file is managed manually** (scp'd) — the pipeline only does
  `pull && up -d`. If you change `docker-compose.yml`, copy it up again.
- **Postgres is pinned to v17.** Don't bump it casually — a major upgrade needs `pg_dump`/
  restore (Postgres won't start on an older major's data dir).
- Public surface is the game TCP ports only; admin (8080) is localhost, Postgres has no
  published port. Keep it that way.
- To expose the admin panel publicly with HTTPS later, use the `deploy/all-in-one` nginx +
  certbot setup with a domain instead of the SSH tunnel.
