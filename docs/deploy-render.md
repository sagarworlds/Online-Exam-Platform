# Step-by-step: host the Exam Platform on Render with a Neon database

**What you end up with**

| Part | Runs on | Cost |
|---|---|---|
| Website (Angular) | Render static site | Free |
| API (.NET) | Render web service (Docker) | Free |
| Database (Postgres) | Neon | Free |

**About this branch.** Everything Render needs lives on the branch `SP/feature/render-deployment` and Render deploys from that branch (`branch:` is set in `render.yaml`). It is kept separate from `main` on purpose: do not merge it, and keep it up to date with `main` only when you want new features deployed.

**Free-plan limits**

- The Render API sleeps after about 15 minutes without traffic; the first request afterwards takes up to a minute.
- The Neon database sleeps after 5 minutes idle and wakes in about a second. It holds 0.5 GB.
- The API can only e-mail sign-in codes. SMS is not supported yet, so candidates and staff must use e-mail.

**What you need before starting**

- A GitHub account with this repository, a Render account (render.com) and a Neon account (neon.tech); all can sign in with GitHub.
- Docker Desktop running on your machine, with the Postgres container that holds your data.
- A free Brevo account (brevo.com) to e-mail sign-in codes. Render's free plan blocks outbound SMTP, so mail is sent through Brevo's HTTPS API; see step 3.

---

## Step 1. Create the Neon database

1. Sign in to neon.tech and choose **Create project**. Name it `examplatform`, pick Postgres 16 or newer and the region nearest your users.
2. Open the project's **Dashboard** and choose **Connect**.
3. Turn **Connection pooling** off. The host must not contain `-pooler`.
4. Note the **host**, **database**, **role** and **password** (reveal the password).

Build two strings from them. Keep both somewhere private:

```
Npgsql string (for Render):
Host=<host>;Port=5432;Database=<database>;Username=<role>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true

URL (for the data copy):
postgresql://<role>:<password>@<host>/<database>?sslmode=require
```

## Step 2. Copy your existing data into Neon

Run these in PowerShell. They can run from any folder.

1. Find your database container:
   ```powershell
   docker ps --format "{{.Names}}  {{.Image}}"
   ```
   Use the name on the `postgres:16` row as `<name>` below. If nothing is listed, open the project folder and run `docker compose up -d postgres` first.
2. Make a dump inside the container (do not use `>`, PowerShell corrupts binary output):
   ```powershell
   docker exec <name> pg_dump -U examplatform -d examplatform --no-owner --no-privileges -Fc -f /tmp/examplatform.dump
   ```
3. Keep a backup copy on your machine:
   ```powershell
   docker cp <name>:/tmp/examplatform.dump $HOME\examplatform.dump
   ```
4. Restore into Neon using the URL from step 1:
   ```powershell
   docker exec <name> pg_restore --no-owner --no-privileges --dbname "<the URL>" /tmp/examplatform.dump
   ```
   Warnings about extensions or `public` schema can be ignored; errors mentioning `already exists` mean the database was not empty (see Troubleshooting).
5. Check the copy by comparing counts. Both numbers must match:
   ```powershell
   docker exec <name> psql -U examplatform -d examplatform -c "select count(*) from identity.\"Users\";"
   docker exec <name> psql "<the URL>" -c "select count(*) from identity.\"Users\";"
   ```

Your local database is not changed. Do not run `docker compose down -v` (it deletes the data volume) until you are sure the copy is complete. The dump holds real candidate data and password hashes: keep it out of git and delete it when you no longer need it.

## Step 3. Create a Brevo API key

1. In Brevo open **SMTP & API > API Keys** (not SMTP & API > SMTP — that page's "SMTP key" is for a direct SMTP connection, which Render's free plan blocks).
2. Choose **Generate a new API key**, name it (e.g. `exam-platform-render`) and copy it. It is shown only once.
3. Open **Senders, Domains & Dedicated IPs > Senders**, add the address you will send from, and confirm it through the e-mail Brevo sends. Brevo refuses mail from an unverified sender.

The free plan allows about 300 e-mails a day, which covers sign-in codes for a small cohort.

## Step 4. Create the services on Render

1. In Render choose **New > Blueprint**, pick this repository and apply `render.yaml`.
2. On the `exam-platform-api` service, **Environment**, fill in the values marked `sync: false`:

| Key | Value |
|---|---|
| `ConnectionStrings__Postgres` | The Npgsql string from step 1. |
| `Cors__AllowedOrigins__0` | The site's address, e.g. `https://exam-platform-web.onrender.com` (no trailing slash). |
| `Brevo__ApiKey` | The API key from step 3. |
| `Brevo__SenderEmail` | The verified sender address from step 3. |
| `Invite__LinkBaseUrl` | The site's address, so invitation links open the site. |

`Jwt__SigningKey` is generated for you. Everyone has to sign in again after it changes.

If Render gives the API a different address than `https://exam-platform-api.onrender.com`, change `API_BASE_URL` on the `exam-platform-web` service and redeploy it: the site is built with that address.

## Step 5. Point the website at the API

1. Open `exam-platform-api` and copy its address, e.g. `https://exam-platform-api.onrender.com`.
2. Open `exam-platform-web` > **Environment**. If `API_BASE_URL` differs from that address, change it.
3. Choose **Manual Deploy > Clear build cache & deploy**. The address is baked in when the site is built, so any change needs a rebuild.

## Step 6. Check that it works

1. Open `https://<api address>/v1/health`. It should answer `Healthy`. After a sleep this can take a minute.
2. In the API's **Logs**, look for `Migrated and seeded <Module>` once per module.
3. Open the website and sign in as a candidate with an e-mail address that exists in your data. The code arrives by e-mail.
4. Sign in as the administrator (password, then an e-mailed code). The administrator's address must be one you can read.
5. Open an exam you created earlier to confirm the data came across.

## Updating later

- Push to `SP/feature/render-deployment`: Render redeploys both services.
- To bring in new features from `main`, merge `main` into this branch (never the other way round), wait for the checks to pass, then push.
- Database changes are applied by the API on start. The data is not lost.

## Keeping keys out of GitHub

No real key is ever written to a file in the repository. The secrets live only in the Render and Neon dashboards:

- `render.yaml` lists the secret settings with `sync: false`. That means "ask for the value in the Render dashboard"; the value is stored by Render, never in git.
- The Neon password, the Brevo API key and the JWT signing key (generated by Render) are typed into **Environment** on Render. Do not paste them into code, docs, commit messages, issues or PR comments.
- `appsettings.Development.json` holds only throw-away local values (the Docker database password and a development signing key). Production ignores that file.
- Never commit the database dump, a `.env` file or a screenshot of the Environment page. `.env` is already in `.gitignore`.

If a key is ever pasted somewhere public, treat it as leaked: generate a new Brevo API key, reset the Neon role password, and update the value on Render. Deleting the commit is not enough, because GitHub keeps history.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| API log: `Jwt:SigningKey` or connection errors | A setting from step 4 is missing or mistyped. Check for spaces and the exact key names (double underscores). |
| API log: `password authentication failed` or SSL errors | Use the non-pooler host and keep `SSL Mode=Require;Trust Server Certificate=true`. |
| Browser shows a CORS error | `Cors__AllowedOrigins__0` must be the exact website address, without a trailing slash. |
| Website calls `localhost` or the wrong API | `API_BASE_URL` was wrong when the site was built. Fix it and redeploy with a cleared cache. |
| No sign-in e-mail arrives | Check the API log for `Brevo refused a message` or `did not respond in time`; verify `Brevo__ApiKey` is an **API key** (not an SMTP key) and `Brevo__SenderEmail` is verified in Brevo. Then check Brevo's **Transactional > Email Activity** log for the message's status. Check spam. |
| `pg_restore` says `already exists` | The API started before the restore. Run it again with `--clean --if-exists` added. |
| Everyone shares one rate limit | Render's proxy range differs. Adjust `ForwardedHeaders__KnownNetworks__0/1` to the range shown in the API log. |
